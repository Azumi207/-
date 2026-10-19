"""Generate a source-only archive from a reviewed manifest, not the working directory."""
from pathlib import Path
import importlib.util,json,zipfile,hashlib,argparse
ROOT=Path(__file__).resolve().parent.parent
def main():
    p=argparse.ArgumentParser();p.add_argument('--output',type=Path,default=ROOT/'release-assets/Frameflow-source-0.2.2.zip');a=p.parse_args()
    spec=importlib.util.spec_from_file_location('checks',ROOT/'scripts/check-repository.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);names=m.check()
    a.output.parent.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(a.output,'x',zipfile.ZIP_DEFLATED) as z:
        for n in sorted(names):
            i=zipfile.ZipInfo('Frameflow/'+n,(2026,10,19,0,0,0));i.create_system=3;i.compress_type=zipfile.ZIP_DEFLATED;i.external_attr=(0o100755 if n.endswith('.sh') else 0o100644)<<16;z.writestr(i,(ROOT/n).read_bytes())
    with zipfile.ZipFile(a.output) as z:assert z.testzip() is None
    print(hashlib.sha256(a.output.read_bytes()).hexdigest()+'  '+str(a.output.resolve()))
if __name__=='__main__':main()
