"""Web entry point security and backward-compatible real HTTP tests."""
from pathlib import Path
import httpx
from test_server import service,ok,ready,change,ASS

def test_web_root_and_assets(service):
    with httpx.Client(base_url=service.url,trust_env=False) as c:
        r=c.get('/');assert r.status_code==200 and '<html lang="zh-CN">' in r.text
        for name,mime in [('app.js','javascript'),('app.css','css'),('sha256.js','javascript'),('hash-worker.js','javascript'),('mark.svg','svg')]:
            r=c.get('/assets/'+name);assert r.status_code==200 and mime in r.headers['Content-Type']
        print('ROOT 200; all five assets 200 with explicit MIME types')

def test_web_security_headers_and_allowlist(service):
    with httpx.Client(base_url=service.url,trust_env=False) as c:
        for path in ['/','/assets/app.js','/api/health','/api/projects']:
            r=c.get(path);csp=r.headers['Content-Security-Policy'];assert "script-src 'self'" in csp and 'unsafe-inline' not in csp and 'unsafe-eval' not in csp and "frame-ancestors 'none'" in csp
            assert r.headers['X-Content-Type-Options']=='nosniff' and r.headers['Cache-Control']=='no-store' and r.headers['Referrer-Policy']=='no-referrer'
        for path in ['/assets/server.py','/assets/../server.py','/assets/%2e%2e%2fserver.py','/data/team.sqlite3','/server.py','/assets/index.html']:
            assert c.get(path).status_code==404
        r=c.get('/api/projects',headers={'Origin':'https://untrusted.invalid','Cookie':'token=bogus'});assert r.status_code==401 and 'access-control-allow-origin' not in r.headers
        print('No source/data traversal; no cookies/query bearer; CSP and no permissive CORS verified')

def test_web_content_is_inert_and_session_memory_only(service):
    web=Path(__file__).with_name('web');js=(web/'app.js').read_text(encoding='utf-8');html=(web/'index.html').read_text(encoding='utf-8')
    for forbidden in ['localStorage','sessionStorage','document.cookie','eval(','new Function(']:assert forbidden not in js
    assert 'type="module" src="/assets/app.js"' in html and ' onclick=' not in html and '<style' not in html
    p=ok(service.clients['admin'],'POST','/api/projects',json={'title':'<img src=x onerror=alert(1)>','description':'<script>not executable</script>'})
    r=httpx.get(service.url+'/',trust_env=False);assert p['title'] not in r.text
    assert 'esc(p.title)' in js and 'esc(p.description' in js and "esc(e.action==='source_uploaded'?'SHA-256 已验证':e.detail)" in js and 'esc(v.note' in js
    print('Stored strings returned only as data; frontend escaping paths retained; token memory-only')

def test_same_api_supports_browser_workflow_and_native_clients(service):
    p=ready(service);t=service.clients['translator'];r=service.clients['reviewer']
    p=change(t,p,'claim');p=change(t,p,'versions',base_version=None,kind='submit',ass=ASS,note='网页交接')
    p=change(r,p,'claim');p=change(r,p,'review',version_id=p['latest_id'],action='return',note='补一个标点')
    p=change(t,p,'claim');p=change(t,p,'versions',base_version=p['latest_id'],kind='submit',ass=ASS.replace('中文初翻','中文初翻。'))
    p=change(r,p,'claim');p=change(r,p,'review',version_id=p['latest_id'],action='approve',note='通过')
    downloaded=ok(t,'GET',f'/api/projects/{p["id"]}/versions/{p["approved_id"]}')
    assert '{\\pos(960,900)}'.encode() in downloaded.content and '说话人'.encode() in downloaded.content
    assert p['stage']=='encoding_ready' and p['approved_id']==p['latest_id']
    print('SOURCE -> TRANSLATION -> REVIEW RETURN -> REVISION -> APPROVE; ASS style/actor preserved')
