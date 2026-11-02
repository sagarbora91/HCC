// F4-T1 · Tariff card page — pure UI state (tested by tests/stream-tariffcard-ui.mjs). No React here.
import type {State,TRCLine,TRCVersion} from '../../domain/types';
import {trcCan,trcVersions,trcLatestPublished,trcLineTitle,trcPriceLine,trcRoundingDiff,TRC_KIND_LABEL,TRC_DAYSET_LABEL,type TrcLineInput} from '../../domain/streams/tariffcard';

export const TRC_TABS=[{id:'rooms',label:'Rooms'},{id:'meals',label:'Meal plans & extras'},{id:'seasons',label:'Seasons & weekends'},{id:'halls',label:'Halls'},
  {id:'history',label:'History'},{id:'print',label:'Print'}] as const;
export type TrcTab=typeof TRC_TABS[number]['id'];
export const TRC_TAB_IDS=TRC_TABS.map(t=>t.id) as TrcTab[];
/** Which lines each editor tab shows. */
export const trcTabLines=(v:TRCVersion|undefined,tab:TrcTab):TRCLine[]=>(v?.lines??[]).filter(l=>
  tab==='rooms'?l.kind==='room'||l.kind==='occupancy'||l.kind==='day-use':tab==='meals'?l.kind==='meal'||l.kind==='extra':tab==='halls'?l.kind==='hall':false);

/** Buttons and editability for the signed-in user on a version (phone: read-only except the Owner). */
export function trcUiState(s:State,uid:string,v:TRCVersion|undefined,phone=false){
  const view=trcCan(s,uid,'view'),propose=trcCan(s,uid,'propose'),publish=trcCan(s,uid,'publish');
  const open=!!v&&(v.status==='draft'||v.status==='proposed');
  const phoneLock=phone&&!publish;
  return {view,
    canEdit:open&&propose&&!phoneLock&&v!.status==='draft',
    canNewDraft:propose&&!phoneLock,
    canPropose:!!v&&v.status==='draft'&&propose&&!publish&&!phoneLock,
    canPublish:open&&publish,
    canReject:!!v&&v.status==='proposed'&&publish,
    canDiscard:!!v&&v.status==='draft'&&propose&&!phoneLock,
    canImport:publish&&!phone,
    canWeekend:publish,
    readOnlyNote:!view?'The tariff card is not available to your role.':publish?'':propose?(phone?'Read only on the phone — propose from a desktop or tablet.':'You can draft and propose; only the Owner publishes.'):'View only.'};
}
/** The version shown by default: an open draft / proposal (for those who can act on it), else the latest published, else the newest. */
export function trcDefaultVersion(s:State,uid:string):TRCVersion|undefined{
  const all=trcVersions(s);
  if(trcCan(s,uid,'propose')){const open=all.find(v=>v.status==='proposed')??all.find(v=>v.status==='draft');if(open)return open;}
  return trcLatestPublished(s)??all[0];
}
export type TrcEditorRow={id:string;kind:string;title:string;daySet:string;excl:number|null;bps:number;incl:number|null;entered?:number;diff:number;note?:string};
/** Editor grid rows (same figures as the rate sheet). */
export const trcEditorRows=(s:State,lines:TRCLine[]):TrcEditorRow[]=>lines.map(l=>({id:l.id,kind:TRC_KIND_LABEL[l.kind],title:trcLineTitle(s,l),daySet:TRC_DAYSET_LABEL[l.daySet],
  excl:l.rateExcl,bps:l.taxBps,incl:l.displayIncl,entered:l.enteredIncl,diff:trcRoundingDiff(l),note:l.kind==='occupancy'&&l.occKind==='child'?`Age ${l.ageFrom??''}–${l.ageTo??''}`:l.note}));
/** Rupee text → paise (blank → null; bad text → undefined). */
export function trcParseRupees(t:string):number|null|undefined{const x=t.replace(/[₹,\s]/g,'');if(!x)return null;if(!/^\d+(\.\d{1,2})?$/.test(x))return undefined;const [r,p='']=x.split('.');return Number(r)*100+Number((p+'00').slice(0,2));}
/** Lines as command input with one price changed (inclusive entry keeps what the user typed; exclusive entry drops it). */
export function trcEditLine(lines:TRCLine[],id:string,paise:number|null,incl:boolean):TrcLineInput[]{
  return lines.map(l=>{const {taxBps:_t,displayIncl:_d,...rest}=l;
    if(l.id!==id)return l.enteredIncl!==undefined?{...rest}:{...rest,enteredIncl:undefined};
    return incl&&paise!==null?{...rest,rateExcl:undefined,enteredIncl:paise}:{...rest,rateExcl:paise,enteredIncl:undefined};});
}
/** Live preview of one edit (before saving): the line as the save would price it, or the refusal text. */
export function trcPreviewLine(s:State,v:TRCVersion,input:TrcLineInput):{line?:TRCLine;error?:string}{
  try{return {line:trcPriceLine(s,input,v.effectiveFrom,input.id??'L')};}catch(e){return {error:(e as Error).message};}
}
