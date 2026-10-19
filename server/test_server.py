"""Real HTTP regression suite. Temporary generated data only; never connects to a user's server."""
import concurrent.futures
import hashlib
import json
import os
from pathlib import Path
import secrets
import socket
import sqlite3
import subprocess
import sys
import time

import httpx
import pytest

SERVER = Path(__file__).with_name('server.py')
ASS = '[Script Info]\nPlayResX: 1920\nPlayResY: 1080\n[V4+ Styles]\nFormat: Name, Fontname, Fontsize\nStyle: Default,Arial,48\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:00.00,0:00:02.00,Default,说话人,0,0,0,,{\\pos(960,900)}中文初翻\n'

class RunningServer:
    def __init__(self, folder):
        self.folder=Path(folder);self.data=self.folder/'data';self.password=secrets.token_urlsafe(24)
        with socket.socket() as s: s.bind(('127.0.0.1',0));self.port=s.getsockname()[1]
        self.url=f'http://127.0.0.1:{self.port}'
        env=dict(os.environ,FRAMEFLOW_TEST_INIT=self.password)
        p=subprocess.run([sys.executable,str(SERVER),'--data',str(self.data),'init','--password-env','FRAMEFLOW_TEST_INIT'],env=env,capture_output=True)
        assert p.returncode==0,(p.stdout,p.stderr)
        self.log=open(self.folder/'server.log','ab');self.start()
    def start(self):
        self.process=subprocess.Popen([sys.executable,str(SERVER),'--data',str(self.data),'serve','--port',str(self.port)],stdout=self.log,stderr=self.log)
        for _ in range(100):
            if self.process.poll() is not None: raise RuntimeError('Server exited: '+(self.folder/'server.log').read_text(errors='replace'))
            try:
                if httpx.get(self.url+'/api/health',timeout=1,trust_env=False).status_code==200: return
            except httpx.TransportError: pass
            time.sleep(.1)
        self.stop()
        raise RuntimeError('Server did not start: '+(self.folder/'server.log').read_text(errors='replace'))
    def stop(self):
        if self.process.poll() is None: self.process.terminate();self.process.wait(timeout=10)
    def login(self,name):
        c=httpx.Client(base_url=self.url,timeout=30,trust_env=False)
        r=c.post('/api/login',json={'username':name,'password':self.password});assert r.status_code==200,r.text
        c.headers['Authorization']='Bearer '+r.json()['token'];return c
    def cli(self,*args):
        p=subprocess.run([sys.executable,str(SERVER),'--data',str(self.data),*map(str,args)],capture_output=True)
        print('CLI',args[0],'exit',p.returncode,p.stdout.decode('utf-8',errors='replace'),p.stderr.decode('utf-8',errors='replace'));return p

@pytest.fixture(scope='module')
def service(tmp_path_factory):
    srv=RunningServer(tmp_path_factory.mktemp('team-http'));srv.clients={}
    admin=srv.login('admin');srv.clients['admin']=admin
    for name in ['source','translator','second','reviewer','outsider']:
        r=admin.post('/api/users',json={'username':name,'password':srv.password});assert r.status_code==200,r.text
        srv.clients[name]=srv.login(name)
    yield srv
    for c in srv.clients.values():c.close()
    srv.stop();srv.log.close()

def ok(c,method,url,expected=200,**kwargs):
    r=c.request(method,url,**kwargs);print(method,url,r.status_code);assert r.status_code==expected,r.text
    return r.json() if r.headers.get('content-type','').startswith('application/json') else r

def project(srv,title='fixture'):
    a=srv.clients['admin'];p=ok(a,'POST','/api/projects',json={'title':title})
    for name,role in [('source','source'),('translator','translator'),('second','translator'),('reviewer','reviewer')]:
        p=ok(a,'POST',f'/api/projects/{p["id"]}/members',json={'revision':p['revision'],'username':name,'role':role})
    return p

