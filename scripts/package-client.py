"""Package a fresh self-contained build; no GPL media binaries or developer data."""
from pathlib import Path
import argparse,hashlib,json,zipfile
ROOT=Path(__file__).resolve().parent.parent

def package(publish,output):
    publish=publish.resolve();output=output.resolve()
    assert publish.is_dir() and not output.exists() and not output.is_relative_to(publish)
    config=json.loads((publish/'Frameflow.runtimeconfig.json').read_text())['runtimeOptions']
    assert not config.get('frameworks') and not config.get('framework')
    frameworks={x['name']:x['version'] for x in config['includedFrameworks']}
    assert frameworks=={'Microsoft.NETCore.App':'9.0.21','Microsoft.WindowsDesktop.App':'9.0.21'}
    required=['Frameflow.exe','Frameflow.dll','coreclr.dll','hostfxr.dll','hostpolicy.dll','PresentationFramework.dll','start-client.cmd','install-ffmpeg.ps1','ffmpeg-provenance.json','LICENSE','THIRD_PARTY.md','Licenses/MoonSharp-LICENSE.txt']
    required += ['Licenses/microsoft.netcore.app.runtime.win-x64/'+n for n in ['LICENSE.TXT','THIRD-PARTY-NOTICES.TXT']]
    required += ['Licenses/microsoft.windowsdesktop.app.runtime.win-x64/LICENSE']
    for n in required:assert (publish/n).is_file(),n
    files={}
    for p in sorted(publish.rglob('*')):
        assert not p.is_symlink()
        if not p.is_file():continue
        n=p.relative_to(publish).as_posix()
        assert not {'Engine','data','backups','test-output','verification','.git'}.intersection(Path(n).parts),n
        assert p.suffix.lower() not in {'.sqlite3','.mp4','.ass','.srt','.zip','.env'},n
        if p.suffix.lower()=='.pdb':continue
        files[n]=p.read_bytes()
    files['启动帧语.cmd']=files['start-client.cmd']
    files['PACKAGE.json']=json.dumps({'edition':'self-contained-portable','application_version':'0.2.0','rid':'win-x64','runtime':frameworks,'system_runtime_required':False,'first_media_setup_needs_network':True,'files':{n:hashlib.sha256(b).hexdigest() for n,b in files.items()}},ensure_ascii=False,indent=2).encode('utf-8')
    output.parent.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(output,'x',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
        for n,b in sorted(files.items()):
            i=zipfile.ZipInfo('Frameflow/'+n,(2026,10,19,0,0,0));i.compress_type=zipfile.ZIP_DEFLATED;i.external_attr=0o100644<<16;z.writestr(i,b)
    with zipfile.ZipFile(output) as z:assert z.testzip() is None and len(z.namelist())==len(files)
    print(json.dumps({'archive':str(output),'files':len(files),'bytes':output.stat().st_size,'sha256':hashlib.sha256(output.read_bytes()).hexdigest(),'runtime':frameworks,'system_runtime_required':False},ensure_ascii=False))
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--publish',type=Path,required=True);p.add_argument('--output',type=Path,default=ROOT/'release-assets/Frameflow-Client-Windows-0.2.0-Portable-win-x64.zip');a=p.parse_args();package(a.publish,a.output)
