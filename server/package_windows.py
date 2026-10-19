"""Generate a new portable Windows package; never include database or credentials."""
from pathlib import Path
import argparse,hashlib,json,os,subprocess,sys,zipfile
from server import VERSION
from importlib.metadata import distribution

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--output',required=True);a=parser.parse_args()
    if os.name!='nt':raise SystemExit('Generate the Windows executable on Windows.')
    source=Path(__file__).resolve().parent;out=Path(a.output).resolve();out.mkdir(parents=True,exist_ok=False)
    commands=[sys.executable,'-m','PyInstaller','--noconfirm','--onedir','--console','--noupx','--name','FrameflowServer','--distpath',str(out/'app'),'--workpath',str(out/'build'),'--specpath',str(out/'build'),'--add-data',str(source/'web')+':web','--collect-all','argon2','--collect-all','uvicorn','--hidden-import','_cffi_backend','--copy-metadata','fastapi','--copy-metadata','starlette','--copy-metadata','anyio','--copy-metadata','opentelemetry-api',str(source/'launcher.py')]
    print(json.dumps({'command':commands}),flush=True);p=subprocess.run(commands,cwd=source)
    if p.returncode:raise SystemExit(p.returncode)
    bundle=out/'app/FrameflowServer'
    (bundle/'启动工作台.cmd').write_text('@echo off\r\ncd /d "%~dp0"\r\n"%~dp0FrameflowServer.exe" %*\r\nif errorlevel 1 pause\r\n',encoding='ascii',newline='')
    (bundle/'重置密码.cmd').write_text('@echo off\r\ncd /d "%~dp0"\r\n"%~dp0FrameflowServer.exe" --reset-password %*\r\npause\r\n',encoding='ascii',newline='')
    for name in ['网页工作台使用说明.txt','README.md','API.md']:(bundle/name).write_bytes((source/name).read_bytes())
    for name in ['LICENSE','THIRD_PARTY.md']:(bundle/name).write_bytes((source/name).read_bytes())
    # Preserve runtime dependency license notices in redistributed standalone packages.
    licenses=bundle/'ThirdPartyLicenses';licenses.mkdir()
    pylicense=Path(sys.base_prefix)/'LICENSE.txt'
    if pylicense.exists():(licenses/'Python-LICENSE.txt').write_bytes(pylicense.read_bytes())
    import re
    for name in re.findall(r'^([A-Za-z0-9_-]+)==',(source/'requirements.txt').read_text(),re.M):
        dist=distribution(name)
        for file in dist.files or []:
            if file.name.lower().startswith(('license','copying','notice')):
                original=Path(dist.locate_file(file))
                if original.is_file():
                    dest=licenses/name/str(file);dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(original.read_bytes())
    paths=[p for p in bundle.rglob('*') if p.is_file()]
    assert not any('data' in p.relative_to(bundle).parts or p.suffix=='.sqlite3' for p in paths)
    zip_path=out/('Frameflow-Team-Web-Windows-'+VERSION+'.zip')
    with zipfile.ZipFile(zip_path,'w',zipfile.ZIP_DEFLATED) as z:
        for p in paths:z.write(p,'Frameflow-Team-Web/'+p.relative_to(bundle).as_posix())
    with zipfile.ZipFile(zip_path) as z:
        assert z.testzip() is None
        for p in paths:assert z.read('Frameflow-Team-Web/'+p.relative_to(bundle).as_posix())==p.read_bytes()
    result={'archive':str(zip_path),'sha256':hashlib.file_digest(open(zip_path,'rb'),'sha256').hexdigest(),'entries':len(paths),'bytes':zip_path.stat().st_size,'data_included':False,'executable':str(bundle/'FrameflowServer.exe')}
    (out/'package-result.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8');print(json.dumps(result,ensure_ascii=True),flush=True)

if __name__=='__main__':main()