def upload(srv,p,data=b'generated-media-fixture',sha=None):
    c=srv.clients['source'];base=f'/api/projects/{p["id"]}'
    u=ok(c,'POST',base+'/uploads',json={'name':'fixture.mp4','size':len(data),'sha256':sha or hashlib.sha256(data).hexdigest()})
    for offset in range(u['offset'],len(data),4*1024*1024):
        ok(c,'PUT','/api/uploads/'+u['id'],content=data[offset:offset+4*1024*1024],headers={'Upload-Offset':str(offset)})
    return u

def ready(srv):
    p=project(srv);u=upload(srv,p)
    return ok(srv.clients['source'],'POST','/api/uploads/'+u['id']+'/finish')

def change(c,p,route,**kwargs):
    return ok(c,'POST',f'/api/projects/{p["id"]}/{route}',json={'revision':p['revision'],**kwargs})

def test_health_and_private_access(service):
    with httpx.Client(base_url=service.url,trust_env=False) as c:
        assert ok(c,'GET','/api/health')['api']==1
        ok(c,'GET','/api/projects',401);ok(c,'POST','/api/login',401,json={'username':'unknown','password':'not-the-password'})
        ok(c,'POST','/api/login',400,json={'username':'../bad','password':'x'})
        ok(c,'GET','/api/projects?token=not-supported',401)
    c=service.clients['translator'];ok(c,'POST','/api/users',403,json={'username':'newuser','password':service.password})
    ok(c,'POST','/api/projects',403,json={'title':'no'})
    with sqlite3.connect(service.data/'team.sqlite3') as db:
        assert all(row[0].startswith('$argon2id$') for row in db.execute('SELECT password FROM users'))
        assert all(len(row[0])==64 for row in db.execute('SELECT token FROM sessions'))
        assert service.password.encode() not in (service.data/'team.sqlite3').read_bytes()

def test_complete_return_revise_approve(service):
    t=service.clients['translator'];r=service.clients['reviewer'];p=ready(service)
    p=change(t,p,'claim');assert p['stage']=='translating'
    p=change(t,p,'versions',base_version=None,kind='draft',ass=ASS,note='初稿');assert p['stage']=='translating'
    first=p['latest_id'];p=change(t,p,'versions',base_version=first,kind='submit',ass=ASS,note='请校对');assert p['stage']=='review_ready'
    submitted=p['latest_id'];p=change(r,p,'claim');assert p['stage']=='reviewing'
    ok(r,'POST',f'/api/projects/{p["id"]}/review',400,json={'revision':p['revision'],'version_id':submitted,'action':'return','note':''})
    p=change(r,p,'review',version_id=submitted,action='return',note='请修改术语');assert p['stage']=='translation_ready'
    p=change(t,p,'claim');fixed=ASS.replace('中文初翻','修改后的术语');p=change(t,p,'versions',base_version=submitted,kind='submit',ass=fixed,note='已改')
    p=change(r,p,'claim');p=change(r,p,'versions',base_version=p['latest_id'],kind='draft',ass=fixed.replace('术语','术语。'),note='校对标点')
    p=change(r,p,'review',version_id=p['latest_id'],action='approve',note='通过');assert p['stage']=='encoding_ready' and p['approved_id']==p['latest_id']
    old=ok(t,'GET',f'/api/projects/{p["id"]}/versions/{first}');assert old.content==ASS.encode()
    detail=ok(t,'GET',f'/api/projects/{p["id"]}');assert len(detail['versions'])==4
    assert {'return','approve'}<={e['action'] for e in detail['events']}
    ok(t,'POST',f'/api/projects/{p["id"]}/versions',403,json={'revision':p['revision'],'base_version':p['latest_id'],'kind':'draft','ass':ASS})
    service.completed=p

def test_atomic_claim_and_conflicts(service):
    p=ready(service);url=f'/api/projects/{p["id"]}/claim'
    with concurrent.futures.ThreadPoolExecutor(2) as pool:
        result=list(pool.map(lambda name:(name,service.clients[name].post(url,json={'revision':p['revision']})),['translator','second']))
    assert sorted(r.status_code for _,r in result)==[200,409]
    name,response=next((n,r) for n,r in result if r.status_code==200);c=service.clients[name];p=response.json()
    ok(c,'POST',f'/api/projects/{p["id"]}/versions',409,json={'revision':p['revision']-1,'base_version':None,'kind':'draft','ass':ASS})
    ok(c,'POST',f'/api/projects/{p["id"]}/versions',409,json={'revision':p['revision'],'base_version':999,'kind':'draft','ass':ASS})
    p=change(c,p,'release');assert p['stage']=='translation_ready' and p['owner_id'] is None

