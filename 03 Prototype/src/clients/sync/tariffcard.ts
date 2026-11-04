// AF-SYNC-OPS (OPS-02) · Tariff card in the production sync layer.
//   server  POST /commands/SaveTariffCardDraft | ProposeTariffCard | PublishTariffCard | RejectTariffCard | DiscardTariffCard | SetTariffCardWeekend
//           GET /tariff-card → TariffCardList (Hcc.Contracts/Tariff/TariffCardContracts.cs)
// A version's prototype id is the server's version Code (what the page shows); the numeric VersionId is learnt in the IdDirectory
// (kind 'tariffVersion'). A line's prototype id is 'L' + LineNo padded to 3 (the prototype's own scheme), so LineNo round-trips.
// TRC_IMPORT has no server contract (the rate card is imported on the hotel server PC): left unmapped.
import {HccClientError} from '../errors';
import {protoUserId,utcToClock} from './map';
import type {TRCState,TRCVersion,TRCLine,TRCLineKind,TRCDaySet,TRCStatus,TRCPublishWrite} from '../../domain/types';
import type {SyncArea,CommandCtx} from './types';

const text=(v:unknown)=>String(v??'').trim();
const TRC_VIEW='PERM-TRF-001';

export type SrvTrcLine={lineNo:number;kind:string;roomType:string|null;plan:string|null;occKind:string|null;mealPlan:string|null;extra:string|null;hall:string|null;daySet:string;
  rateExcl:number|null;enteredIncl:number|null;minGuests:number|null;maxGuests:number|null;ageFrom:number|null;ageTo:number|null;note:string|null;taxBps:number;displayIncl:number|null};
export type SrvTrcVersion={versionId:number;code:string;label:string;effectiveFrom:string;status:string;weekendDays:number[];lines:SrvTrcLine[];createdBy:number;createdUtc:string;
  proposedBy:number|null;publishedBy:number|null;publishedUtc:string|null;supersededBy:number|null;notes:string|null;publishLog:{master:string;ref:string;before:number|null;after:number;label:string}[]};
export type SrvTrcList={versions:SrvTrcVersion[];weekendDaysDefault:number[]};

const lineId=(no:number)=>'L'+String(no).padStart(3,'0');
export function trcLineFromServer(l:SrvTrcLine):TRCLine{
  return {id:lineId(l.lineNo),kind:l.kind as TRCLineKind,...(l.roomType?{roomType:l.roomType}:{}),...(l.plan?{plan:l.plan}:{}),...(l.occKind?{occKind:l.occKind as TRCLine['occKind']}:{}),
    ...(l.mealPlan?{mealPlan:l.mealPlan}:{}),...(l.extra?{extraId:l.extra}:{}),...(l.hall?{hallId:l.hall}:{}),daySet:l.daySet as TRCDaySet,rateExcl:l.rateExcl,taxBps:l.taxBps,displayIncl:l.displayIncl,
    ...(l.enteredIncl!==null&&l.enteredIncl!==undefined?{enteredIncl:l.enteredIncl}:{}),...(l.minGuests!=null?{minGuests:l.minGuests}:{}),...(l.maxGuests!=null?{maxGuests:l.maxGuests}:{}),
    ...(l.ageFrom!=null?{ageFrom:l.ageFrom}:{}),...(l.ageTo!=null?{ageTo:l.ageTo}:{}),...(l.note?{note:l.note}:{})};
}
/** GET /tariff-card → s.tariffCardX. `learn` records code ↔ numeric id for the command mappers. */
export function tariffCardFromServer(r:SrvTrcList,learn?:(code:string,id:number)=>void):TRCState{
  const codeOf=(id:number|null)=>id==null?undefined:(r.versions.find(v=>v.versionId===id)?.code||String(id));
  const versions:TRCVersion[]=r.versions.map(v=>{
    const id=v.code||String(v.versionId);learn?.(id,v.versionId);
    const sup=codeOf(v.supersededBy);
    return {id,label:v.label,effectiveFrom:String(v.effectiveFrom).slice(0,10),status:String(v.status).toLowerCase() as TRCStatus,weekendDays:[...v.weekendDays],
      lines:v.lines.map(trcLineFromServer),by:protoUserId(v.createdBy),at:utcToClock(v.createdUtc),
      ...(v.proposedBy!=null?{proposedBy:protoUserId(v.proposedBy)}:{}),...(v.publishedBy!=null?{publishedBy:protoUserId(v.publishedBy)}:{}),
      ...(v.publishedUtc?{publishedAt:utcToClock(v.publishedUtc)}:{}),...(sup?{supersededBy:sup}:{}),...(v.notes?{notes:v.notes}:{}),
      ...(v.publishLog?.length?{publishLog:v.publishLog.map(w=>({master:w.master as TRCPublishWrite['master'],ref:w.ref,...(w.before!=null?{before:w.before}:{}),after:w.after}))}:{})};
  });
  return {versions,weekendDaysDefault:[...(r.weekendDaysDefault??[])]};
}

