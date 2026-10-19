"""Read-only readiness check, without proxy env, credentials or third-party packages."""
import argparse,json,sys,urllib.request

def main():
    p=argparse.ArgumentParser();p.add_argument('--port',type=int,default=8765);a=p.parse_args()
    if not 1<=a.port<=65535:raise SystemExit('Invalid port')
    try:
        opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
        with opener.open(f'http://127.0.0.1:{a.port}/api/health',timeout=3) as response:
            data=json.loads(response.read(4096))
            if response.status!=200 or data.get('service')!='Frameflow Team' or data.get('api')!=1:raise ValueError('Unexpected service')
        print('Frameflow Team ready (API 1)')
    except Exception as error:print('Not ready: '+str(error),file=sys.stderr);raise SystemExit(1)

if __name__=='__main__':main()
