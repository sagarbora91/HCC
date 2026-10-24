// Module 16 · Linen, laundry & stock — command handlers. Command types MUST start with 'INV_'. Return value = audit/summary text.
import type {Handler,Kit} from '../commands';
import {can} from '../../../domain/selectors';
import {INVState,INVItem,INVLoc,INVMove,INV_LOCS,INV_FLOORS,INV_COUNTABLE,INV_LOSS_REASONS,INV_MAX_QTY,invEmpty,invLocLabel,invMoneyRole,invTotal} from '../../../domain/streams/inventory';

const slice=(k:Kit):INVState=>{const x=k.s.inventoryX=k.s.inventoryX??invEmpty();
  x.items??=[];x.moves??=[];x.batches??=[];x.losses??=[];x.counts??=[];x.grns??=[];return x;};
const item=(k:Kit,id:unknown):INVItem=>{const it=slice(k).items.find(i=>i.id===id);k.assert(it,'Choose a stock item.','NOT_FOUND');return it!;};
const qty=(k:Kit,n:unknown,what='Quantity')=>k.assert(Number.isInteger(n)&&(n as number)>=1&&(n as number)<=INV_MAX_QTY,`${what} must be a whole number from 1 to ${INV_MAX_QTY.toLocaleString('en-IN')}.`);
const loc=(k:Kit,l:unknown,allowed:INVLoc[]=INV_LOCS.map(x=>x.id))=>{k.assert(allowed.includes(l as INVLoc),'Choose a valid location.');return l as INVLoc;};
const have=(k:Kit,it:INVItem,l:INVLoc,n:number)=>k.assert((it.stock[l]??0)>=n,`Only ${it.stock[l]??0} ${it.name} at ${invLocLabel(l)}.`);
const add=(it:INVItem,l:INVLoc,n:number)=>{it.stock[l]=(it.stock[l]??0)+n;};
function move(k:Kit,m:Omit<INVMove,'id'|'date'|'at'|'actor'>){slice(k).moves.push({id:k.id('IMV'),date:k.s.date,at:k.s.clock,actor:k.uid,...m});}
function lines(k:Kit,raw:unknown){k.assert(Array.isArray(raw)&&raw.length>0,'Add at least one item line.');
  const arr=raw as {item:string}[];k.assert(new Set(arr.map(l=>l.item)).size===arr.length,'Each item may appear only once.');return arr;}

