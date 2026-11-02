// LF-ALERTS · dialogs: 'rmd-edit' (new / edit reminder) and 'shortcuts' (keyboard shortcut list, user menu › Keyboard shortcuts).
import React,{useState} from 'react';
import {useHcc} from '../../app/context';
import {rmdCanManage,rmdSlice,rmdLeadDefault,rmdRoleOf,RMD_REPEATS,SHORTCUTS,shortcutAllowed,type Role} from '../../services/mock';
import {Empty} from '../../components/ui';
import type {ModalProps} from '../../app/registry';

const ROLES:Role[]=['Owner','Manager','Reception','Cashier','Accountant','Supervisor','Housekeeper','Maintenance','Tech admin'];
export function RmdEditModal({id}:ModalProps){
  const {s,uid,run,modal,busy,closeModal,permission}=useHcc();
  const r=id?rmdSlice(s).reminders.find(x=>x.id===id):undefined;
  const boss=['Owner','Manager'].includes(rmdRoleOf(s,uid)??'');
  const [f,setF]=useState({subject:r?.subject??'',date:r?.date??s.date,time:r?.time??'',leadDays:String(r?.leadDays??rmdLeadDefault(s)),role:r?.role??'all',repeat:r?.repeat??'none',note:r?.note??''});
  if(!rmdCanManage(s,uid))return <Empty title="Not permitted" body="Reminders need PERM-RMD-001 (Owner, Manager, Reception, Accountant)."/>;
  const set=(p:Partial<typeof f>)=>setF(x=>({...x,...p}));
  const mine=rmdRoleOf(s,uid);const roles:(Role|'all')[]=boss?['all',...ROLES]:['all',...(mine?[mine]:[])];
  return <><div className="modal-heading"><h2>{r?'Edit reminder':'New reminder'}</h2><p>Shows on the desk banner from the alert days before the date until it is done.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();void run({type:'RMD_SAVE',...(r?{id:r.id}:{}),subject:f.subject,date:f.date,time:f.time,leadDays:f.leadDays,role:f.role,repeat:f.repeat,note:f.note,key:modal!.key},r?'Reminder updated.':'Reminder saved.',{close:true});}}>
      <label>Subject<input value={f.subject} maxLength={80} required onChange={e=>set({subject:e.target.value})} placeholder="e.g. Fire extinguisher refill due"/></label>
      <div className="form-row"><label>Date<input type="date" value={f.date} required onChange={e=>set({date:e.target.value})}/></label>
        <label>Time (optional)<input type="time" value={f.time} onChange={e=>set({time:e.target.value})}/></label>
        <label>Alert days before<input type="number" min={0} max={60} value={f.leadDays} onChange={e=>set({leadDays:e.target.value})}/></label></div>
      <div className="form-row"><label>For<select value={f.role} onChange={e=>set({role:e.target.value as typeof f.role})}>{roles.map(x=><option key={x} value={x}>{x==='all'?'Everyone at the desk':x}</option>)}</select></label>
        <label>Repeats<select value={f.repeat} onChange={e=>set({repeat:e.target.value as typeof f.repeat})}>{RMD_REPEATS.map(x=><option key={x} value={x}>{x==='none'?'Does not repeat':x==='monthly'?'Every month':'Every year'}</option>)}</select></label></div>
      <label>Note (optional)<input value={f.note} maxLength={200} onChange={e=>set({note:e.target.value})}/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||f.subject.trim().length<2}>{r?'Save changes':'Save reminder'}</button></div>
    </form></>;
}
export function ShortcutsModal(){
  const {permission,closeModal}=useHcc();
  return <><div className="modal-heading"><h2>Keyboard shortcuts</h2><p>Desktop and tablet with a keyboard. A key works when you hold the permission and no dialog is open.</p></div>
    <table className="dk-short"><thead><tr><th>Key</th><th>Opens</th><th>Old software</th></tr></thead><tbody>
      {SHORTCUTS.map(x=><tr key={x.id} style={shortcutAllowed(x,permission)?undefined:{opacity:.55}}><td><kbd className="dk-key">{x.key}</kbd></td><td>{x.label}</td><td>{x.legacy??'—'}</td></tr>)}</tbody></table>
    <p className="muted">F1, F5, F11 and F12 are left to the browser (help, reload, full screen, developer tools).</p>
    <div className="modal-actions"><button className="btn" onClick={closeModal}>Close</button></div></>;
}
