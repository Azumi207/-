"""POSIX syntax and MOCK command dispatch. Passing is not proof of Linux/Docker execution."""
import os,shutil,subprocess
from pathlib import Path
import pytest

ROOT=Path(__file__).parent.resolve()
SH=shutil.which('sh') if os.name!='nt' else r'C:\Program Files\Git\bin\bash.exe'
pytestmark=pytest.mark.skipif(not SH or not Path(SH).is_file(),reason='POSIX shell not available')
def posix(p):
    s=str(p).replace('\\','/')
    return '/'+s[0].lower()+s[2:] if os.name=='nt' and len(s)>1 and s[1]==':' else s

def execute(script,args,tmp_path,fail_init=False):
    root=tmp_path/'source with spaces';root.mkdir(exist_ok=True)
    for name in ['install.sh','frameflow.sh','docker-entrypoint.sh']:
        (root/name).write_bytes((ROOT/name).read_bytes())
    bin=tmp_path/'bin';bin.mkdir(exist_ok=True);log=tmp_path/'commands.log'
    for name in ['docker','python']:
        (bin/name).write_text('#!/bin/sh\nprintf "%s\\n" "$*" >> "$CALL_LOG"\ncase "$*" in *"app init"*) [ "${FAIL_INIT:-0}" = 0 ] || exit 41 ;; esac\nexit 0\n',encoding='utf-8',newline='\n');(bin/name).chmod(0o755)
    env=os.environ|{'CALL_LOG':posix(log),'FAIL_INIT':'1' if fail_init else '0','FRAMEFLOW_TEAM_DATA':posix(tmp_path/'not initialized')}
    p=subprocess.run([SH,'-c','export PATH="$1:$PATH"; shift; exec sh "$@"','mock-runner',posix(bin),posix(root/script),*args],env=env,capture_output=True,text=True,encoding='utf-8')
    return p,log.read_text(encoding='utf-8') if log.exists() else ''

def test_all_posix_scripts_parse():
    for script in ROOT.rglob('*.sh'):
        if '.venv' in script.parts:continue
        assert b'\r' not in script.read_bytes()
        p=subprocess.run([SH,'-n',str(script)],capture_output=True,text=True)
        assert p.returncode==0,p.stderr

def test_mock_install_order_and_forward_username(tmp_path):
    p,log=execute('install.sh',['--username','fixture_admin'],tmp_path)
    assert p.returncode==0,p.stderr
    assert log.splitlines()==['compose version','compose build --pull','compose run --rm --no-deps app init --username fixture_admin','compose up -d --wait --wait-timeout 120']

def test_mock_failed_initialization_never_starts(tmp_path):
    p,log=execute('install.sh',[],tmp_path,True)
    assert p.returncode==41 and 'compose up' not in log

def test_mock_backup_before_upgrade_and_no_volume_deletion(tmp_path):
    p,log=execute('frameflow.sh',['upgrade'],tmp_path)
    assert p.returncode==0,p.stderr
    assert log.index('backup /backups/')<log.index('compose cp')<log.index('compose build --pull')<log.index('compose up')
    assert 'down' not in log and 'volume rm' not in log

def test_mock_reset_and_entrypoint_guard(tmp_path):
    p,log=execute('frameflow.sh',['reset-password','fixture'],tmp_path);assert p.returncode==0
    assert 'compose exec app python /app/server.py --data /data reset-password fixture' in log
    other=tmp_path/'second';other.mkdir();p,log=execute('docker-entrypoint.sh',['reset-password','fixture'],other)
    assert p.returncode==1 and 'No existing database' in p.stderr and not log

def test_mock_entrypoint_serve_only(tmp_path):
    p,log=execute('docker-entrypoint.sh',['serve'],tmp_path);assert p.returncode==0,p.stderr
    assert '--no-browser --serve-only --host 0.0.0.0 --allow-insecure-http --max-video-gb 20 --quota-gb 100' in log
