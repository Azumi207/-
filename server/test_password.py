"""Password-confirmation regressions use disposable accounts, never real data."""
import sys,secrets
from unittest.mock import Mock
import pytest
import server,launcher
from test_server import service,ok

def seed(tmp_path):
    s=server.Store(tmp_path);s.add_user('fixture','Previous-fixture-password-123',True)
    with s.tx(True) as c:
        u=c.execute('SELECT id FROM users').fetchone()[0]
        c.execute('INSERT INTO sessions VALUES(?,?,?)',('fixture-session',u,9999999999))
        c.execute('INSERT INTO projects(id,title,description,created) VALUES(?,?,?,?)',('fixture-project','untouched','original',1))
    return s

def snapshot(s):
    with s.tx() as c:
        return dict(user=dict(c.execute('SELECT * FROM users').fetchone()),sessions=[tuple(r) for r in c.execute('SELECT * FROM sessions')],projects=[tuple(r) for r in c.execute('SELECT * FROM projects')])

def reset(s,monkeypatch,values):
    monkeypatch.setattr(sys,'argv',['server','--data',str(s.root),'reset-password','fixture'])
    prompt=Mock(side_effect=values);monkeypatch.setattr(server.getpass,'getpass',prompt);server.cli();return prompt

def test_reset_mismatch_leaves_password_sessions_and_projects(tmp_path,monkeypatch):
    s=seed(tmp_path);before=snapshot(s)
    with pytest.raises(server.HTTPException) as error:reset(s,monkeypatch,['New-fixture-password-123','Mistyped-fixture-password-123'])
    assert error.value.status_code==400 and '不一致' in error.value.detail
    assert snapshot(s)==before

def test_reset_matching_revokes_only_sessions_and_preserves_data(tmp_path,monkeypatch):
    s=seed(tmp_path);before=snapshot(s);new=secrets.token_urlsafe(24);prompt=reset(s,monkeypatch,[new,new]);after=snapshot(s)
    assert prompt.call_count==2 and server.PH.verify(after['user']['password'],new)
    assert after['sessions']==[] and after['projects']==before['projects'] and after['user']['id']==before['user']['id']
    with pytest.raises(server.VerificationError):server.PH.verify(after['user']['password'],'Previous-fixture-password-123')

def test_short_confirmed_password_does_not_write(tmp_path,monkeypatch):
    s=seed(tmp_path);before=snapshot(s)
    with pytest.raises(server.HTTPException):reset(s,monkeypatch,['short','short'])
    assert snapshot(s)==before

def test_cancel_confirmation_does_not_write(tmp_path,monkeypatch):
    s=seed(tmp_path);before=snapshot(s)
    with pytest.raises(KeyboardInterrupt):reset(s,monkeypatch,['New-fixture-password-123',KeyboardInterrupt()])
    assert snapshot(s)==before

def test_web_confirmation_api_and_legacy_native_compatibility(service):
    a=service.clients['admin'];name='confirm_fixture'
    ok(a,'POST','/api/users',400,json={'username':name,'password':service.password,'password_confirm':'mismatch'})
    assert not any(u['username']==name for u in ok(a,'GET','/api/users'))
    ok(a,'POST','/api/users',json={'username':name,'password':service.password,'password_confirm':service.password})
    c=service.login(name);assert ok(c,'GET','/api/me')['username']==name;c.close()
    ok(a,'POST','/api/users',json={'username':'legacy_native','password':service.password})

def test_recovery_refuses_missing_database_without_creating(tmp_path,monkeypatch):
    data=tmp_path/'missing';monkeypatch.setattr(sys,'argv',['launcher','--data',str(data),'--reset-password'])
    with pytest.raises(SystemExit,match='没有现有账号数据库'):launcher.main()
    assert not data.exists()

def test_recovery_uses_selected_existing_database(tmp_path,monkeypatch):
    s=seed(tmp_path);before=snapshot(s)
    monkeypatch.setattr(sys,'argv',['launcher','--data',str(tmp_path),'--reset-password']);monkeypatch.setattr(sys,'stdin',Mock(isatty=lambda:True))
    monkeypatch.setattr('builtins.input',lambda _: 'fixture');prompt=Mock(side_effect=['Recovered-fixture-password-123']*2);monkeypatch.setattr(server.getpass,'getpass',prompt)
    launcher.main();after=snapshot(s)
    assert prompt.call_count==2 and server.PH.verify(after['user']['password'],'Recovered-fixture-password-123')
    assert after['projects']==before['projects'] and after['sessions']==[]

def test_first_setup_mismatch_does_not_create_admin(tmp_path,monkeypatch):
    monkeypatch.setattr(sys,'argv',['launcher','--data',str(tmp_path),'--no-browser']);monkeypatch.setattr(sys,'stdin',Mock(isatty=lambda:True))
    monkeypatch.setattr('builtins.input',lambda _: 'fixture');monkeypatch.setattr(server.getpass,'getpass',Mock(side_effect=['First-fixture-password-123','Mismatched-fixture-password-123',KeyboardInterrupt()]))
    with pytest.raises(KeyboardInterrupt):launcher.main()
    with server.Store(tmp_path).tx() as c:assert not c.execute('SELECT * FROM users').fetchall()
