// V14-6 · Modal 'mst-retire' (id = "<master>:<rowId>", ":sheet" appended when opened from the phone sheet 'mst-row'): the app's own
// confirmation step before MST_MASTER_RETIRE (same pattern as Cancel booking: heading, what happens, Back + the danger action) on desktop
// and phone. Reactivate needs no confirmation (MasterForm runs it at once). Back returns to the row's sheet on the phone.
import React from 'react';
import {Archive,Info,TriangleAlert} from 'lucide-react';
import {mstSpec,mstById,mstUsage} from '../../services/mock';
import {useHcc} from '../../app/context';

/** "Retire Floor 1?" body: what changes, and that old records keep the name. Pure text for the dialog and tests. */
export function mstRetireText(specId:string,name:string,code:string):string{
  if(specId==='rooms')return `Room ${code} leaves the room board, the calendar and New booking from today and can no longer be booked. Old records keep the name — past stays keep room ${code}.`;
  return `${name} leaves the pick lists, so it can no longer be chosen for new records. Old records keep the name.`;
}

export function MstRetireConfirm({id}:{id?:string}){
  const {s,run,busy,modal,open,closeModal}=useHcc();
  const [specId,rowId,from]=String(id??'').split(':');
  const spec=mstSpec(specId),row=spec&&rowId?mstById(s,specId,rowId):undefined;
  const back=()=>from==='sheet'?open({type:'mst-row',id:`${specId}:${rowId}`}):closeModal();
  if(!spec||!row)return <><div className="modal-heading"><h2>Retire</h2><p>This row no longer exists.</p></div>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button></div></>;
  const u=mstUsage(s,spec,row),blocked=row.active?spec.canRetire?.(s,row):`${row.name} is already retired.`;
  return <><div className="modal-heading"><div className="eyebrow">{spec.label.toUpperCase()} · {row.code}</div><h2>Retire {row.name}?</h2>
    <p>{mstRetireText(specId,row.name,row.code)}</p></div>
    {u.count>0&&<div className="info-note"><Info size={16}/>Used in {u.count} · {u.where.join('; ')} — these records stay as they are.</div>}
    {blocked&&<div className="info-note" role="alert"><TriangleAlert size={16}/>{blocked}</div>}
    <p className="muted">You can reactivate it at any time from the Retired filter of the list.</p>
    <div className="modal-actions"><button type="button" className="btn" onClick={back}>Back</button>
      <button type="button" className="btn danger" disabled={busy||!!blocked} onClick={()=>void run({type:'MST_MASTER_RETIRE',master:specId,id:row.id,key:modal?.key??`mst-retire:${specId}:${row.id}:${s.sequence}`},
        `${row.name} retired · old records keep the name`,{close:true})}><Archive size={15}/> Retire</button></div></>;
}
