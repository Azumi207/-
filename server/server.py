"""Frameflow Team Server 0.1: single-group, single-process self-hosted workflow.

Storage is private; every project and file operation is authorized on the server.
Run `python server.py --help`. No default credentials, public registration or shell jobs.
"""
from __future__ import annotations
import argparse
import asyncio
import contextlib
import getpass
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import sqlite3
import time
import uuid

from argon2 import PasswordHasher
from argon2.exceptions import VerificationError, InvalidHashError
from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import FileResponse, Response
from starlette.concurrency import run_in_threadpool

VERSION = "0.2.2"
CHUNK = 4 * 1024 * 1024
ASS_LIMIT = 4 * 1024 * 1024
SESSION_SECONDS = 8 * 3600
ROLES = {"source", "translator", "reviewer", "encoder"}
PH = PasswordHasher(time_cost=3, memory_cost=65536, parallelism=1)

def fail(status, message):
    raise HTTPException(status, message)

def ident():
    return uuid.uuid4().hex

def digest(value):
    return hashlib.sha256(value).hexdigest()

def file_hash(path):
    with open(path, "rb") as f:
        return hashlib.file_digest(f, "sha256").hexdigest()

def password_ok(value):
    if not isinstance(value, str) or not 12 <= len(value) <= 128:
        fail(400, "密码长度应为 12–128 个字符。")
    return value

def username_ok(value):
    if not isinstance(value, str) or not re.fullmatch(r"[A-Za-z0-9_.-]{3,40}", value):
        fail(400, "用户名为 3–40 位英文字母、数字、点、下划线或横线。")
    return value.lower()

def confirmed_password(prompt="新密码（至少 12 字符，输入时不显示）: "):
    password=getpass.getpass(prompt)
    confirmation=getpass.getpass("确认密码（再输入一次）: ")
    if password!=confirmation: fail(400,"两次密码不一致，未保存。请重新操作。")
    return password_ok(password)

def text(value, limit, name, required=True):
    if not isinstance(value, str) or len(value) > limit or "\0" in value or required and not value.strip():
        fail(400, name + "无效或过长。")
    try: value.encode("utf-8")
    except UnicodeError: fail(400, name + "含有无效 Unicode。")
    return value

def integer(value, minimum=0, maximum=2**63-1):
    if type(value) is not int or not minimum <= value <= maximum:
        fail(400, "整数参数超出范围。")
    return value