/** Numeric server id of a prototype version id (the Code learnt at load time, else a plain server id). */
function versionIdOf(c:CommandCtx,id:unknown):number{
  const s=text(id);
  const known=c.ids.server('tariffVersion',s);
  if(known!==undefined)return known;
  const n=Number(s);
  if(Number.isSafeInteger(n)&&n>0)return n;
  throw new HccClientError('VALIDATION',`Tariff version ${s||'(none)'} is not a server record. Refresh and try again.`);
}
const lineNoOf=(id:string,index:number)=>{const m=/^L(\d+)$/.exec(id);const n=m?Number(m[1]):0;return n>0?n:index+1;};
const num=(v:unknown)=>v===undefined||v===null||v===''?null:Number(v);
function lineDto(l:Record<string,unknown>,index:number){
  return {lineNo:lineNoOf(text(l.id),index),kind:text(l.kind),roomType:text(l.roomType)||undefined,plan:text(l.plan)||undefined,occKind:text(l.occKind)||undefined,
    mealPlan:text(l.mealPlan)||undefined,extra:text(l.extraId)||undefined,hall:text(l.hallId)||undefined,daySet:text(l.daySet)||'all',
    rateExcl:num(l.rateExcl),enteredIncl:num(l.enteredIncl),minGuests:num(l.minGuests),maxGuests:num(l.maxGuests),ageFrom:num(l.ageFrom),ageTo:num(l.ageTo),
    note:text(l.note)||undefined,taxBps:Number(l.taxBps??0),displayIncl:num(l.displayIncl)};
}

const idBody=(c:CommandCtx)=>({versionId:versionIdOf(c,c.cmd.id)});

export const tariffcard:SyncArea={
  id:'tariffcard',
  loaders:[{slice:'tariffCardX',async load(c){
    if(!c.me.permIds.includes(TRC_VIEW))return c.current('tariffCardX');
    const r=await c.api.get<SrvTrcList>('/tariff-card');
    return tariffCardFromServer(r,(code,id)=>c.ids.learn('tariffVersion',code,id));
  }}],
  commands:[
    {type:'TRC_DRAFT_SAVE',reload:['tariffCardX'],async send(c){const k=c.cmd;
      const hasId=text(k.id)!=='';
      const cur=hasId?(c.state.tariffCardX?.versions??[]).find(v=>v.id===text(k.id)):undefined;
      // An edit sends only what changed (lines, or the valid-from date): the rest of the version comes from what was loaded.
      const label=text(k.label)||cur?.label||'';
      const effectiveFrom=text(k.effectiveFrom)||cur?.effectiveFrom||'';
      const lines=(Array.isArray(k.lines)?k.lines:cur?.lines??[]) as Record<string,unknown>[];
      await c.api.command('SaveTariffCardDraft',{versionId:hasId?versionIdOf(c,k.id):null,label,effectiveFrom,lines:lines.map(lineDto),
        ...(Array.isArray(k.weekendDays)?{weekendDays:k.weekendDays.map(Number)}:cur?{weekendDays:[...cur.weekendDays]}:{}),...(text(k.notes)?{notes:text(k.notes)}:cur?.notes?{notes:cur.notes}:{})});}},
    {type:'TRC_PROPOSE',reload:['tariffCardX'],async send(c){await c.api.command('ProposeTariffCard',idBody(c));}},
    {type:'TRC_PUBLISH',reload:['tariffCardX'],async send(c){await c.api.command('PublishTariffCard',idBody(c));}},
    {type:'TRC_DISCARD',reload:['tariffCardX'],async send(c){await c.api.command('DiscardTariffCard',idBody(c));}},
    {type:'TRC_REJECT',reload:['tariffCardX'],async send(c){await c.api.command('RejectTariffCard',{...idBody(c),reason:text(c.cmd.reason)});}},
    {type:'TRC_WEEKEND_SET',reload:['tariffCardX'],async send(c){
      await c.api.command('SetTariffCardWeekend',{versionId:text(c.cmd.id)?versionIdOf(c,c.cmd.id):null,days:(Array.isArray(c.cmd.days)?c.cmd.days:[]).map(Number)});}},
  ],
  unmapped:{TRC_IMPORT:'The rate card is imported on the hotel server PC (CSV); not available from this device.'},
};
