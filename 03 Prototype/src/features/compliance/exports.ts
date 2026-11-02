// Compliance calendar — the evidence ZIP (SCC exportEvidenceZip L2045–2098): the selected FY's files in <Month>_<Category>_<Name>__<itemId>
// folders with an index.csv manifest, file Evidence_<firm>_<FY>.zip. Demo: built on the device from s.docObjectsX (stored, not deflated —
// compression is not a rule); production: built by the hotel server (GET /compliance/evidence-zip) and saved here.
import {dobUrl} from '../../domain/streams/docobjects';
import {cmpSlice,cmpEvidenceZipPlan,cmpFyLabel,type State} from '../../services/mock';
import {zipStored} from '../../app/xlsx';
import {saveBinaryFile,saveAndReport} from '../../app/native';
import {getSyncRuntime} from '../../clients/sync/instance';
import {cmpDataUrlBytes} from './shared';

type SrvZip={fileName:string;files:number;base64:string};
const b64Bytes=(b64:string)=>{const bin=atob(b64),a=new Uint8Array(bin.length);for(let i=0;i<bin.length;i++)a[i]=bin.charCodeAt(i);return a;};

/** The demo's ZIP bytes (null when the FY has no files). */
export function cmpZipBytes(s:State):{fileName:string;bytes:Uint8Array;files:number}|null{
  const plan=cmpEvidenceZipPlan(s),enc=new TextEncoder(),files:{name:string;data:Uint8Array}[]=[];
  for(const e of plan.entries){const d=dobUrl(s,cmpSlice(s).files.find(f=>f.id===e.fileId)?.objectRef);if(!d)continue;try{files.push({name:e.path,data:cmpDataUrlBytes(d)});}catch{/* SCC skips an unreadable file */}}
  if(!files.length)return null;
  files.push({name:'index.csv',data:enc.encode(plan.index)});
  return {fileName:plan.fileName,bytes:zipStored(files),files:files.length-1};
}
export async function cmpSaveZip(s:State,toast:(t:string,e?:boolean)=>void){
  const x=cmpSlice(s);
  if(!x.profile){toast('Set the firm profile first to export its evidence pack.',true);return;}
  try{
    const z=process.env.HCC_BUILD==='production'
      ?await getSyncRuntime().api.get<SrvZip>('/compliance/evidence-zip',{fy:x.fyStart}).then(r=>r.files?{fileName:r.fileName,bytes:b64Bytes(r.base64),files:r.files}:null)
      :cmpZipBytes(s);
    if(!z){toast(`No evidence files are attached for ${x.profile.name} (${cmpFyLabel(x.fyStart)}).`,true);return;}
    await saveAndReport(saveBinaryFile(z.fileName,z.bytes,'application/zip'),toast,`${z.fileName} saved · ${z.files} file(s)`,'the evidence ZIP');
  }catch(e){toast('Could not build the evidence ZIP: '+String((e as {message?:string})?.message??e),true);}
}
