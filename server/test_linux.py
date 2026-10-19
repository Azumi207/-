"""Portable backend/package tests. These do not simulate a successful Linux deployment."""
import hashlib,importlib.util,json,os,subprocess,sys,tarfile,zipfile
from pathlib import Path,PurePosixPath
from unittest.mock import Mock
import pytest
import launcher
from server import Store
from test_server import service

ROOT=Path(__file__).parent
def module(path):
    spec=importlib.util.spec_from_file_location('fixture_module',ROOT/path);m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m

def test_serve_only_missing_directory_does_not_write(tmp_path,monkeypatch):
    data=tmp_path/'absent';monkeypatch.setattr(sys,'argv',['launcher','--data',str(data),'--serve-only'])
    with pytest.raises(SystemExit,match='未初始化'):launcher.main()
    assert not data.exists()

def test_serve_only_no_active_admin_never_prompts(tmp_path,monkeypatch):
    Store(tmp_path);monkeypatch.setattr(sys,'argv',['launcher','--data',str(tmp_path),'--serve-only'])
    prompt=Mock(side_effect=AssertionError('Unexpected password prompt'));monkeypatch.setattr('builtins.input',prompt)
    with pytest.raises(SystemExit,match='有效管理员'):launcher.main()
    prompt.assert_not_called()

def test_serve_only_configured_quota_and_no_browser(tmp_path,monkeypatch):
    Store(tmp_path).add_user('fixture','Disposable-Linux-password',True)
    monkeypatch.setattr(sys,'argv',['launcher','--data',str(tmp_path),'--serve-only','--no-browser','--max-video-gb','2','--quota-gb','5'])
    factory=Mock(return_value=object());monkeypatch.setattr(launcher,'create_app',factory)
    monkeypatch.setattr(launcher.threading,'Thread',Mock());browser=Mock();monkeypatch.setattr(launcher.webbrowser,'open',browser)
    config=Mock();server=Mock();monkeypatch.setattr('uvicorn.Config',config);monkeypatch.setattr('uvicorn.Server',Mock(return_value=server))
    launcher.main();factory.assert_called_once_with(tmp_path.resolve(),2*1024**3,5*1024**3);server.run.assert_called_once();browser.assert_not_called()
    assert config.call_args.kwargs['workers']==1 and config.call_args.kwargs['host']=='127.0.0.1'

@pytest.mark.parametrize('args',[['--port','0'],['--max-video-gb','0'],['--max-video-gb','4','--quota-gb','3']])
def test_invalid_options_before_data_write(tmp_path,monkeypatch,args):
    data=tmp_path/'absent';monkeypatch.setattr(sys,'argv',['launcher','--data',str(data),*args])
    with pytest.raises(SystemExit):launcher.main()
    assert not data.exists()

def test_healthcheck_against_real_http(service):
    p=subprocess.run([sys.executable,str(ROOT/'healthcheck.py'),'--port',str(service.port)],capture_output=True,text=True)
    assert p.returncode==0 and 'ready (API 1)' in p.stdout and not p.stderr

def test_healthcheck_wrong_service(monkeypatch):
    h=module('healthcheck.py');monkeypatch.setattr(sys,'argv',['healthcheck'])
    response=Mock(status=200);response.read.return_value=b'{"service":"another","api":1}';response.__enter__=Mock(return_value=response);response.__exit__=Mock(return_value=False)
    monkeypatch.setattr(h.urllib.request,'build_opener',Mock(return_value=Mock(open=Mock(return_value=response))))
    with pytest.raises(SystemExit) as error:h.main()
    assert error.value.code==1

def test_systemd_quoting_and_no_implicit_shell():
    m=module('linux/render_service.py');unit=m.render(PurePosixPath('/srv/Frame flow$%'),PurePosixPath('/srv/private data'),8765)
    assert 'WorkingDirectory="/srv/Frame flow$%%"' in unit
    assert '"/srv/Frame flow$$%%/.venv/bin/python"' in unit
    assert '--serve-only' in unit and 'sh -c' not in unit and 'UMask=0077' in unit
    with pytest.raises(ValueError):m.quote('path\nExecStart=bad')
    with pytest.raises(ValueError):m.render(Path('/srv'),Path('/data'),0)