export const handlers:Record<string,Handler>={
  /** Stock in (simulated goods receipt) → Main store. Weighted-average cost when a unit cost is entered. */
  INV_GRN:k=>{const {cmd,s}=k;k.req('housekeeping');
    k.assert(can(s,k.uid,'inspect')||can(s,k.uid,'expenses'),'Receiving stock needs a supervisor or manager (inspect or expenses permission).','FORBIDDEN');k.open();
    const supplier=String(cmd.supplier??'').trim();k.assert(supplier.length>=2,'Enter the supplier.');
    const ls=lines(k,cmd.lines) as {item:string;qty:number;cost?:number}[];
    const costed=ls.some(l=>l.cost!==undefined&&l.cost!==null);
    if(costed)k.assert(invMoneyRole(s,k.uid),'Only roles that see stock values may enter a unit cost.','FORBIDDEN');
    const gid=k.id('GRN'),out:{item:string;qty:number;cost:number}[]=[];
    for(const l of ls){const it=item(k,l.item);qty(k,l.qty);
      if(l.cost!==undefined&&l.cost!==null){k.assert(Number.isSafeInteger(l.cost)&&l.cost>0&&l.cost<=10000000,'Unit cost must be greater than zero.');
        const tot=invTotal(it);it.cost=Math.round((it.cost*tot+l.cost*l.qty)/(tot+l.qty));}
      add(it,'store',l.qty);move(k,{kind:'grn',item:it.id,qty:l.qty,from:supplier,to:'store',ref:gid});out.push({item:it.id,qty:l.qty,cost:l.cost??it.cost});}
    const ref=String(cmd.ref??'').trim()||gid;
    slice(k).grns.push({id:gid,date:s.date,supplier,ref,lines:out,actor:k.uid});
    return `${gid} · ${supplier} · ${out.length} line(s) received to Main store (simulated GRN)`;},

  /** Transfer between Main store and floor pantries (issue to floor / return to store). */
  INV_ISSUE:k=>{const {cmd}=k;k.req('housekeeping');k.open();const it=item(k,cmd.item);qty(k,cmd.qty);
    const from=loc(k,cmd.from,INV_COUNTABLE),to=loc(k,cmd.to,INV_COUNTABLE);k.assert(from!==to,'Choose two different locations.');
    have(k,it,from,cmd.qty);add(it,from,-cmd.qty);add(it,to,cmd.qty);move(k,{kind:'issue',item:it.id,qty:cmd.qty,from,to});
    return `${it.name} × ${cmd.qty} · ${invLocLabel(from)} → ${invLocLabel(to)}`;},

  /** Amenities / cleaning supplies used from a floor pantry. */
  INV_USE:k=>{const {cmd}=k;k.req('housekeeping');k.open();const it=item(k,cmd.item);qty(k,cmd.qty);
    k.assert(it.kind!=='linen','Linen is not consumed — record a room linen change instead.');
    const from=loc(k,cmd.floor,INV_FLOORS);have(k,it,from,cmd.qty);add(it,from,-cmd.qty);move(k,{kind:'use',item:it.id,qty:cmd.qty,from,to:'used',ref:cmd.room?String(cmd.room):undefined});
    return `${it.name} × ${cmd.qty} used from ${invLocLabel(from)}`;},

  /** Linen change: fresh pieces from a floor pantry into rooms; the same number of used pieces become soiled. */
  INV_ROOM_CHANGE:k=>{const {cmd}=k;k.req('housekeeping');k.open();const it=item(k,cmd.item);qty(k,cmd.qty);
    k.assert(it.kind==='linen','Only linen is changed in rooms.');
    const from=loc(k,cmd.floor,INV_FLOORS);have(k,it,from,cmd.qty);have(k,it,'rooms',cmd.qty);
    add(it,from,-cmd.qty);add(it,'soiled',cmd.qty);move(k,{kind:'room-change',item:it.id,qty:cmd.qty,from,to:'rooms',ref:cmd.room?String(cmd.room):undefined});
    return `${it.name} × ${cmd.qty} changed (${invLocLabel(from)} → rooms; used pieces → soiled)`;},

  /** Soiled linen out to the laundry vendor (simulated pickup). */
  INV_LAUNDRY_SEND:k=>{const {cmd,s}=k;k.req('housekeeping');k.open();const x=slice(k);
    const ls=lines(k,cmd.lines) as {item:string;qty:number}[];const out=[];
    for(const l of ls){const it=item(k,l.item);k.assert(it.kind==='linen','Only linen goes to the laundry.');qty(k,l.qty);have(k,it,'soiled',l.qty);}
    const bid=k.id('LB'),vendor=x.vendor?.name??'Fresh Linen Services';
    for(const l of ls){const it=item(k,l.item);add(it,'soiled',-l.qty);add(it,'laundry',l.qty);move(k,{kind:'laundry-out',item:it.id,qty:l.qty,from:'soiled',to:'laundry',ref:bid});out.push({item:it.id,sent:l.qty});}
    x.batches.push({id:bid,vendor,sent:s.date,sentAt:s.clock,lines:out,status:'Out',actor:k.uid,note:String(cmd.note??'').trim()||undefined});
    return `${bid} · ${out.reduce((a,l)=>a+l.sent,0)} pieces sent to ${vendor} (simulated pickup)`;},

  /** Laundry return: received clean → Main store; damaged → written off; missing → vendor shortage. */
  INV_LAUNDRY_RECEIVE:k=>{const {cmd,s}=k;k.req('housekeeping');k.open();const x=slice(k);
    const b=x.batches.find(b=>b.id===cmd.id);k.assert(b,'Laundry batch not found.','NOT_FOUND');
    k.assert(b!.status==='Out','This batch has already been received.','INVALID_TRANSITION');
    const ls=(Array.isArray(cmd.lines)?cmd.lines:[]) as {item:string;received:number;damaged?:number}[];
    for(const l of b!.lines){const r=ls.find(z=>z.item===l.item);k.assert(r,'Enter the returned count for every item in the batch.');
      const rec=r!.received,dmg=r!.damaged??0;
      k.assert(Number.isInteger(rec)&&rec>=0&&Number.isInteger(dmg)&&dmg>=0,'Counts must be whole numbers (0 or more).');
      k.assert(rec+dmg<=l.sent,`Received + damaged cannot exceed the ${l.sent} sent.`);}
    let charge=0,short=false;
    for(const l of b!.lines){const r=ls.find(z=>z.item===l.item)!,it=item(k,l.item),dmg=r.damaged??0,miss=l.sent-r.received-dmg;
      l.received=r.received;l.damaged=dmg;add(it,'laundry',-l.sent);add(it,'store',r.received);
      if(r.received)move(k,{kind:'laundry-in',item:it.id,qty:r.received,from:'laundry',to:'store',ref:b!.id});
      charge+=(r.received+dmg)*(it.laundryRate??0);
      if(dmg){short=true;x.losses.push({id:k.id('LOSS'),date:s.date,item:it.id,loc:'laundry',qty:dmg,reason:'Damaged at laundry',actor:k.uid,value:dmg*it.cost,ref:b!.id});move(k,{kind:'loss',item:it.id,qty:dmg,from:'laundry',reason:'Damaged at laundry',ref:b!.id});}
      if(miss){short=true;x.losses.push({id:k.id('LOSS'),date:s.date,item:it.id,loc:'laundry',qty:miss,reason:'Missing from laundry batch (vendor claim)',actor:k.uid,value:miss*it.cost,ref:b!.id});move(k,{kind:'loss',item:it.id,qty:miss,from:'laundry',reason:'Missing from laundry batch',ref:b!.id});}}
    b!.status=short?'Returned short':'Returned';b!.returned=s.date;b!.receivedBy=k.uid;b!.charge=charge;
    if(cmd.note)b!.note=[b!.note,String(cmd.note).trim()].filter(Boolean).join(' · ');
    return `${b!.id} · ${b!.status.toLowerCase()} · ${b!.lines.reduce((a,l)=>a+(l.received||0),0)} clean pieces to Main store`;},

  /** Shortage / damage / loss write-off with a reason. */
  INV_LOSS:k=>{const {cmd,s}=k;k.req('housekeeping');k.open();const it=item(k,cmd.item);qty(k,cmd.qty);
    const l=loc(k,cmd.loc,['store','floor1','floor2','floor3','rooms','soiled']);
    k.assert((INV_LOSS_REASONS as readonly string[]).includes(cmd.reason),'Choose a reason.');
    const note=String(cmd.note??'').trim();k.assert(cmd.reason!=='Other'||note.length>=3,'Describe the reason.');
    have(k,it,l,cmd.qty);add(it,l,-cmd.qty);const lid=k.id('LOSS');
    slice(k).losses.push({id:lid,date:s.date,item:it.id,loc:l,qty:cmd.qty,reason:cmd.reason,note:note||undefined,actor:k.uid,value:cmd.qty*it.cost});
    move(k,{kind:'loss',item:it.id,qty:cmd.qty,from:l,reason:cmd.reason,ref:lid});
    return `${lid} · ${it.name} × ${cmd.qty} written off at ${invLocLabel(l)} · ${cmd.reason}`;},

  /** Physical count of a shelf location. No variance → posted; variance → awaits review by another user with inspect. */
  INV_COUNT_SUBMIT:k=>{const {cmd,s}=k;k.req('housekeeping');k.open();const x=slice(k);
    const l=loc(k,cmd.loc,INV_COUNTABLE);
    k.assert(!x.counts.some(c=>c.loc===l&&c.status==='Submitted'),`A count for ${invLocLabel(l)} is already awaiting review.`,'INVALID_TRANSITION');
    const counts=(cmd.counts??{}) as Record<string,number>;
    const lines=x.items.map(it=>{const n=counts[it.id];k.assert(Number.isInteger(n)&&n>=0&&n<=INV_MAX_QTY,`Enter a whole-number count for ${it.name}.`);return {item:it.id,system:it.stock[l]??0,counted:n};});
    const variance=lines.some(z=>z.counted!==z.system),cid=k.id('CNT');
    x.counts.push({id:cid,date:s.date,at:s.clock,loc:l,lines,status:variance?'Submitted':'Posted',actor:k.uid,note:String(cmd.note??'').trim()||undefined});
    return `${cid} · ${invLocLabel(l)} · ${variance?lines.filter(z=>z.counted!==z.system).length+' variance line(s) — review required':'matches system stock'}`;},

  /** Review a count with variance: approve adjusts stock by the variance (later movements preserved), reject discards it. */
  INV_COUNT_POST:k=>{const {cmd,s}=k;k.req('inspect');k.open();const x=slice(k);
    const c=x.counts.find(c=>c.id===cmd.id);k.assert(c,'Count not found.','NOT_FOUND');
    k.assert(c!.status==='Submitted','This count has already been decided.','INVALID_TRANSITION');
    k.assert(c!.actor!==k.uid,'You cannot review your own count. Switch to another supervisor or manager.','FORBIDDEN');
    const reason=String(cmd.reason??'').trim();k.assert(reason.length>=3,'A reason is required.');
    if(cmd.approve){
      for(const l of c!.lines){const d=l.counted-l.system;if(!d)continue;const it=item(k,l.item);
        k.assert((it.stock[c!.loc]??0)+d>=0,`${it.name}: stock moved since the count — count again.`);}
      for(const l of c!.lines){const d=l.counted-l.system;if(!d)continue;const it=item(k,l.item);add(it,c!.loc,d);
        move(k,{kind:'count',item:it.id,qty:d,from:c!.loc,reason,ref:c!.id});
        if(d<0)x.losses.push({id:k.id('LOSS'),date:s.date,item:it.id,loc:c!.loc,qty:-d,reason:'Count shortage',note:reason,actor:c!.actor,value:-d*it.cost,ref:c!.id});}
    }
    c!.status=cmd.approve?'Posted':'Rejected';c!.reviewer=k.uid;c!.reason=reason;
    return `${c!.id} · ${invLocLabel(c!.loc)} · variance ${cmd.approve?'posted':'rejected'}`;},

  /** Minimum, reorder quantity, par per room type and standard use per room-night. */
  INV_ITEM_SET:k=>{const {cmd}=k;k.req('inspect');k.open();const it=item(k,cmd.id);
    const int=(n:unknown,max=INV_MAX_QTY)=>Number.isInteger(n)&&(n as number)>=0&&(n as number)<=max;
    k.assert(int(cmd.min)&&int(cmd.reorderQty),'Minimum and reorder quantity must be whole numbers (0 or more).');
    if(cmd.stdX100!==undefined)k.assert(int(cmd.stdX100,10000),'Standard use per room-night must be 0–100.');
    if(cmd.par!==undefined){k.assert(it.kind==='linen','Par levels apply to linen only.');
      k.assert(cmd.par&&typeof cmd.par==='object'&&Object.values(cmd.par).every(v=>int(v,20)),'Par per room must be 0–20 pieces.');}
    it.min=cmd.min;it.reorderQty=cmd.reorderQty;if(cmd.stdX100!==undefined)it.stdX100=cmd.stdX100;if(cmd.par!==undefined)it.par={...cmd.par};
    return `${it.name} · minimum ${it.min}, reorder ${it.reorderQty}${it.par?' · par '+Object.entries(it.par).map(([t,n])=>`${t} ${n}`).join(', '):''}`;},
};
