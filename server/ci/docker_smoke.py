"""Actual Docker/Compose acceptance check. Own random project only; no production data."""
import json,os,secrets,subprocess,tempfile,urllib.request
from pathlib import Path

ROOT=Path(__file__).resolve().parent.parent

def main():
    identity='frameflow-ci-'+secrets.token_hex(8)
    password=secrets.token_urlsafe(24)
    env=os.environ|{'FRAMEFLOW_CI_PASSWORD':password,'FRAMEFLOW_PORT':'0'}
    with tempfile.TemporaryDirectory(prefix=identity) as td:
        override=Path(td)/'override.json';override.write_text(json.dumps({'services':{'app':{'image':identity+':local'}}}))
        base=['docker','compose','-p',identity,'-f',str(ROOT/'compose.yaml'),'-f',str(override)]
        def run(*args,check=True):
            p=subprocess.run([*base,*args],cwd=ROOT,env=env,capture_output=True,text=True,encoding='utf-8')
            if check and p.returncode:raise RuntimeError(' '.join(args)+'\n'+p.stdout+'\n'+p.stderr)
            return p.stdout.strip()
        try:
            run('config','--quiet');run('build','--pull')
            run('run','--rm','-T','-e','FRAMEFLOW_CI_PASSWORD','app','init','--password-env','FRAMEFLOW_CI_PASSWORD')
            run('up','-d','--wait','--wait-timeout','120')
            assert run('exec','-T','app','id','-u')=='10001'
            port=int(run('port','app','8765').rsplit(':',1)[1])
            opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
            def api(path,data=None,token=None):
                headers={'Content-Type':'application/json'}
                if token:headers['Authorization']='Bearer '+token
                req=urllib.request.Request(f'http://127.0.0.1:{port}'+path,data=None if data is None else json.dumps(data).encode(),headers=headers)
                with opener.open(req,timeout=10) as r:return json.load(r)
            assert api('/api/health')['api']==1
            token=api('/api/login',{'username':'admin','password':password})['token']
            created=api('/api/projects',{'title':'Disposable Docker smoke'},token)
            run('exec','-T','app','python','/app/server.py','--data','/data','backup','/backups/smoke')
            run('exec','-T','app','test','-f','/backups/smoke/BACKUP_COMPLETE.txt')
            run('restart');run('up','-d','--wait','--wait-timeout','120')
            # Docker may allocate a new host port on restart when published port is 0.
            port=int(run('port','app','8765').rsplit(':',1)[1])
            assert api('/api/projects/'+created['id'],token=token)['title']=='Disposable Docker smoke'
            api('/api/logout',{},token)
            print('PASS: Compose build/init/health/nonroot/login/project/backup/restart persistence/logout')
        finally:
            # Only this invocation's random isolated project; never the default frameflow deployment.
            run('down','--volumes','--remove-orphans',check=False)
            subprocess.run(['docker','image','rm',identity+':local'],capture_output=True,check=False)

if __name__=='__main__':main()