def test_roles_membership_and_no_self_approval(service):
    p=ready(service);pid=p['id'];outsider=service.clients['outsider'];reviewer=service.clients['reviewer']
    assert ok(outsider,'GET','/api/projects')==[]
    ok(outsider,'GET','/api/projects/'+pid,404);ok(outsider,'GET','/api/files/'+p['media_id'],404,headers={'Range':'bytes=0-3'})
    ok(reviewer,'POST',f'/api/projects/{pid}/claim',403,json={'revision':p['revision']})
    a=service.clients['admin'];p=change(a,p,'claim');p=change(a,p,'versions',base_version=None,kind='submit',ass=ASS)
    ok(a,'POST',f'/api/projects/{pid}/claim',403,json={'revision':p['revision']})
    ok(outsider,'GET',f'/api/projects/{pid}/versions/{p["latest_id"]}',404)
    other=ready(service);ok(reviewer,'GET',f'/api/projects/{other["id"]}/versions/{p["latest_id"]}',404)

def test_resumable_upload_range_integrity_and_immutability(service):
    p=project(service);c=service.clients['source'];data=b'0123456789'*(500000);sha=hashlib.sha256(data).hexdigest();url=f'/api/projects/{p["id"]}/uploads'
    payload={'name':'视频.mp4','size':len(data),'sha256':sha};u=ok(c,'POST',url,json=payload)
    ok(c,'PUT','/api/uploads/'+u['id'],headers={'Upload-Offset':'0'},content=data[:100])
    resumed=ok(c,'POST',url,json=payload);assert resumed['id']==u['id'] and resumed['offset']==100
    ok(c,'PUT','/api/uploads/'+u['id'],409,headers={'Upload-Offset':'0'},content=b'x')
    ok(c,'POST','/api/uploads/'+u['id']+'/finish',409)
    ok(service.clients['translator'],'GET','/api/uploads/'+u['id'],404)
    for offset in range(100,len(data),4*1024*1024):ok(c,'PUT','/api/uploads/'+u['id'],headers={'Upload-Offset':str(offset)},content=data[offset:offset+4*1024*1024])
    p=ok(c,'POST','/api/uploads/'+u['id']+'/finish');again=ok(c,'POST','/api/uploads/'+u['id']+'/finish');assert p==again
    r=ok(service.clients['translator'],'GET','/api/files/'+p['media_id'],206,headers={'Range':'bytes=100-199'});assert r.content==data[100:200] and r.headers['etag']=='"'+sha+'"'
    whole=ok(c,'GET','/api/files/'+p['media_id']);assert hashlib.sha256(whole.content).hexdigest()==sha
    ok(c,'POST',url,409,json=payload);ok(c,'DELETE','/api/uploads/'+u['id'],409)

def test_invalid_uploads_and_size_limits(service):
    p=project(service);c=service.clients['source'];url=f'/api/projects/{p["id"]}/uploads'
    for name in ['../escape.mp4','x\\escape.mp4','C:evil.mp4','payload.exe']:
        ok(c,'POST',url,400,json={'name':name,'size':5,'sha256':'0'*64})
    ok(c,'POST',url,400,json={'name':'a.mp4','size':21*1024**3,'sha256':'0'*64})
    u=upload(service,p,b'wrong','0'*64);ok(c,'POST','/api/uploads/'+u['id']+'/finish',422)
    ok(c,'DELETE','/api/uploads/'+u['id']);ok(c,'GET','/api/uploads/'+u['id'],404)
    u=ok(c,'POST',url,json={'name':'a.mp4','size':5*1024*1024,'sha256':'0'*64})
    ok(c,'PUT','/api/uploads/'+u['id'],413,headers={'Upload-Offset':'0'},content=b'x'*(4*1024*1024+1))
    ok(c,'DELETE','/api/uploads/'+u['id'])

