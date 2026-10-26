import {randomUUID,randomInt} from 'node:crypto';
export const BASE='http://127.0.0.1:5000/api/v1';
export async function call(method,path,body,{cookie,device,key}={}){
  const h={'Content-Type':'application/json'};if(cookie)h.Cookie=cookie;if(device)h['X-HCC-Device']=device;
  if(method==='POST')h['Idempotency-Key']=key||randomUUID();
  const r=await fetch(BASE+path,{method,headers:h,body:body?JSON.stringify(body):undefined});
  const t=await r.text();let j;try{j=JSON.parse(t)}catch{j=t}
  return {status:r.status,body:j,cookie:r.headers.get('set-cookie')?.split(';')[0]};
}
export const rnd=n=>Array.from({length:n},()=>'abcdefghjkmnpqrstuvwxyz23456789'[randomInt(31)]).join('');
