// Local preview server. Default: this PC only. "--lan": reachable from tablets/phones on the same network (DEC-036).
import http from 'node:http';
import os from 'node:os';
import {readFile} from 'node:fs/promises';
const lan=process.argv.includes('--lan');
const port=Number(process.env.PORT||4173),host=lan?'0.0.0.0':'127.0.0.1';
http.createServer(async(req,res)=>{
  try{
    const path=(req.url||'/').split('?')[0];
    if(path!=='/'&&path!=='/index.html'){res.writeHead(404);res.end('Not found');return;}
    res.setHeader('Content-Type','text/html; charset=utf-8');res.setHeader('Cache-Control','no-store');
    res.end(await readFile(new URL('./dist/index.html',import.meta.url)));
  }catch{res.writeHead(500);res.end('Run npm run build first.');}
}).listen(port,host,()=>{
  console.log(`Local:   http://127.0.0.1:${port}`);
  if(lan){
    const ips=Object.values(os.networkInterfaces()).flat().filter(i=>i&&i.family==='IPv4'&&!i.internal).map(i=>i.address);
    ips.forEach(ip=>console.log(`Network: http://${ip}:${port}`));
    console.log('LAN mode: allow Node.js through Windows Defender Firewall (Private networks) if a tablet cannot connect.');
    console.log('Each device keeps its own browser-local demo data — this is not shared or synchronised.');
  }
});
