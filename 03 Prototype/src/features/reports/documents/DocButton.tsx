// F7-R7 · "Print (Lodge look)" button for the document layouts: builds the model with the pure builder (so the page shows the same figures as the screen it
// sits on), prints through the native hook (Android printer plugin; the browser prints a hidden frame), and writes the print-log row (rule V.4).
import React from 'react';
import {Printer} from 'lucide-react';
import {useHcc} from '../../../app/context';
import {isNative,printPage,printablePage} from '../../../app/native';
import {buildDocument,docHtml,docDef,type DocParams} from '../../../domain/reports/documents';

/** Browser print of a full HTML page in a hidden frame (the screen itself is not printed). */
function printFrame(page:string):Promise<'printed'|'failed'>{
  return new Promise(res=>{
    try{
      const f=document.createElement('iframe');f.setAttribute('aria-hidden','true');f.style.cssText='position:fixed;right:0;bottom:0;width:0;height:0;border:0';
      f.onload=()=>{try{f.contentWindow?.focus();f.contentWindow?.print();res('printed');}catch{res('failed');}setTimeout(()=>f.remove(),1500);};
      f.srcdoc=page;document.body.appendChild(f);
    }catch{res('failed');}
  });
}
export async function printDocModelPage(title:string,body:string,css:string){
  const page=printablePage(title,body,css);
  return isNative()?printPage(title,page):printFrame(page);
}
export function DocPrintButton({doc,params,label='Print (Lodge look)',className='btn'}:{doc:string;params:DocParams;label?:string;className?:string}){
  const {s,uid,run,toast}=useHcc();
  const def=docDef(doc);
  if(!def||!def.view(s,uid))return null;
  const go=async()=>{
    try{
      const m=buildDocument(s,uid,doc,params),h=docHtml(m);
      await printDocModelPage(h.title,h.body,h.css);
      await run({type:'RPT_LOG',reportId:doc,format:'print',params:Object.fromEntries(Object.entries(params).filter(([,v])=>v)),rows:m.tables.reduce((n,t)=>n+t.rows.length,0)});
    }catch(e){toast((e as Error).message,true);}
  };
  return <button type="button" className={className+' no-print'} data-doc={doc} onClick={()=>{void go();}}><Printer size={16}/> {label}</button>;
}
