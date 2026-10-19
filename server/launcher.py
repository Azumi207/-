"""Shared desktop/Linux entry point. No embedded credentials."""
import argparse,getpass,os,sys,threading,time,webbrowser
from pathlib import Path
from server import Store,create_app,cli,HTTPException,VERSION

def main():
    if os.name!='nt':os.umask(0o077)
    # Keep administration/backup available in the frozen build without a Python install.
    base=Path(sys.executable).parent if getattr(sys,'frozen',False) else Path(__file__).parent
    if len(sys.argv)>1 and sys.argv[1]=='--cli':
        os.environ.setdefault('FRAMEFLOW_TEAM_DATA',str(base/'data'))
        sys.argv.pop(1);return cli()
    p=argparse.ArgumentParser(description='帧语工作台 · 双击启动网页版字幕组工作流')
    p.add_argument('--data',default=str(base/'data'));p.add_argument('--host',default='127.0.0.1');p.add_argument('--port',type=int,default=8765)
    p.add_argument('--no-browser',action='store_true');p.add_argument('--allow-insecure-http',action='store_true')
    p.add_argument('--reset-password',action='store_true',help='本机交互重置已有账号密码，不删除任何项目')
    p.add_argument('--serve-only',action='store_true',help='仅启动已初始化的服务，后台运行不弹出账号提示')
    p.add_argument('--max-video-gb',type=int,default=20);p.add_argument('--quota-gb',type=int,default=100)
    a=p.parse_args()
    if a.host not in {'127.0.0.1','localhost','::1'} and not a.allow_insecure_http:raise SystemExit('默认仅本机可见。远程使用请配置 HTTPS 反向代理；明确了解风险后才使用 --allow-insecure-http。')
    if not 1<=a.port<=65535:raise SystemExit('端口必须为 1–65535。')
    if not 1<=a.max_video_gb<=1000 or not a.max_video_gb<=a.quota_gb<=100000:raise SystemExit('无效存储限额。')
    data=Path(a.data).resolve()
    if a.reset_password:
        if not (data/'team.sqlite3').is_file():raise SystemExit('此目录没有现有账号数据库，不会新建或覆盖。请用 --data 指定实际 data 目录：'+str(data))
        if not sys.stdin.isatty():raise SystemExit('重置密码需要交互式终端，请双击重置密码.cmd。')
        print('正在重置此工作台的账号密码：'+str(data)+'\n项目和字幕不会删除；成功后该账号旧登录会话失效。')
        username=input('要重置的用户名 [admin]：').strip() or 'admin'
        sys.argv=[sys.argv[0],'--data',str(data),'reset-password',username]
        return cli()
    if a.serve_only and not (data/'team.sqlite3').is_file():raise SystemExit('工作台未初始化。请先运行 init 创建管理员，再启动后台服务。')
    data.mkdir(parents=True,exist_ok=True)
    # Refuse a second launcher on the same database, including a different port.
    lock=open(data/'.launcher.lock','a+b')
    try:
        if os.name=='nt':
            import msvcrt
            lock.seek(0);lock.write(b'0');lock.flush();lock.seek(0);msvcrt.locking(lock.fileno(),msvcrt.LK_NBLCK,1)
        else:
            import fcntl
            fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
    except OSError:
        lock.close();raise SystemExit('此数据目录已有服务运行。请使用原窗口，或先关闭它。')
    try:
        store=Store(data)
        print('\n  帧语工作台 / FRAMEFLOW TEAM '+VERSION+'\n  数据目录：'+str(data)+'\n',flush=True)
        with store.tx() as c:initialized=c.execute('SELECT 1 FROM users WHERE admin=1 AND active=1').fetchone()
        if not initialized:
            if a.serve_only:raise SystemExit('工作台未初始化或没有有效管理员；不会在后台设置密码。')
            if not sys.stdin.isatty():
                raise SystemExit('首次启动需要交互式终端。请双击启动工作台.cmd；自动化部署请先使用 --cli init --password-env 变量名。')
            print('首次使用：创建管理员（没有默认密码）。')
            while True:
                username=input('管理员用户名 [admin]：').strip() or 'admin'
                password=getpass.getpass('设置密码（至少 12 字符，输入时不显示）：')
                confirm=getpass.getpass('确认密码（再输入一次）：')
                if password!=confirm:print('两次密码不同，请重试。');continue
                try:store.add_user(username,password,True);break
                except HTTPException as e:print(str(e.detail))
            del password,confirm
            print('管理员已创建。请妥善保管密码。',flush=True)
        import uvicorn
        app=create_app(data,a.max_video_gb*1024**3,a.quota_gb*1024**3)
        server=uvicorn.Server(uvicorn.Config(app,host=a.host,port=a.port,workers=1,access_log=False,limit_concurrency=64,timeout_keep_alive=10,proxy_headers=False,ws='none',loop='asyncio',http='h11'))
        host='[::1]' if a.host=='::1' else '127.0.0.1';url=f'http://{host}:{a.port}/'
        def open_when_ready():
            while not server.started and not server.should_exit:time.sleep(.1)
            if server.started:
                print('网页已就绪：'+url+'\n保持本窗口运行；Ctrl+C 停止服务。',flush=True)
                if not a.no_browser:webbrowser.open(url)
        threading.Thread(target=open_when_ready,daemon=True).start()
        server.run()
    finally:lock.close()

if __name__=='__main__':
    import multiprocessing
    multiprocessing.freeze_support()
    try:main()
    except (HTTPException,EOFError,KeyboardInterrupt) as e:raise SystemExit(str(getattr(e,'detail',e)))