def filename_ok(value):
    text(value, 180, "文件名")
    if any(c in value for c in '/\\:\r\n') or value in {".", ".."} or any(ord(c) < 32 for c in value):
        fail(400, "文件名不能包含路径或控制字符。")
    if Path(value).suffix.lower() not in {".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".m4v"}:
        fail(400, "请选择支持的视频文件扩展名。")
    return value

class Store:
    def __init__(self, data, max_video=20*1024**3, quota=100*1024**3):
        self.root = Path(data).resolve()
        self.root.mkdir(parents=True, exist_ok=True, mode=0o700)
        self.blobs = self.root / "blobs"
        self.parts = self.root / "uploads"
        self.blobs.mkdir(exist_ok=True, mode=0o700)
        self.parts.mkdir(exist_ok=True, mode=0o700)
        self.db = self.root / "team.sqlite3"
        self.max_video, self.quota = max_video, quota
        with contextlib.closing(self.connect()) as c:
            version = c.execute("PRAGMA user_version").fetchone()[0]
            if version not in (0, 1):
                raise RuntimeError("数据库版本不支持；不会自动清空或降级。")
            c.execute("PRAGMA journal_mode=WAL")
            c.executescript("""
            CREATE TABLE IF NOT EXISTS users(id TEXT PRIMARY KEY, username TEXT UNIQUE NOT NULL, password TEXT NOT NULL, admin INTEGER NOT NULL DEFAULT 0, active INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE IF NOT EXISTS sessions(token TEXT PRIMARY KEY, user_id TEXT NOT NULL REFERENCES users(id), expires INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS attempts(key TEXT PRIMARY KEY, started INTEGER NOT NULL, count INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS projects(id TEXT PRIMARY KEY, title TEXT NOT NULL, description TEXT NOT NULL, stage TEXT NOT NULL DEFAULT 'source_pending', revision INTEGER NOT NULL DEFAULT 0, owner_id TEXT REFERENCES users(id), media_id TEXT, latest_id INTEGER, approved_id INTEGER, submitter_id TEXT REFERENCES users(id), created INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS members(project_id TEXT NOT NULL REFERENCES projects(id), user_id TEXT NOT NULL REFERENCES users(id), role TEXT NOT NULL, PRIMARY KEY(project_id,user_id));
            CREATE TABLE IF NOT EXISTS files(id TEXT PRIMARY KEY, project_id TEXT NOT NULL REFERENCES projects(id), name TEXT NOT NULL, size INTEGER NOT NULL, sha256 TEXT NOT NULL, created INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS uploads(id TEXT PRIMARY KEY, project_id TEXT NOT NULL REFERENCES projects(id), user_id TEXT NOT NULL REFERENCES users(id), name TEXT NOT NULL, size INTEGER NOT NULL, sha256 TEXT NOT NULL, offset INTEGER NOT NULL DEFAULT 0, expires INTEGER NOT NULL, file_id TEXT);
            CREATE TABLE IF NOT EXISTS versions(id INTEGER PRIMARY KEY AUTOINCREMENT, project_id TEXT NOT NULL REFERENCES projects(id), number INTEGER NOT NULL, parent_id INTEGER, author_id TEXT NOT NULL REFERENCES users(id), kind TEXT NOT NULL, ass TEXT NOT NULL, sha256 TEXT NOT NULL, note TEXT NOT NULL, created INTEGER NOT NULL, UNIQUE(project_id,number));
            CREATE TABLE IF NOT EXISTS events(id INTEGER PRIMARY KEY AUTOINCREMENT, project_id TEXT NOT NULL REFERENCES projects(id), actor_id TEXT NOT NULL REFERENCES users(id), action TEXT NOT NULL, detail TEXT NOT NULL, created INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS session_expiry ON sessions(expires);
            CREATE INDEX IF NOT EXISTS project_versions ON versions(project_id,id);
            PRAGMA user_version=1;
            """)
        if os.name != "nt":
            os.chmod(self.root, 0o700)
            os.chmod(self.db, 0o600)

    def connect(self):
        c = sqlite3.connect(self.db, timeout=15, isolation_level=None)
        c.row_factory = sqlite3.Row
        c.execute("PRAGMA foreign_keys=ON")
        return c

    @contextlib.contextmanager
    def tx(self, write=False):
        c = self.connect()
        try:
            c.execute("BEGIN IMMEDIATE" if write else "BEGIN")
            yield c
            c.commit()
        except BaseException:
            c.rollback()
            raise
        finally:
            c.close()

    def user(self, request, c):
        auth = request.headers.get("authorization", "")
        if not auth.startswith("Bearer ") or not 20 <= len(auth[7:]) <= 200:
            fail(401, "请登录服务器。")
        row = c.execute("SELECT u.* FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token=? AND s.expires>? AND u.active=1", (digest(auth[7:].encode()), int(time.time()))).fetchone()
        if row is None:
            fail(401, "登录已失效，请重新登录。")
        return row

    def project(self, pid, user, c, role=None):
        row = c.execute("SELECT * FROM projects WHERE id=?", (pid,)).fetchone()
        membership = c.execute("SELECT role FROM members WHERE project_id=? AND user_id=?", (pid,user["id"])).fetchone()
        if row is None or not user["admin"] and membership is None:
            fail(404, "项目不存在或无权访问。")
        if role and not user["admin"] and membership["role"] != role:
            fail(403, "没有此项目阶段的操作权限。")
        return row

    def event(self, c, pid, uid, action, detail=""):
        c.execute("INSERT INTO events(project_id,actor_id,action,detail,created) VALUES(?,?,?,?,?)", (pid,uid,action,detail,int(time.time())))

    def view(self, c, p, user):
        d = dict(p)
        d["owner_name"] = c.execute("SELECT username FROM users WHERE id=?", (p["owner_id"],)).fetchone()[0] if p["owner_id"] else ""
        m = c.execute("SELECT role FROM members WHERE project_id=? AND user_id=?", (p["id"],user["id"])).fetchone()
        d["my_role"] = "admin" if user["admin"] else m["role"]
        d["media"] = dict(c.execute("SELECT * FROM files WHERE id=?",(p["media_id"],)).fetchone()) if p["media_id"] else None
        return d

    def revision(self, p, payload):
        if integer(payload.get("revision")) != p["revision"]:
            fail(409, "项目已被其他成员更新，请刷新；不会覆盖新版本。")

    def add_user(self, name, password, admin=False):
        name = username_ok(name)
        hashed = PH.hash(password_ok(password))
        with self.tx(True) as c:
            if c.execute("SELECT 1 FROM users WHERE username=?",(name,)).fetchone():
                fail(409,"用户名已存在。")
            uid=ident()
            c.execute("INSERT INTO users(id,username,password,admin) VALUES(?,?,?,?)",(uid,name,hashed,int(admin)))
        return {"id":uid,"username":name,"admin":admin}

async def body(request, limit=8*1024*1024, binary=False):
    declared=request.headers.get("content-length")
    if declared:
        try:
            if int(declared)<0 or int(declared)>limit: fail(413,"请求内容过大。")
        except ValueError: fail(400,"Content-Length 无效。")
    data=bytearray()
    async for chunk in request.stream():
        if len(data)+len(chunk)>limit: fail(413,"请求内容过大。")
        data.extend(chunk)
    if binary: return bytes(data)
    try:
        value=json.loads(data)
        if not isinstance(value,dict): raise ValueError()
        return value
    except (ValueError,UnicodeError): fail(400,"需要有效 JSON 对象。")

def create_app(data, max_video=20*1024**3, quota=100*1024**3):
    store=Store(data,max_video,quota)
    app=FastAPI(title="Frameflow Team",version=VERSION,docs_url=None,redoc_url=None,openapi_url=None)
    app.state.store=store
    auth_slots=asyncio.Semaphore(4)
    dummy=PH.hash(secrets.token_urlsafe(24))

    @app.middleware("http")
    async def security(request, call_next):
        # Same-origin web and native Bearer authentication; no cookies or query tokens.
        response=await call_next(request)
        response.headers["Cache-Control"]="no-store"
        response.headers["X-Content-Type-Options"]="nosniff"
        response.headers["Content-Security-Policy"]="default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; worker-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'"
        response.headers["Referrer-Policy"]="no-referrer"
        response.headers["Permissions-Policy"]="camera=(), microphone=(), geolocation=()"
        return response

    @app.get("/", include_in_schema=False)
    def web_index():
        return FileResponse(Path(__file__).parent/"web/index.html",media_type="text/html")

    @app.get("/assets/{name}", include_in_schema=False)
    def web_asset(name:str):
        allowed={"app.css":"text/css","app.js":"text/javascript","sha256.js":"text/javascript","hash-worker.js":"text/javascript","mark.svg":"image/svg+xml"}
        if name not in allowed: fail(404,"资源不存在。")
        return FileResponse(Path(__file__).parent/"web"/name,media_type=allowed[name])

    @app.get("/api/health")
    def health(): return {"service":"Frameflow Team","version":VERSION,"api":1}

    @app.post("/api/login")
    async def login(request:Request):
        b=await body(request,8192); name=username_ok(b.get("username")); password=text(b.get("password"),128,"密码")
        ip=request.client.host if request.client else "unknown"
        now=int(time.time())
        with store.tx(True) as c:
            for raw,cap in [("ip:"+ip,30),("user:"+name,10)]:
                key=digest(raw.encode());a=c.execute("SELECT * FROM attempts WHERE key=?",(key,)).fetchone()
                if a and now-a["started"]<60 and a["count"]>=cap: fail(429,"登录尝试过多，请一分钟后重试。")
                if not a or now-a["started"]>=60: c.execute("INSERT OR REPLACE INTO attempts VALUES(?,?,1)",(key,now))
                else: c.execute("UPDATE attempts SET count=count+1 WHERE key=?",(key,))
            c.execute("DELETE FROM attempts WHERE started<?",(now-86400,))
            u=c.execute("SELECT * FROM users WHERE username=?",(name,)).fetchone()
        def verify():
            try: return PH.verify(u["password"] if u else dummy,password)
            except (VerificationError,InvalidHashError): return False
        async with auth_slots: valid=await run_in_threadpool(verify)
        if not valid or not u or not u["active"]: fail(401,"用户名或密码错误。")
        token=secrets.token_urlsafe(32)
        with store.tx(True) as c:
            current=c.execute("SELECT * FROM users WHERE id=?",(u["id"],)).fetchone()
            if not current["active"] or current["password"]!=u["password"]: fail(401,"账号状态已变化，请重试。")
            c.execute("DELETE FROM sessions WHERE expires<=?",(now,))
            c.execute("DELETE FROM sessions WHERE user_id=? AND token NOT IN (SELECT token FROM sessions WHERE user_id=? ORDER BY expires DESC LIMIT 4)",(u["id"],u["id"]))
            c.execute("INSERT INTO sessions VALUES(?,?,?)",(digest(token.encode()),u["id"],now+SESSION_SECONDS))
        return {"token":token,"expires":now+SESSION_SECONDS,"user":{"id":u["id"],"username":name,"admin":bool(u["admin"])}}

    @app.post("/api/logout")
    def logout(request:Request):
        with store.tx(True) as c:
            store.user(request,c)
            c.execute("DELETE FROM sessions WHERE token=?",(digest(request.headers["authorization"][7:].encode()),))
        return {"ok":True}

    @app.get("/api/me")
    def me(request:Request):
        with store.tx() as c:
            u=store.user(request,c)
            return {"id":u["id"],"username":u["username"],"admin":bool(u["admin"])}

    @app.get("/api/users")
    def users(request:Request):
        with store.tx() as c:
            u=store.user(request,c)
            if not u["admin"]: fail(403,"需要管理员权限。")
            return [dict(x) for x in c.execute("SELECT id,username,admin,active FROM users ORDER BY username")]

    @app.post("/api/users")
    async def add_user(request:Request):
        with store.tx() as c:
            if not store.user(request,c)["admin"]: fail(403,"需要管理员权限。")
        b=await body(request,8192)
        # Optional for API-1 native clients, mandatory in the web creation form.
        if "password_confirm" in b and b.get("password")!=b["password_confirm"]:
            fail(400,"两次密码不一致，账号未创建。")
        async with auth_slots: return await run_in_threadpool(store.add_user,b.get("username"),b.get("password"),False)

    @app.get("/api/projects")
    def projects(request:Request):
        with store.tx() as c:
            u=store.user(request,c)
            rows=c.execute("SELECT * FROM projects ORDER BY created DESC,id") if u["admin"] else c.execute("SELECT p.* FROM projects p JOIN members m ON m.project_id=p.id WHERE m.user_id=? ORDER BY p.created DESC,p.id",(u["id"],))
            return [store.view(c,p,u) for p in rows]

    @app.post("/api/projects")
    async def new_project(request:Request):
        b=await body(request,16384)
        with store.tx(True) as c:
            u=store.user(request,c)
            if not u["admin"]: fail(403,"请管理员建立项目并分配成员。")
            pid=ident();c.execute("INSERT INTO projects(id,title,description,created) VALUES(?,?,?,?)",(pid,text(b.get("title"),150,"项目名"),text(b.get("description",""),4000,"说明",False),int(time.time())))
            store.event(c,pid,u["id"],"created")
            return store.view(c,store.project(pid,u,c),u)

    @app.get("/api/projects/{pid}")
    def project_detail(pid:str,request:Request):
        with store.tx() as c:
            u=store.user(request,c);p=store.project(pid,u,c);d=store.view(c,p,u)
            d["members"]=[dict(x) for x in c.execute("SELECT m.user_id,u.username,m.role FROM members m JOIN users u ON u.id=m.user_id WHERE m.project_id=?",(pid,))]
            pending=c.execute("SELECT id,name,size,offset,expires FROM uploads WHERE project_id=? AND user_id=? AND file_id IS NULL AND expires>? ORDER BY expires DESC LIMIT 1",(pid,u["id"],int(time.time()))).fetchone()
            d["pending_upload"]=dict(pending) if pending else None
            d["versions"]=[dict(x) for x in c.execute("SELECT v.id,v.number,v.parent_id,v.author_id,u.username author,v.kind,v.sha256,v.note,v.created FROM versions v JOIN users u ON u.id=v.author_id WHERE project_id=? ORDER BY v.id DESC",(pid,))]
            d["events"]=[dict(x) for x in c.execute("SELECT e.action,e.detail,e.created,u.username actor FROM events e JOIN users u ON u.id=e.actor_id WHERE project_id=? ORDER BY e.id DESC LIMIT 100",(pid,))]
            return d

    @app.post("/api/projects/{pid}/members")
    async def member(pid:str,request:Request):
        b=await body(request,8192)
        with store.tx(True) as c:
            u=store.user(request,c);p=store.project(pid,u,c)
            if not u["admin"]: fail(403,"需要管理员权限。")
            store.revision(p,b);role=b.get("role")
            if not isinstance(role,str) or role not in ROLES: fail(400,"成员角色无效。")
            target=c.execute("SELECT * FROM users WHERE username=? AND active=1",(username_ok(b.get("username")),)).fetchone()
            if not target: fail(404,"找不到有效账号。")
            if p["owner_id"]==target["id"]: fail(409,"请先释放此成员领取的任务。")
            c.execute("INSERT OR REPLACE INTO members VALUES(?,?,?)",(pid,target["id"],role));c.execute("UPDATE projects SET revision=revision+1 WHERE id=?",(pid,));store.event(c,pid,u["id"],"member",target["username"]+":"+role)
            return store.view(c,store.project(pid,u,c),u)

    @app.post("/api/projects/{pid}/claim")
    async def claim(pid:str,request:Request):
        b=await body(request,8192)
        with store.tx(True) as c:
            u=store.user(request,c);p=store.project(pid,u,c);store.revision(p,b)
            if p["stage"] not in {"translation_ready","review_ready"} or p["owner_id"]: fail(409,"任务不在待领取阶段或已被领取。")
            review=p["stage"]=="review_ready";store.project(pid,u,c,"reviewer" if review else "translator")
            if review and p["submitter_id"]==u["id"]: fail(403,"不能校对通过自己提交的初翻，请由另一位成员校对。")
            c.execute("UPDATE projects SET stage=?,owner_id=?,revision=revision+1 WHERE id=?",("reviewing" if review else "translating",u["id"],pid));store.event(c,pid,u["id"],"claim")
            return store.view(c,store.project(pid,u,c),u)

    @app.post("/api/projects/{pid}/release")
    async def release(pid:str,request:Request):
        b=await body(request,8192)
        with store.tx(True) as c:
            u=store.user(request,c);p=store.project(pid,u,c);store.revision(p,b)
            if p["stage"] not in {"translating","reviewing"} or not (u["admin"] or p["owner_id"]==u["id"]): fail(403,"只能释放自己领取的任务，管理员可释放搁置任务。")
            c.execute("UPDATE projects SET stage=?,owner_id=NULL,revision=revision+1 WHERE id=?",("translation_ready" if p["stage"]=="translating" else "review_ready",pid));store.event(c,pid,u["id"],"release")
            return store.view(c,store.project(pid,u,c),u)

    @app.post("/api/projects/{pid}/uploads")
    async def start_upload(pid:str,request:Request):
        b=await body(request,8192);name=filename_ok(b.get("name"));size=integer(b.get("size"),1,store.max_video);sha=b.get("sha256")
        if not isinstance(sha,str) or not re.fullmatch(r"[a-f0-9]{64}",sha): fail(400,"需要 SHA256 校验值。")
        with store.tx(True) as c:
            u=store.user(request,c);p=store.project(pid,u,c,"source")
            if p["stage"]!="source_pending" or p["media_id"]: fail(409,"片源已固定；更换片源请新建项目，避免时间轴错位。")
            old=c.execute("SELECT * FROM uploads WHERE project_id=? AND file_id IS NULL AND expires>?",(pid,int(time.time()))).fetchone()
            if old:
                if old["user_id"]==u["id"] and old["sha256"]==sha and old["size"]==size: return dict(old)
                fail(409,"此项目有未完成片源上传，请继续或取消原上传。")
            used=c.execute("SELECT COALESCE(SUM(size),0) FROM files").fetchone()[0]+c.execute("SELECT COALESCE(SUM(size),0) FROM uploads WHERE file_id IS NULL AND expires>?",(int(time.time()),)).fetchone()[0]
            if used+size>store.quota or shutil.disk_usage(store.root).free<size+256*1024**2: fail(507,"服务端空间或配额不足。")
            uid=ident();c.execute("INSERT INTO uploads(id,project_id,user_id,name,size,sha256,expires) VALUES(?,?,?,?,?,?,?)",(uid,pid,u["id"],name,size,sha,int(time.time())+86400))
            return dict(c.execute("SELECT * FROM uploads WHERE id=?",(uid,)).fetchone())

    def upload_auth(uid,request,c):
        u=store.user(request,c);up=c.execute("SELECT * FROM uploads WHERE id=?",(uid,)).fetchone()
        if not up or up["user_id"]!=u["id"]: fail(404,"上传任务不存在或不属于当前账号。")
        store.project(up["project_id"],u,c,"source")
        if up["expires"]<time.time() and not up["file_id"]: fail(410,"上传任务已过期，请重新创建。")
        return u,up

    @app.get("/api/uploads/{uid}")
    def upload_status(uid:str,request:Request):
        with store.tx() as c: return dict(upload_auth(uid,request,c)[1])

    @app.put("/api/uploads/{uid}")
    async def chunk(uid:str,request:Request):
        with store.tx() as c: upload_auth(uid,request,c)
        value=await body(request,CHUNK,True)
        try: offset=int(request.headers.get("upload-offset",""))
        except ValueError: fail(400,"缺少有效 Upload-Offset。")
        def write():
            with store.tx(True) as c:
                _,up=upload_auth(uid,request,c)
                if up["file_id"] or offset!=up["offset"]: fail(409,"上传偏移不一致，请查询进度后继续。")
                if not value or offset+len(value)>up["size"]: fail(400,"分块超出声明大小或为空。")
                path=store.parts/(uid+".part")
                actual=path.stat().st_size if path.exists() else 0
                if actual<offset: fail(409,"服务端临时文件不完整，请取消后重新上传。")
                with open(path,"r+b" if path.exists() else "w+b") as f:
                    f.truncate(offset);f.seek(offset);f.write(value);f.flush();os.fsync(f.fileno())
                c.execute("UPDATE uploads SET offset=?,expires=? WHERE id=?",(offset+len(value),int(time.time())+86400,uid))
                return {"id":uid,"offset":offset+len(value)}
        return await run_in_threadpool(write)

    @app.delete("/api/uploads/{uid}")
    def cancel_upload(uid:str,request:Request):
        with store.tx(True) as c:
            _,up=upload_auth(uid,request,c)
            if up["file_id"]: fail(409,"已提交片源不能通过取消上传删除。")
            c.execute("DELETE FROM uploads WHERE id=?",(uid,))
            (store.parts/(uid+".part")).unlink(missing_ok=True)
        return {"ok":True}

    @app.post("/api/uploads/{uid}/finish")
    def finish_upload(uid:str,request:Request):
        with store.tx() as c:
            u,up=upload_auth(uid,request,c)
            if up["file_id"]: return store.view(c,store.project(up["project_id"],u,c),u)
            if up["offset"]!=up["size"]: fail(409,"视频尚未上传完整。")
        part=store.parts/(uid+".part");dest=store.blobs/uid
        path=part if part.exists() else dest # recover a rename completed before a crashed DB commit
        if not path.is_file() or path.stat().st_size!=up["size"] or file_hash(path)!=up["sha256"]: fail(422,"视频 SHA256 或大小校验失败，请取消后重新上传。")
        with store.tx(True) as c:
            u,up=upload_auth(uid,request,c);p=store.project(up["project_id"],u,c,"source")
            if up["file_id"]: return store.view(c,p,u)
            if p["media_id"] or p["stage"]!="source_pending": fail(409,"项目片源已变化。")
            if part.exists(): os.replace(part,dest)
            c.execute("INSERT INTO files VALUES(?,?,?,?,?,?)",(uid,p["id"],up["name"],up["size"],up["sha256"],int(time.time())))
            c.execute("UPDATE uploads SET file_id=? WHERE id=?",(uid,uid))
            c.execute("UPDATE projects SET media_id=?,stage='translation_ready',revision=revision+1 WHERE id=?",(uid,p["id"]))
            store.event(c,p["id"],u["id"],"source_uploaded",up["sha256"])
            return store.view(c,store.project(p["id"],u,c),u)

    @app.get("/api/files/{fid}")
    def download(fid:str,request:Request):
        with store.tx() as c:
            u=store.user(request,c);f=c.execute("SELECT * FROM files WHERE id=?",(fid,)).fetchone()
            if not f: fail(404,"文件不存在。")
            store.project(f["project_id"],u,c)
            path=store.blobs/f["id"]
            if not path.is_file(): fail(503,"素材存储暂不可用，请联系管理员。")
            return FileResponse(path,media_type="application/octet-stream",filename=f["name"],headers={"ETag":'"'+f["sha256"]+'"',"X-Content-SHA256":f["sha256"]})

    @app.post("/api/projects/{pid}/versions")
    async def version(pid:str,request:Request):
        b=await body(request);ass=text(b.get("ass"),ASS_LIMIT,"ASS 字幕");kind=b.get("kind");note=text(b.get("note",""),8000,"提交说明",False)
        if len(ass.encode("utf-8"))>ASS_LIMIT or "[events]" not in ass.lower() or not re.search(r"(?im)^Dialogue\s*:",ass): fail(400,"需要 UTF-8 ASS/SSA 正文，至少包含一条 Dialogue；不执行任何字幕脚本。")
        if not isinstance(kind,str) or kind not in {"draft","submit"}: fail(400,"提交类型无效。")
        if b.get("base_version") is not None: integer(b["base_version"],1)
        with store.tx(True) as c:
            u=store.user(request,c);p=store.project(pid,u,c);store.revision(p,b)
            if p["owner_id"]!=u["id"] or p["stage"] not in {"translating","reviewing"}: fail(403,"请先领取此阶段任务。")
            store.project(pid,u,c,"translator" if p["stage"]=="translating" else "reviewer")
            if kind=="submit" and p["stage"]!="translating": fail(409,"校对请同步修改后使用退回或通过。")
            if b.get("base_version")!=p["latest_id"]: fail(409,"字幕基版本已变化，不允许覆盖他人修改。")
            number=c.execute("SELECT COALESCE(MAX(number),0)+1 FROM versions WHERE project_id=?",(pid,)).fetchone()[0]
            vid=c.execute("INSERT INTO versions(project_id,number,parent_id,author_id,kind,ass,sha256,note,created) VALUES(?,?,?,?,?,?,?,?,?)",(pid,number,p["latest_id"],u["id"],kind,ass,digest(ass.encode("utf-8")),note,int(time.time()))).lastrowid
            c.execute("UPDATE projects SET latest_id=?,revision=revision+1 WHERE id=?",(vid,pid))
            if kind=="submit": c.execute("UPDATE projects SET stage='review_ready',owner_id=NULL,submitter_id=? WHERE id=?",(u["id"],pid))
            store.event(c,pid,u["id"],kind,"v"+str(number)+": "+note)
            return store.view(c,store.project(pid,u,c),u)

    @app.get("/api/projects/{pid}/versions/{vid}")
    def subtitle(pid:str,vid:int,request:Request):
        with store.tx() as c:
            u=store.user(request,c);store.project(pid,u,c);v=c.execute("SELECT * FROM versions WHERE id=? AND project_id=?",(vid,pid)).fetchone()
            if not v: fail(404,"字幕版本不存在。")
            return Response(v["ass"].encode("utf-8"),media_type="application/octet-stream",headers={"Content-Disposition":f'attachment; filename="subtitle-v{v["number"]}.ass"',"X-Content-SHA256":v["sha256"]})

    @app.post("/api/projects/{pid}/review")
    async def review(pid:str,request:Request):
        b=await body(request,32768);action=b.get("action");note=text(b.get("note",""),8000,"校对意见",action=="return")
        if not isinstance(action,str) or action not in {"return","approve"}: fail(400,"校对操作无效。")
        integer(b.get("version_id"),1)
        with store.tx(True) as c:
            u=store.user(request,c);p=store.project(pid,u,c,"reviewer");store.revision(p,b)
            if p["stage"]!="reviewing" or p["owner_id"]!=u["id"] or p["submitter_id"]==u["id"]: fail(403,"请由另一位成员领取校对任务。")
            if not p["latest_id"] or b.get("version_id")!=p["latest_id"]: fail(409,"校对版本已变化，请刷新并确认。")
            c.execute("UPDATE projects SET stage=?,owner_id=NULL,approved_id=?,revision=revision+1 WHERE id=?",("encoding_ready" if action=="approve" else "translation_ready",p["latest_id"] if action=="approve" else None,pid))
            store.event(c,pid,u["id"],action,"version="+str(p["latest_id"])+": "+note)
            return store.view(c,store.project(pid,u,c),u)

    return app

def cli():
    parser=argparse.ArgumentParser(description="帧语字幕组服务端：一个服务端对应一个字幕组")
    parser.add_argument("--data",default=os.environ.get("FRAMEFLOW_TEAM_DATA",str(Path(__file__).parent/"data")))
    sub=parser.add_subparsers(dest="command",required=True)
    init=sub.add_parser("init",help="初始化管理员，无默认密码");init.add_argument("--username",default="admin");init.add_argument("--password-env",help="从指定环境变量读取初始密码，仅用于部署自动化")
    serve=sub.add_parser("serve");serve.add_argument("--host",default="127.0.0.1");serve.add_argument("--port",type=int,default=8765);serve.add_argument("--allow-insecure-http",action="store_true",help="确认直连 HTTP 风险；公网应使用 HTTPS 反向代理")
    serve.add_argument("--max-video-gb",type=int,default=20);serve.add_argument("--quota-gb",type=int,default=100)
    reset=sub.add_parser("reset-password");reset.add_argument("username")
    disable=sub.add_parser("disable-user");disable.add_argument("username")
    backup=sub.add_parser("backup");backup.add_argument("output")
    sub.add_parser("cleanup-uploads",help="停服后清理过期未完成上传")
    a=parser.parse_args();store=Store(a.data)
    if a.command=="init":
        with store.tx() as c:
            if c.execute("SELECT 1 FROM users WHERE admin=1").fetchone(): print("已有管理员，未覆盖。数据库："+str(store.db));return
        password=os.environ.get(a.password_env,"") if a.password_env else confirmed_password("设置管理员密码（至少 12 字符）: ")
        store.add_user(a.username,password,True);print("管理员已创建："+a.username+"；请妥善保管密码。")
    elif a.command=="serve":
        with store.tx() as c:
            if not c.execute("SELECT 1 FROM users WHERE admin=1 AND active=1").fetchone(): raise SystemExit("请先运行 init 创建管理员。")
        if a.host not in {"127.0.0.1","::1","localhost"} and not a.allow_insecure_http: raise SystemExit("默认仅绑定本机。公网请配置 HTTPS 反向代理；仅明确知悉风险时使用 --allow-insecure-http。")
        if not 1<=a.max_video_gb<=1000 or not a.max_video_gb<=a.quota_gb<=100000: raise SystemExit("无效存储限额。")
        import uvicorn
        uvicorn.run(create_app(a.data,a.max_video_gb*1024**3,a.quota_gb*1024**3),host=a.host,port=a.port,workers=1,access_log=False,limit_concurrency=64,timeout_keep_alive=10,proxy_headers=False)
    elif a.command in {"reset-password","disable-user"}:
        hashed=PH.hash(confirmed_password()) if a.command=="reset-password" else None
        with store.tx(True) as c:
            u=c.execute("SELECT * FROM users WHERE username=?",(username_ok(a.username),)).fetchone()
            if not u: raise SystemExit("账号不存在。")
            if a.command=="disable-user" and u["admin"]: raise SystemExit("此命令不禁用管理员，避免锁死管理入口。")
            if hashed: c.execute("UPDATE users SET password=? WHERE id=?",(hashed,u["id"]))
            else: c.execute("UPDATE users SET active=0 WHERE id=?",(u["id"],))
            c.execute("DELETE FROM sessions WHERE user_id=?",(u["id"],))
        print("账号已更新，原登录会话失效。")
    elif a.command=="backup":
        dest=Path(a.output).resolve()
        if dest==store.root or store.root in dest.parents: raise SystemExit("备份必须位于数据目录之外。")
        dest.mkdir(parents=True,exist_ok=False,mode=0o700)
        with contextlib.closing(store.connect()) as src,contextlib.closing(sqlite3.connect(dest/"team.sqlite3")) as target: src.backup(target)
        (dest/"blobs").mkdir()
        with contextlib.closing(sqlite3.connect(dest/"team.sqlite3")) as snapshot:
            for (fid,) in snapshot.execute("SELECT id FROM files"): shutil.copy2(store.blobs/fid,dest/"blobs"/fid)
            snapshot.execute("DELETE FROM sessions");snapshot.execute("DELETE FROM uploads WHERE file_id IS NULL")
            snapshot.commit()
        (dest/"BACKUP_COMPLETE.txt").write_text("Completed immutable files + SQLite snapshot; sessions revoked.\n",encoding="utf-8")
        print("备份完成："+str(dest))
    elif a.command=="cleanup-uploads":
        with store.tx(True) as c:
            ids=[row[0] for row in c.execute("SELECT id FROM uploads WHERE file_id IS NULL AND expires<?",(int(time.time()),))]
            for uid in ids: (store.parts/(uid+".part")).unlink(missing_ok=True)
            c.execute("DELETE FROM uploads WHERE file_id IS NULL AND expires<?",(int(time.time()),))
        print("过期上传已清理："+str(len(ids)))

if __name__=="__main__":
    try: cli()
    except HTTPException as e: raise SystemExit(str(e.detail))
