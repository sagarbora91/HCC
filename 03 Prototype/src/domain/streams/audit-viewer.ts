// F4-T3 qw-audit · audit-trail viewer helpers: filters by actor / entity type / entity id / date, a before / after diff for master and
// setting rows, and the CSV of report `rpt.admin.audit-log`. Pure over `s.audit` ({id,at,actor,action,detail}); permission is the existing
// PERM-ADM-004 gate of the Audit log tab. Money in a diff is masked for roles that never see hotel money (DEC-061) with `mstMaskMoney`.
import type {State,Audit} from '../types';
import {csvMinimal} from '../csv';
import {mstMaskMoney} from './masters';

export const AV_REPORT_ID='rpt.admin.audit-log';
export type AVFilter={actor?:string;entity?:string;entityId?:string;from?:string;to?:string;action?:string;text?:string};
/** Entity type of an audit row = the first word of its action in upper case ("SETTING SET" → SETTING, "MST_ROOM" → MST). */
export const avEntity=(a:Pick<Audit,'action'>):string=>(a.action.trim().split(/[\s_·:-]+/)[0]||'').toUpperCase();
/** Entity id = first reference-like token in the detail ("Setting gst.room.lowBps", "HCC-1041", "EXP-102", "Room 105"). */
export function avEntityId(a:Pick<Audit,'detail'>):string{
  const d=a.detail||'';
  const ref=d.match(/\b[A-Z]{2,6}-\d+\b/);if(ref)return ref[0];
  const kv=d.match(/^(?:Setting|Master|Room|User|Rate)\s+([A-Za-z0-9_.:-]+)/i);return kv?kv[1]:'';
}
export const avEntities=(s:State):string[]=>[...new Set(s.audit.map(avEntity).filter(Boolean))].sort();
export function avFilter(s:State,f:AVFilter):Audit[]{
  const t=(f.text||'').trim().toLowerCase(),id=(f.entityId||'').trim().toLowerCase();
  return s.audit.filter(a=>(!f.actor||a.actor===f.actor)&&(!f.action||a.action===f.action)&&(!f.entity||avEntity(a)===f.entity)
    &&(!id||avEntityId(a).toLowerCase().includes(id)||a.detail.toLowerCase().includes(id))
    &&(!t||`${a.action} ${a.detail} ${a.actor}`.toLowerCase().includes(t))&&(!f.from||a.at.slice(0,10)>=f.from)&&(!f.to||a.at.slice(0,10)<=f.to));
}
export type AVChange={key:string;before:string;after:string};
/** Master / setting rows write "<what> · <old> → <new> · <extra>": every segment with an arrow is one changed key. */
export function avDiff(a:Pick<Audit,'detail'>):AVChange[]{
  const out:AVChange[]=[];
  for(const seg of (a.detail||'').split(' · ')){
    const m=seg.match(/^(?:(.+?):\s*)?(.+?)\s*(?:→|->)\s*(.+)$/);
    if(m)out.push({key:m[1]||'value',before:m[2].trim(),after:m[3].trim()});
  }
  return out;
}
export const avHasDiff=(a:Pick<Audit,'detail'>)=>avDiff(a).length>0;
/** Diff values with money masked for roles without hotel money. */
export const avDiffFor=(s:State,uid:string,a:Pick<Audit,'detail'>):AVChange[]=>avDiff(a).map(c=>({key:c.key,before:mstMaskMoney(s,uid,c.before),after:mstMaskMoney(s,uid,c.after)}));
/** CSV of the filtered rows (formula-guarded); the same filter and masking as the screen. */
export function avCsv(s:State,uid:string,f:AVFilter):string{
  const rows=avFilter(s,f);
  const head=['Id','At','Actor','Entity','Entity id','Action','Detail'];
  return [head,...rows.map(a=>[a.id,a.at.replace('T',' '),a.actor,avEntity(a),avEntityId(a),a.action,mstMaskMoney(s,uid,a.detail)])].map(r=>r.map(csvMinimal).join(',')).join('\r\n');
}
