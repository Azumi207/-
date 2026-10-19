"""Entry point logic tests. Browser/terminal is stubbed, not actual UI automation."""
import sys,time
from unittest.mock import Mock
import pytest
import launcher
from server import Store

def test_headless_first_start_fails_fast(tmp_path,monkeypatch):
    monkeypatch.setattr(sys,'argv',['launcher','--data',str(tmp_path),'--no-browser'])
    monkeypatch.setattr(sys,'stdin',Mock(isatty=lambda:False))
    with pytest.raises(SystemExit,match='交互式终端'):launcher.main()

def test_first_setup_and_open_only_when_ready(tmp_path,monkeypatch):
    monkeypatch.setattr(sys,'argv',['launcher','--data',str(tmp_path),'--port','19191'])
    monkeypatch.setattr(sys,'stdin',Mock(isatty=lambda:True))
    monkeypatch.setattr('builtins.input',lambda _: 'fixture_admin')
    monkeypatch.setattr(launcher.getpass,'getpass',lambda _: 'Disposable-setup-test-password')
    urls=[];monkeypatch.setattr(launcher.webbrowser,'open',lambda url:urls.append(url))
    class Server:
        started=False;should_exit=False
        def __init__(self,*args):pass
        def run(self):
            assert urls==[];self.started=True
            for _ in range(50):
                if urls:break
                time.sleep(.01)
            self.should_exit=True
    monkeypatch.setattr('uvicorn.Server',Server);launcher.main()
    assert urls==['http://127.0.0.1:19191/']
    with Store(tmp_path).tx() as c:assert c.execute('SELECT username FROM users WHERE admin=1').fetchone()[0]=='fixture_admin'

def test_nonloopback_refused_before_data_creation(tmp_path,monkeypatch):
    data=tmp_path/'untouched';monkeypatch.setattr(sys,'argv',['launcher','--data',str(data),'--host','0.0.0.0'])
    with pytest.raises(SystemExit,match='HTTPS'):launcher.main()
    assert not data.exists()
