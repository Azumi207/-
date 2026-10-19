"""Build data-free, deterministic source ZIP and Linux tar.gz using an allowlist."""
import argparse,gzip,hashlib,io,re,tarfile,zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parent.parent
ROOT_FILES='''.dockerignore .env.example .gitattributes .gitignore API.md README.md LICENSE
CONTRIBUTING.md SECURITY.md CHANGELOG.md THIRD_PARTY.md Dockerfile compose.yaml
Caddyfile.example bootstrap.sh install.sh install-native.sh frameflow.sh start.sh
docker-entrypoint.sh launcher.py server.py healthcheck.py requirements.txt
requirements-test.txt requirements-build.txt start.cmd start.ps1 build-windows.ps1
package_windows.py 生成Windows整包.cmd 网页工作台使用说明.txt
test_server.py test_web.py test_launcher.py test_password.py test_linux.py test_linux_shell.py'''.split()
NESTED='''linux/install-user-service.sh linux/render_service.py docs/LINUX.md docs/WINDOWS.md
ci/package.py ci/docker_smoke.py .github/workflows/ci.yml'''.split()

def collect(root=ROOT):
    # Explicit runtime static whitelist mirrors server.py; never recursive user files.
    names=ROOT_FILES+NESTED+['web/'+n for n in ['index.html','app.js','app.css','hash-worker.js','sha256.js','mark.svg']]
    result={}
    for name in sorted(names):
        path=root/name
        if any(p.is_symlink() for p in [path,*path.parents] if p!=root.parent):
            raise ValueError('Symlinks are not release inputs: '+name)
        data=path.read_bytes()
        if name.endswith('.sh') and (b'\r' in data or not data.startswith(b'#!/bin/sh\n')):
            raise ValueError('Shell scripts must be POSIX sh / LF: '+name)
        result[name]=data
    return result

def build(output,root=ROOT):
    files=collect(root)
    version=re.search(r'^VERSION\s*=\s*[\'"]([0-9.]+)',files['server.py'].decode(),re.M).group(1)
    prefix='frameflow-team-'+version
    files['SHA256SUMS']=''.join(hashlib.sha256(b).hexdigest()+'  '+n+'\n' for n,b in sorted(files.items())).encode()
    output=Path(output);output.mkdir(parents=True,exist_ok=True)
    paths=[output/(prefix+'-source.zip'),output/(prefix+'-linux.tar.gz')]
    if any(p.exists() for p in paths):raise FileExistsError('Release exists; choose a new output directory.')
    with zipfile.ZipFile(paths[0],'x',zipfile.ZIP_DEFLATED) as z:
        for n,b in sorted(files.items()):
            i=zipfile.ZipInfo(prefix+'/'+n,(2026,10,19,0,0,0));i.create_system=3;i.compress_type=zipfile.ZIP_DEFLATED
            i.external_attr=(0o100755 if n.endswith('.sh') else 0o100644)<<16;z.writestr(i,b)
    with paths[1].open('xb') as raw,gzip.GzipFile(filename='',mode='wb',fileobj=raw,mtime=0) as gz,tarfile.open(fileobj=gz,mode='w') as tar:
        for n,b in sorted(files.items()):
            i=tarfile.TarInfo(prefix+'/'+n);i.size=len(b);i.mtime=0;i.mode=0o755 if n.endswith('.sh') else 0o644
            tar.addfile(i,io.BytesIO(b))
    for p in paths:print(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+str(p.resolve()))
    return paths

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--output',type=Path,default=ROOT/'dist');build(p.parse_args().output)
