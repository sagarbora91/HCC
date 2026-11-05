// F12 MEAS · meas-settings handlers (MEAS-CONTRACT §1.4). Both run through the Master configuration path: the pack / item settings are
// module-store definitions, so SETTING_SET (and CFG_IMPORT / CFG_REVERT / CFG_DECIDE) writes the setting's own history row and then calls
// these with `viaHub`. Called directly (the Measurement panel), they write that row themselves.
//   MEAS_PACK_SET {pack,on,reason?}   — writes one change row per item whose effective state changes (core items never change).
//   MEAS_ITEM_SET {setting,on,reason?} — refuses MEAS_PACK_OFF when switching an item on under an off pack; core items cannot be off.
// PERM-MEAS-001 (measurement settings) = Owner only; Manager and Tech admin read the group.
import type {Handler,Kit} from '../commands';
import {cfgEnsure} from '../../../domain/streams/config';
import {MEAS_ITEMS,MEAS_PACKS,MEAS_PACK_OFF,measItem,measIsCore,measOn,measPack,measPackOn,measItemSwitch} from '../../../domain/streams/measure';
import type {MeasPack} from '../../../domain/streams/measure-types';

const onOff=(v:boolean)=>v?'On':'Off';
function owner(k:Kit){if(k.user.role!=='Owner')k.fail('FORBIDDEN','Only the Owner changes measurement settings (PERM-MEAS-001). You can review them here.');}
function row(k:Kit,key:string,before:string,after:string,reason:string){
  (k.s.configHistory||(k.s.configHistory=[])).unshift({id:k.id('CFG'),key,before,after,at:k.s.clock,actor:k.user.name,effectiveFrom:k.s.date,reason});
}
function rawChange(k:Kit,key:string,before:boolean,after:boolean){
  cfgEnsure(k.s).changes.unshift({id:k.id('CFG'),key,before,after,effectiveFrom:k.s.date,at:k.s.clock,by:k.uid,status:'applied'});
}

export const handlers:Record<string,Handler>={
  MEAS_PACK_SET:k=>{
    const {s,cmd,assert}=k;owner(k);
    const p=MEAS_PACKS.find(x=>x.pack===cmd.pack);assert(p,'Choose a measurement pack.','NOT_FOUND');
    assert(typeof cmd.on==='boolean','Choose on or off.');
    const on=cmd.on as boolean,pack=p!.pack as MeasPack,before=measPackOn(s,pack);
    if(!cmd.viaHub){assert(before!==on,'No change: the pack already has this value.');}
    const items=MEAS_ITEMS.filter(i=>i.pack===pack);
    const was=new Map(items.map(i=>[i.key,measOn(s,i.key)]));
    const reason=String(cmd.reason??'').trim().slice(0,120)||`Pack ${p!.label} switched ${on?'on':'off'}`;
    if(!cmd.viaHub){row(k,p!.key,onOff(before),onOff(on),reason);rawChange(k,p!.key,before,on);}
    cfgEnsure(s).values[p!.key]=on;
    let n=0;
    for(const i of items){const now=measOn(s,i.key);if(now!==was.get(i.key)){row(k,i.key,onOff(!!was.get(i.key)),onOff(now),`${reason} (pack)`);n++;}}
    return `Measurement pack ${p!.label} · ${onOff(before)} → ${onOff(on)} · ${n} item${n===1?'':'s'} changed · core items stay on · data kept`;
  },
  MEAS_ITEM_SET:k=>{
    const {s,cmd,assert,fail}=k;owner(k);
    const key=String(cmd.setting??'');const it=measItem(key);assert(it,'Unknown measurement item.','NOT_FOUND');
    assert(typeof cmd.on==='boolean','Choose on or off.');
    const on=cmd.on as boolean,before=measItemSwitch(s,key);
    if(!on&&measIsCore(s,key))fail('VALIDATION',`${it!.label} is a core item — always on (it keeps the day close and the night audit whole).`);
    if(on&&!measPackOn(s,it!.pack))fail(MEAS_PACK_OFF,`MEAS_PACK_OFF: switch the ${measPack(it!.pack).label} pack on first; ${it!.label} stays off while its pack is off.`);
    if(!cmd.viaHub){assert(before!==on,'No change: the item already has this value.');
      row(k,key,onOff(before),onOff(on),String(cmd.reason??'').trim().slice(0,120));rawChange(k,key,before,on);}
    cfgEnsure(s).values[key]=on;
    return `Measurement item ${key} · ${onOff(before)} → ${onOff(on)} · data kept`;
  },
};
