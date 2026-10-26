// test-only: serve production index.html and proxy /api to the API (same origin)
import http from 'node:http';import {readFile} from 'node:fs/promises';
const API=5000,PORT=5099;
http.createServer(async(req,res)=>{
 if(req.url.startsWith('/api')){
  const p=http.request({host:'127.0.0.1',port:API,path:req.url,method:req.method,headers:{...req.headers,host:'localhost:'+API}},r=>{res.writeHead(r.statusCode,r.headers);r.pipe(res);});
  p.on('error',()=>{res.writeHead(502);res.end('bad gateway');});req.pipe(p);return;}
 try{res.setHeader('Content-Type','text/html; charset=utf-8');res.end(await readFile(process.argv[2]));}catch(e){res.writeHead(500);res.end(String(e));}
}).listen(PORT,'127.0.0.1',()=>console.log('proxy up'));
