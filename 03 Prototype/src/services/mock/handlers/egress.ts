// F5-E1 · CFG_NET_SET {setting, value, reason} (contract text says {key,…}; `key` is the idempotency key of every command, so the field is `setting` as in SETTING_SET) — the internet switches (EGRESS-CONTRACT §1.1, §6). Owner only (PERM-NET-001), reason required,
// takes effect at once; every change is kept in s.egressX.changes and the held outbox is replayed / held right away (§1.2).
// NET_TICK {} — the outbox worker tick (replay while ON). Both are audited by execute() with the returned text.
import type {Handler} from '../commands';
import {cfgEnsure} from '../../../domain/streams/config';
import {NET_KEYS,NET_WINDOW,netParseWindow,netReplay,netSlice} from '../../../domain/streams/egress';

export const handlers:Record<string,Handler>={
  CFG_NET_SET:k=>{
    const {s,cmd,assert,fail}=k;
    if(k.user.role!=='Owner')fail('FORBIDDEN','Only the Owner changes the internet switches (PERM-NET-001).');
    const key=String(cmd.setting??'');assert(NET_KEYS.includes(key),'Unknown internet setting.');
    const reason=String(cmd.reason??'').trim();assert(reason.length>=3&&reason.length<=120,'Give a reason for changing an internet switch (3–120 characters).');
    let value:unknown=cmd.value;
    if(key===NET_WINDOW){
      if(value===''||value===null||value===undefined)value=null;
      else{const w=netParseWindow(value);assert(w,'Window: {"from":"HH:mm","to":"HH:mm","days":[0-6]} (0 = Sunday), or empty.');value=w;}
    }else assert(typeof value==='boolean','Choose on or off.');
    const vals=cfgEnsure(s).values;const before=vals[key]??(key===NET_WINDOW?null:false);
    vals[key]=value;
    const x=netSlice(s);x.seq+=1;
    x.changes.push({id:`NETC-${String(x.seq).padStart(5,'0')}`,key,before,after:value,reason,by:k.uid,at:s.clock});
    const r=netReplay(s);
    const show=(v:unknown)=>v===true?'ON':v===false?'OFF':v?JSON.stringify(v):'none';
    return `${key} ${show(before)} → ${show(value)} · ${reason}${r.released?` · ${r.released} held item(s) released`:''}${r.held?` · ${r.held} item(s) held`:''}`;
  },
  NET_TICK:k=>{
    if(k.user.role!=='Owner')k.fail('FORBIDDEN','Only the Owner runs the internet outbox tick (PERM-NET-001).');
    const r=netReplay(k.s);
    return `Internet outbox: ${r.released} released, ${r.held} held`;
  },
};
