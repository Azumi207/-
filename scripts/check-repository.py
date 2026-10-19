"""Read-only publication manifest checks; never walks user data directories."""
from pathlib import Path
import argparse,hashlib,json,re,subprocess,sys
ROOT=Path(__file__).resolve().parent.parent
def check(root=ROOT):
    meta=json.loads((root/'repository-files.json').read_text(encoding='utf-8'));names=meta['files']
    assert len(set(names))==len(names)
    blocked={'data','backups','bin','obj','.venv','.build-venv','__pycache__','.git','release-assets'}
    token=re.compile(rb'(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{50,}|-----BEGIN (?:RSA |OPENSSH |EC )?PRIVATE KEY-----)')
    for n in names:
        p=root/n;assert p.resolve().is_relative_to(root.resolve()) and not blocked.intersection(Path(n).parts),n
        assert p.is_file() and not p.is_symlink(),n
        assert not n.endswith(('.exe','.dll','.pdb','.sqlite3','.zip','.tar.gz')) and Path(n).name!='.env',n
        b=p.read_bytes();assert len(b)<2*1024*1024 and not token.search(b),n
        if n.endswith('.sh'):assert b.startswith(b'#!/bin/sh\n') and b'\r' not in b,n
    for n in ['windows-client/Frameflow.csproj','linux-server/install.sh','windows-server/start.cmd','docs/使用说明.md','README.md','LICENSE']:assert n in names
    print(json.dumps({'public_files':len(names),'private_data_files':0,'large_files':0,'known_token_patterns':0},sort_keys=True))
    return names
if __name__=='__main__':check()
