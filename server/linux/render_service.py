"""Render a user service without shell interpolation. Does not run systemctl."""
from pathlib import Path
import argparse,os

def quote(value,exec_arg=False):
    if any(ord(c)<32 or ord(c)==127 for c in value):raise ValueError('Control character in systemd value')
    value=value.replace('\\','\\\\').replace('"','\\"').replace('%','%%')
    if exec_arg:value=value.replace('$','$$')
    return '"'+value+'"'

def render(source,data,port):
    if not 1<=port<=65535:raise ValueError('Invalid port')
    args=[str(source/'.venv/bin/python'),str(source/'launcher.py'),'--data',str(data),'--serve-only','--no-browser','--port',str(port)]
    return '\n'.join(['[Unit]','Description=Frameflow Team subtitle workspace','After=network.target','','[Service]','Type=simple','WorkingDirectory='+quote(str(source)),
        'ExecStart='+' '.join(quote(x,True) for x in args),'Environment=PYTHONUNBUFFERED=1','Environment=PYTHONDONTWRITEBYTECODE=1','UMask=0077','NoNewPrivileges=true','Restart=on-failure','RestartSec=5','TimeoutStopSec=30','KillSignal=SIGINT','','[Install]','WantedBy=default.target',''])

def main():
    p=argparse.ArgumentParser();p.add_argument('--data',required=True);p.add_argument('--port',type=int,default=8765);p.add_argument('--output',type=Path,default=Path.home()/'.config/systemd/user/frameflow.service');p.add_argument('--replace',action='store_true');a=p.parse_args()
    source=Path(__file__).resolve().parent.parent;data=Path(a.data).resolve()
    if not (data/'team.sqlite3').is_file():raise SystemExit('Initialize the intended data directory first; no service written.')
    if a.output.exists() and not a.replace:raise SystemExit('Service file already exists. Inspect it and pass --replace explicitly to update.')
    content=render(source,data,a.port);a.output.parent.mkdir(parents=True,exist_ok=True)
    a.output.write_text(content,encoding='utf-8',newline='\n');os.chmod(a.output,0o600);print(str(a.output.resolve()))

if __name__=='__main__':main()