def test_invalid_ass_and_stale_review(service):
    p=ready(service);t=service.clients['translator'];r=service.clients['reviewer'];p=change(t,p,'claim')
    ok(t,'POST',f'/api/projects/{p["id"]}/versions',400,json={'revision':p['revision'],'base_version':None,'kind':'draft','ass':'not ASS'})
    p=change(t,p,'versions',base_version=None,kind='submit',ass=ASS);p=change(r,p,'claim')
    ok(r,'POST',f'/api/projects/{p["id"]}/review',409,json={'revision':p['revision'],'version_id':9999,'action':'approve','note':''})

def test_session_logout_expiry_disable(service):
    c=service.login('outsider');ok(c,'POST','/api/logout');ok(c,'GET','/api/me',401);c.close()
    c=service.login('outsider')
    with sqlite3.connect(service.data/'team.sqlite3') as db:db.execute('UPDATE sessions SET expires=0 WHERE token=?',(hashlib.sha256(c.headers['Authorization'][7:].encode()).hexdigest(),))
    ok(c,'GET','/api/me',401);c.close()
    c=service.login('outsider');assert service.cli('disable-user','outsider').returncode==0;ok(c,'GET','/api/me',401);c.close()

def test_restart_persistence_backup_restore(service):
    p=service.completed;service.stop();service.start();a=service.clients['admin'];assert ok(a,'GET','/api/projects/'+p['id'])['approved_id']==p['approved_id']
    dest=service.folder/'backup';assert service.cli('backup',dest).returncode==0
    assert (dest/'BACKUP_COMPLETE.txt').is_file()
    with sqlite3.connect(dest/'team.sqlite3') as db:
        assert db.execute('SELECT count(*) FROM sessions').fetchone()[0]==0
        assert db.execute('SELECT approved_id FROM projects WHERE id=?',(p['id'],)).fetchone()[0]==p['approved_id']
        for fid,sha in db.execute('SELECT id,sha256 FROM files'):assert hashlib.sha256((dest/'blobs'/fid).read_bytes()).hexdigest()==sha
    original=service.data;service.stop();service.data=dest;service.start()
    ok(a,'GET','/api/projects',401);restored=service.login('admin');assert ok(restored,'GET','/api/projects/'+p['id'])['stage']=='encoding_ready';restored.close()
    service.stop();service.data=original;service.start()

def test_non_loopback_refuses_unsafe_default(service):
    p=service.cli('serve','--host','0.0.0.0');assert p.returncode!=0

def test_expired_upload_cleanup(service):
    p=project(service);u=upload(service,p,b'partialfixture')
    with sqlite3.connect(service.data/'team.sqlite3') as db:db.execute('UPDATE uploads SET expires=0 WHERE id=?',(u['id'],))
    ok(service.clients['source'],'GET','/api/uploads/'+u['id'],410)
    service.stop();assert service.cli('cleanup-uploads').returncode==0;service.start()
    assert not (service.data/'uploads'/(u['id']+'.part')).exists()

def test_login_throttle(service):
    with httpx.Client(base_url=service.url,trust_env=False) as c:
        for _ in range(10):ok(c,'POST','/api/login',401,json={'username':'bruteforce','password':'wrong'})
        ok(c,'POST','/api/login',429,json={'username':'bruteforce','password':'wrong'})

def test_invalid_json_types(service):
    p=ready(service);a=service.clients['admin'];t=service.clients['translator']
    ok(a,'POST',f'/api/projects/{p["id"]}/members',400,json={'revision':p['revision'],'username':'translator','role':[]})
    p=change(t,p,'claim')
    for payload in [{'kind':[],'base_version':None,'ass':ASS},{'kind':'draft','base_version':True,'ass':ASS},{'kind':'draft','base_version':None,'ass':ASS+'\ud800'}]:
        # JSON escaped surrogate fixture is valid transport but not UTF-8 subtitle text.
        ok(t,'POST',f'/api/projects/{p["id"]}/versions',400,content=json.dumps({'revision':p['revision'],**payload}),headers={'Content-Type':'application/json'})
