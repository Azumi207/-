import {SHA256} from './sha256.js';
self.onmessage=async ({data:file})=>{try{const hash=new SHA256();for(let offset=0;offset<file.size;offset+=4194304){hash.update(await file.slice(offset,offset+4194304).arrayBuffer());self.postMessage({progress:Math.min(offset+4194304,file.size)/file.size});}self.postMessage({hash:hash.hex()});}catch{self.postMessage({error:'无法读取片源，请重新选择文件。'});}};
