"""Execute apphost with an empty .NET root; inspect runtime selection and real tests.

Does not uninstall/rename any installed runtime or modify machine configuration.
For release validation use a disposable extracted package with Engine installed.
"""
from pathlib import Path
import argparse,json,os,subprocess,sys

def main():
    p=argparse.ArgumentParser();p.add_argument('--app',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--expect-framework-dependent',action='store_true');a=p.parse_args()
    app=a.app.resolve();out=a.output.resolve();out.mkdir(parents=True,exist_ok=False)
    empty=out/'empty-dotnet';empty.mkdir();trace=out/'host-trace.txt';tests=out/'core';config=json.loads((app/'Frameflow.runtimeconfig.json').read_text())['runtimeOptions']
    env=os.environ|{'DOTNET_ROOT':str(empty),'DOTNET_ROOT_X64':str(empty),'DOTNET_MULTILEVEL_LOOKUP':'0','COREHOST_TRACE':'1','COREHOST_TRACEFILE':str(trace),'COREHOST_TRACE_VERBOSITY':'4','DOTNET_CLI_TELEMETRY_OPTOUT':'1'}
    env['PATH']=os.pathsep.join(x for x in env.get('PATH','').split(os.pathsep) if 'dotnet' not in x.lower())
    command=[str(app/'Frameflow.exe'),'--self-test',str(tests)]
    run=subprocess.run(command,env=env,cwd=app,capture_output=True,timeout=180)
    (out/'stdout.bin').write_bytes(run.stdout);(out/'stderr.bin').write_bytes(run.stderr)
    text=trace.read_text(encoding='utf-8-sig',errors='replace') if trace.exists() else ''
    result={'command':command,'input':'--self-test with empty DOTNET_ROOT_X64, DOTNET_ROOT; no dotnet PATH; multilevel lookup disabled','exit_status':run.returncode,'stdout':run.stdout.decode('utf-8',errors='replace'),'stderr':run.stderr.decode('utf-8',errors='replace'),'runtime_config':config,'trace':str(trace),'installed_runtimes_modified':False}
    (out/'result.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    if a.expect_framework_dependent:
        assert config.get('frameworks') and not config.get('includedFrameworks')
        assert run.returncode!=0 and ('hostfxr' in text.lower() or '.net' in result['stderr'].lower()),'Framework-dependent baseline unexpectedly used installed runtime'
        print('BASELINE: runtime-dependent app fails with empty runtime root; machine runtimes untouched.')
    else:
        assert not config.get('frameworks') and not config.get('framework') and config.get('includedFrameworks')
        assert run.returncode==0,result['stderr']
        assert 'TOTAL: 288 passed' in (tests/'core-tests.txt').read_text(encoding='utf-8-sig')
        assert 'coreclr.dll' in text.lower() and str(app).lower() in text.lower()
        # host trace must resolve the CLR and WPF library inside THIS package, not installed shared frameworks.
        for line in text.splitlines():
            if 'coreclr path =' in line.lower():assert str(app/'coreclr.dll').lower() in line.lower(),line
        print('PASS: app-local self-contained runtime selected; 288 actual core tests passed; no system runtime installation.')
    print(json.dumps(result,ensure_ascii=False))
if __name__=='__main__':main()
