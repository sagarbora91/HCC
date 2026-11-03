// F7-R7 · shift hand-over pack: the handover notes of a business date (issues, keys, guests, carried items) and the shift checklists' completion.
// The cash float line appears only for roles that see handover cash (hovSeesCash); housekeeping / maintenance get the pack without it (DEC-061).
import type {State} from '../../types';
import {HccError} from '../../errors';
import {hovHasAccess,hovNotesFor,hovProgress,hovSeesCash,hovRunsFor} from '../../streams/handover';
import {RPT_NO_MONEY_ROLES} from '../mask';
import {type DocModel,T,R,userName} from './model';

export function handoverPackDocument(s:State,uid:string,date:string=s.date):DocModel{
  if(!hovHasAccess(s,uid))throw new HccError('FORBIDDEN','You cannot print the hand-over pack.');
  const role=s.users.find(u=>u.id===uid)?.role??'',cash=hovSeesCash(s,uid)&&!RPT_NO_MONEY_ROLES.includes(role);
  const notes=hovNotesFor(s,uid).filter(n=>n.date===date);
  const runs=hovRunsFor(s,uid,date);
  const noteRows=notes.map(n=>R({desk:n.desk,shift:`${n.shift} to ${n.toShift}`,author:userName(s,n.author),issues:n.nothing?'Nothing to report':n.issues,keys:n.keys,guests:n.guests,carry:n.carry.map(c=>c.text+(c.done?' (done)':'')).join('; '),status:n.status,...(cash?{cash:n.cash?.amount??null}:{})}));
  const cols=[T('desk','Desk'),T('shift','Shift'),T('author','By'),T('issues','Issues'),T('keys','Keys'),T('guests','Guests'),T('carry','Carried over'),T('status','Status'),...(cash?[T('cash','Cash float','money')]:[])];
  const runRows=runs.map(r=>{const p=hovProgress(s,r);return R({name:r.name,desk:r.desk,shift:r.shift,status:r.status,done:p.done,total:p.total,overdue:p.overdue});});
  return {id:'doc.handover-pack',title:'Shift hand-over pack',number:date,property:s.settings.hotel,money:cash,
    pairs:[['Business date',date],['Notes',String(notes.length)],['Checklists',String(runs.length)]],
    tables:[{key:'notes',title:'Hand-over notes',columns:cols,rows:noteRows},
      {key:'lists',title:'Checklists',columns:[T('name','Checklist'),T('desk','Desk'),T('shift','Shift'),T('status','Status'),T('done','Done','int'),T('total','Items','int'),T('overdue','Overdue','int')],rows:runRows}],
    notes:['Same figures as the Checklists and handover page.'],sign:['Handing over','Taking over'],basis:'Business date of the note.'};
}