def test_unit_writes_only_explicit_output_and_refuses_overwrite(tmp_path,monkeypatch):
    m=module('linux/render_service.py');data=tmp_path/'data';data.mkdir();(data/'team.sqlite3').touch();out=tmp_path/'fixture.service'
    monkeypatch.setattr(sys,'argv',['render','--data',str(data),'--output',str(out)]);m.main();before=out.read_bytes()
    with pytest.raises(SystemExit,match='already exists'):m.main()
    assert out.read_bytes()==before
    monkeypatch.setattr(sys,'argv',['render','--data',str(tmp_path/'missing'),'--output',str(tmp_path/'missing.service')])
    with pytest.raises(SystemExit,match='Initialize'):m.main()
    assert not (tmp_path/'missing.service').exists()

def test_release_archives_allowlist_hashes_modes_and_determinism(tmp_path):
    m=module('ci/package.py');first=m.build(tmp_path/'one');second=m.build(tmp_path/'two');assert [p.read_bytes() for p in first]==[p.read_bytes() for p in second]
    with pytest.raises(FileExistsError):m.build(tmp_path/'one')
    with zipfile.ZipFile(first[0]) as z,tarfile.open(first[1]) as t:
        assert z.testzip() is None
        assert set(z.namelist())==set(t.getnames())
        prefix=z.namelist()[0].split('/')[0]+'/'
        for line in z.read(prefix+'SHA256SUMS').decode().splitlines():
            sha,name=line.split('  ',1);assert hashlib.sha256(z.read(prefix+name)).hexdigest()==sha
        for n in z.namelist():
            assert not set(Path(n).parts)&{'data','backups','.venv','__pycache__'} and not n.endswith(('.sqlite3','.exe','.env'))
            assert z.read(n)==t.extractfile(n).read()
            if n.endswith('.sh'):assert t.getmember(n).mode==0o755 and b'\r' not in z.read(n)
        for n in ['LICENSE','compose.yaml','.github/workflows/ci.yml','web/sha256.js','web/mark.svg']:assert prefix+n in z.namelist()

def test_deployment_defaults_and_mit_present():
    compose=(ROOT/'compose.yaml').read_text();docker=(ROOT/'Dockerfile').read_text()
    assert '127.0.0.1:${FRAMEFLOW_PORT:-8765}:8765' in compose and 'read_only: true' in compose
    assert 'USER 10001:10001' in docker and 'HEALTHCHECK' in docker and 'MIT License' in (ROOT/'LICENSE').read_text()
    assert 'frameflow-data:/data' in compose and 'no-new-privileges:true' in compose

def test_packager_ignores_runtime_data_and_unlisted_files(tmp_path):
    import secrets
    m=module('ci/package.py');root=tmp_path/'source';canary=secrets.token_bytes(32)
    for n,b in m.collect().items():p=root/n;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b)
    for n in ['.env','data/team.sqlite3','backups/private.ass','web/unlisted-secret.txt']:
        p=root/n;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(canary)
    result=m.collect(root)
    assert all(canary not in value for value in result.values())
    assert set(result)==set(m.collect())

def test_actual_launcher_serve_only_restart_and_password_confirmation(tmp_path):
    import socket,time,httpx,secrets
    data=tmp_path/'data';password=secrets.token_urlsafe(24);Store(data).add_user('fixture',password,True)
    with socket.socket() as s:s.bind(('127.0.0.1',0));port=s.getsockname()[1]
    command=[sys.executable,str(ROOT/'launcher.py'),'--data',str(data),'--serve-only','--no-browser','--port',str(port)]
    with (tmp_path/'launcher.log').open('wb') as log,httpx.Client(base_url=f'http://127.0.0.1:{port}',trust_env=False,timeout=5) as client:
        project_id=None
        for round in range(2):
            process=subprocess.Popen(command,stdout=log,stderr=log)
            try:
                for _ in range(100):
                    assert process.poll() is None
                    try:
                        if client.get('/api/health').status_code==200:break
                    except httpx.TransportError:pass
                    time.sleep(.1)
                else:raise AssertionError('Launcher readiness timed out')
                result=client.post('/api/login',json={'username':'fixture','password':password});assert result.status_code==200
                client.headers['Authorization']='Bearer '+result.json()['token']
                if round==0:
                    assert client.post('/api/users',json={'username':'second','password':password,'password_confirm':'wrong'}).status_code==400
                    p=client.post('/api/projects',json={'title':'Linux entry point fixture'});assert p.status_code==200;project_id=p.json()['id']
                else:
                    p=client.get('/api/projects/'+project_id);assert p.status_code==200 and p.json()['title']=='Linux entry point fixture'
                assert client.post('/api/logout').status_code==200;del client.headers['Authorization']
            finally:
                process.terminate();process.wait(timeout=10)
