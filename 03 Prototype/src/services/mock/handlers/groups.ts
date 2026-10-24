// Module 14 · Group & corporate — command handlers. Command types MUST start with 'GRP_'. Return value = audit/summary text.
//   GRP_COMPANY_SAVE   create / edit a company account (reservations). New-company credit limit: admin sets, others raise a request.
//   GRP_LIMIT_SET      admin → sets the credit limit; reservations → raises a limit request.
//   GRP_LIMIT_DECIDE   approve / reject a limit request (approve; never your own request).
//   GRP_BLOCK_CREATE   group block: allotment per room type, dates, cut-off (reservations).
//   GRP_BLOCK_ALLOT    change allotment (not below pickup; increases revalidated).
//   GRP_ROOMING        rooming list → real bookings via the stream-A BOOK handler, atomic; credit-limit check.
//   GRP_RELEASE        manual release of unpicked rooms ({roomType,count} or all).
//   GRP_SWEEP          auto-release blocks whose cut-off has passed (also runs before every GRP command).
//   GRP_BLOCK_CANCEL   cancel a block with no active member bookings.
import type {Handler,Kit} from '../commands';
import {handlers as reservations} from './reservations';
import {can} from '../../../domain/selectors';
import {money,nights} from '../../../domain/money';
import {withHoldExempt} from '../../../domain/selectors';
import {grpPickupRooms,grpEnsure,grpSweep,grpRoomTypes,grpFreeRooms,grpHeldElsewhere,grpRemaining,grpPicked,grpCompany,grpCredit,grpMembers,
  GRP_GSTIN,GRP_MAX_LIMIT} from '../../../domain/streams/groups';
import type {GRPBlock,GRPCompany} from '../../../domain/streams/groups';
import {RES_PHONE} from '../../../domain/streams/reservations';

const DATE=/^\d{4}-\d{2}-\d{2}$/;
const str=(v:unknown,max=120)=>String(v??'').trim().slice(0,max);
function begin(k:Kit){const x=grpEnsure(k.s);grpSweep(k.s);return x;}
function blockOf(k:Kit,id:unknown):GRPBlock{const blk=grpEnsure(k.s).blocks.find(b=>b.id===id);k.assert(blk,'Group block not found.','NOT_FOUND');return blk!;}
function companyOf(k:Kit,id:unknown):GRPCompany{const c=grpEnsure(k.s).companies.find(c=>c.id===id);k.assert(c,'Company account not found.','NOT_FOUND');return c!;}
function rateMap(k:Kit,v:unknown,types:string[]):Record<string,number>{
  const out:Record<string,number>={};if(!v||typeof v!=='object')return out;
  for(const [t,n] of Object.entries(v as Record<string,unknown>)){
    if(n===undefined||n===null||n==='')continue;
    k.assert(types.includes(t),`Unknown room type “${t}”.`);
    k.assert(Number.isSafeInteger(n)&&(n as number)>0&&(n as number)<=10000000,`Enter a ${t} rate between ₹1 and ₹1,00,000.`);
    out[t]=n as number;
  }
  return out;
}
/** Allotment check: requested unpicked rooms ≤ free rooms (shared availability) − other open blocks' unpicked rooms. */
function checkAllot(k:Kit,a:string,d:string,type:string,need:number,except=''){
  if(need<=0)return;
  const free=grpFreeRooms(k.s,type,a,d).length,held=grpHeldElsewhere(k.s,type,a,d,except),cap=Math.max(0,free-held);
  k.assert(need<=cap,`Only ${cap} ${type} room${cap===1?'':'s'} can be held for these dates (${free} free, ${held} held by other blocks).`,'NOT_AVAILABLE');
}
function validLimit(k:Kit,n:unknown){k.assert(Number.isSafeInteger(n)&&(n as number)>=0&&(n as number)<=GRP_MAX_LIMIT,'Enter a credit limit between ₹0 and ₹1,00,00,000.');return n as number;}

export const handlers:Record<string,Handler>={
  GRP_COMPANY_SAVE:k=>{
    const {s,cmd,req,open,assert,id,uid}=k;req('reservations');open();const x=begin(k);
    const name=str(cmd.name,80);assert(name.length>=2,'Enter the company name.');
    assert(!x.companies.some(c=>c.id!==cmd.id&&c.name.toLowerCase()===name.toLowerCase()),'A company account with this name already exists.');
    const gstin=str(cmd.gstin,15).toUpperCase();assert(!gstin||GRP_GSTIN.test(gstin),'Enter the GSTIN in the 15-character format, e.g. 27ZZFIC0009F1Z5 (fictional).');
    const phone=str(cmd.phone,20);assert(!phone||RES_PHONE.test(phone),'Enter a valid phone number (7–16 digits; fictional).');
    const email=str(cmd.email,80);assert(!email||/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email),'Enter a valid e-mail address (fictional).');
    const rates=rateMap(k,cmd.rates,grpRoomTypes(s));
    const fields={name,gstin,contact:str(cmd.contact,60),phone,email,city:str(cmd.city,40),rates};
    if(cmd.id){const c=companyOf(k,cmd.id);Object.assign(c,fields);return `${c.id} · ${c.name} updated`;}
    const c:GRPCompany={id:id('CO'),...fields,creditLimit:0,openingOutstanding:0,limitHistory:[]};
    x.companies.push(c);
    const limit=cmd.creditLimit===undefined||cmd.creditLimit===''?0:validLimit(k,cmd.creditLimit);
    if(limit>0){
      if(can(s,uid,'admin')){c.creditLimit=limit;c.limitHistory!.push({from:0,to:limit,at:s.clock,actor:uid,reason:'Opening limit'});return `${c.id} · ${c.name} created · credit limit ${money(limit)}`;}
      x.requests.push({id:id('GLR'),company:c.id,from:0,to:limit,reason:'Opening limit for new account',actor:uid,at:s.clock,status:'Pending'});
      return `${c.id} · ${c.name} created · credit limit ${money(limit)} requested`;
    }
    return `${c.id} · ${c.name} created`;
  },
  GRP_LIMIT_SET:k=>{
    const {s,cmd,uid,req,open,assert,id}=k;open();const x=begin(k);
    const c=companyOf(k,cmd.company),limit=validLimit(k,cmd.limit),reason=str(cmd.reason,200);
    assert(limit!==c.creditLimit,'The new limit is the same as the current limit.');
    if(can(s,uid,'admin')){c.limitHistory=[...(c.limitHistory||[]),{from:c.creditLimit,to:limit,at:s.clock,actor:uid,reason:reason||'Set by administrator'}];
      const from=c.creditLimit;c.creditLimit=limit;
      x.requests.filter(r=>r.company===c.id&&r.status==='Pending').forEach(r=>{r.status='Rejected';r.approver=uid;r.decidedAt=s.clock;r.note='Superseded by administrator change';});
      return `${c.name} · credit limit ${money(from)} → ${money(limit)}`;}
    req('reservations');
    assert(reason.length>=3,'Give a reason for the credit-limit request.');
    assert(!x.requests.some(r=>r.company===c.id&&r.status==='Pending'),'A credit-limit request for this company is already pending.','INVALID_TRANSITION');
    const rid=id('GLR');x.requests.push({id:rid,company:c.id,from:c.creditLimit,to:limit,reason,actor:uid,at:s.clock,status:'Pending'});
    return `${rid} · ${c.name} · limit ${money(c.creditLimit)} → ${money(limit)} requested`;
  },
  GRP_LIMIT_DECIDE:k=>{
    const {s,cmd,uid,req,open,assert}=k;req('approve');open();const x=begin(k);
    const r=x.requests.find(r=>r.id===cmd.id);assert(r&&r.status==='Pending','This credit-limit request is no longer pending.','INVALID_TRANSITION');
    assert(r!.actor!==uid,'You cannot approve your own request. Switch to another authorized demo user.','FORBIDDEN');
    const c=companyOf(k,r!.company);
    if(cmd.approve){c.limitHistory=[...(c.limitHistory||[]),{from:c.creditLimit,to:r!.to,at:s.clock,actor:uid,reason:r!.reason,request:r!.id}];c.creditLimit=r!.to;}
    r!.status=cmd.approve?'Approved':'Rejected';r!.approver=uid;r!.decidedAt=s.clock;const note=str(cmd.note,200);if(note)r!.note=note;
    return `${r!.id} · ${c.name} · ${r!.status}${cmd.approve?' · limit '+money(r!.to):''}`;
  },
  GRP_BLOCK_CREATE:k=>{
    const {s,cmd,uid,req,open,assert,id}=k;req('reservations');open();const x=begin(k);
    const name=str(cmd.name,80);assert(name.length>=3,'Enter a name for the group.');
    const a=String(cmd.arrival||''),d=String(cmd.departure||''),cut=String(cmd.cutoff||'');
    assert(DATE.test(a)&&DATE.test(d)&&nights(a,d)>0&&nights(a,d)<=30,'Departure must follow arrival (maximum 30 nights in this demo).');
    assert(a>=s.date,'Arrival cannot be before the current business date.');
    assert(DATE.test(cut)&&cut>=s.date&&cut<=a,'The cut-off date must be between today’s business date and the arrival date.');
    const billTo=cmd.billTo==='company'?'company':'guest';
    const c=cmd.company?companyOf(k,cmd.company):undefined;
    assert(billTo!=='company'||c,'Choose the company account to bill.');
    const types=grpRoomTypes(s),allot:Record<string,number>={};
    for(const [t,n] of Object.entries((cmd.allot||{}) as Record<string,unknown>)){
      assert(types.includes(t),`Unknown room type “${t}”.`);
      assert(Number.isInteger(n)&&(n as number)>=0&&(n as number)<=50,`Enter 0–50 ${t} rooms.`);
      if((n as number)>0)allot[t]=n as number;
    }
    assert(Object.keys(allot).length>0,'Hold at least one room.');
    for(const [t,n] of Object.entries(allot))checkAllot(k,a,d,t,n);
    const given=rateMap(k,cmd.rates,types),rates:Record<string,number>={};
    for(const t of Object.keys(allot)){const rack=s.rooms.find(r=>r.type===t)!.rate;rates[t]=given[t]??c?.rates[t]??rack;}
    const blk:GRPBlock={id:id('GB'),name,company:c?.id,billTo,arrival:a,departure:d,cutoff:cut,allot,rates,released:{},members:[],status:'Open',
      contact:str(cmd.contact,60)||undefined,notes:str(cmd.notes,200)||undefined,createdBy:uid,createdAt:s.clock};
    x.blocks.push(blk);
    return `${blk.id} · ${name} · ${Object.entries(allot).map(([t,n])=>n+' '+t).join(', ')} · ${a}→${d} · cut-off ${cut}`;
  },
  GRP_BLOCK_ALLOT:k=>{
    const {s,cmd,req,open,assert}=k;req('reservations');open();begin(k);const blk=blockOf(k,cmd.id);
    assert(blk.status==='Open','Only an open block can change its allotment.','INVALID_TRANSITION');
    const types=grpRoomTypes(s),next:Record<string,number>={...blk.allot};
    for(const [t,n] of Object.entries((cmd.allot||{}) as Record<string,unknown>)){
      assert(types.includes(t),`Unknown room type “${t}”.`);assert(Number.isInteger(n)&&(n as number)>=0&&(n as number)<=50,`Enter 0–50 ${t} rooms.`);
      const picked=grpPicked(s,blk,t);assert((n as number)>=picked,`${t}: ${picked} room${picked===1?' is':'s are'} already picked up — the allotment cannot go below that.`);
      const add=(n as number)-(blk.allot[t]||0);
      if(add>0){checkAllot(k,blk.arrival<s.date?s.date:blk.arrival,blk.departure,t,add+grpRemaining(s,blk,t),blk.id);}
      next[t]=n as number;if(!blk.rates[t])blk.rates[t]=grpCompany(s,blk.company)?.rates[t]??s.rooms.find(r=>r.type===t)!.rate;
    }
    const before=Object.entries(blk.allot).map(([t,n])=>n+' '+t).join(', ');blk.allot=next;
    return `${blk.id} · allotment ${before} → ${Object.entries(next).filter(([,n])=>n>0).map(([t,n])=>n+' '+t).join(', ')}`;
  },
  GRP_ROOMING:k=>{
    const {s,cmd,uid,req,open,assert,fail}=k;req('reservations');open();begin(k);const blk=blockOf(k,cmd.block);
    assert(blk.status==='Open',blk.status==='Released'?`Pickup is closed: unpicked rooms were released${blk.releasedBy==='auto'?' at the cut-off ('+blk.cutoff+')':''}.`:'This block was cancelled.','INVALID_TRANSITION');
    const rows=Array.isArray(cmd.rows)?cmd.rows as Record<string,unknown>[]:[];
    assert(rows.length>0&&rows.length<=50,'Add 1–50 guests to the rooming list.');
    const company=grpCompany(s,blk.company),bill=blk.billTo==='company'&&company;
    const before=bill?grpCredit(s,company!).exposure:0;
    const made:string[]=[];
    rows.forEach((row,i)=>{
      const n=i+1,type=String(row.type||'');
      assert(blk.allot[type]>0,`Row ${n}: ${type||'room type'} is not part of this block.`);
      assert(grpRemaining(s,blk,type)>0,`Row ${n}: no ${type} rooms left in the allotment (${blk.allot[type]} held, all picked up).`,'NOT_AVAILABLE');
      const a=blk.arrival<s.date?s.date:blk.arrival;
      let room=str(row.room,6);
      if(room){const r=s.rooms.find(r=>r.id===room);assert(r&&r.type===type,`Row ${n}: room ${room} is not a ${type} room.`);}
      else{room=grpPickupRooms(s,blk,type,a,blk.departure)[0]||'';
        if(!room)fail('NOT_AVAILABLE',`Row ${n}: no ${type} room is free for ${a}→${blk.departure}.`);}
      const people=Number(row.people||1);
      try{withHoldExempt(blk.id,()=>reservations.BOOK({...k,cmd:{type:'BOOK',guest:row.guest,room,arrival:a,departure:blk.departure,people,children:row.children,phone:row.phone,
        source:'Group · '+blk.id,restrictionOverride:cmd.restrictionOverride,company:bill?{name:company!.name,gstin:company!.gstin||undefined}:undefined,notes:`Group block ${blk.id} · ${blk.name}`}}));}
      catch(e){const err=e as Error&{code?:string};fail((err.code||'VALIDATION') as never,`Row ${n} (${str(row.guest,40)||'guest'}): ${err.message}`);}
      const b=s.bookings.at(-1)!;b.rate=blk.rates[type]||b.rate;
      blk.members.push({booking:b.id,type});made.push(`${b.id} ${b.room}`);
    });
    let extra='';
    if(bill){
      const after=grpCredit(s,company!);
      if(after.exposure>company!.creditLimit){
        if(!cmd.override)fail('VALIDATION',`Credit limit exceeded: ${company!.name} exposure would be ${money(after.exposure)} against a limit of ${money(company!.creditLimit)} (+${money(after.exposure-before)} from this list). An approver may override with a reason.`);
        req('approve');const reason=str(cmd.overrideReason,200);assert(reason.length>=3,'Give a reason for overriding the credit limit.');
        const x=grpEnsure(s);x.overrides.push({id:k.id('GCO'),block:blk.id,company:company!.id,exposure:after.exposure,limit:company!.creditLimit,reason,actor:uid,at:s.clock});
        extra=` · credit limit overridden (${money(after.exposure)} / ${money(company!.creditLimit)}): ${reason}`;
      }
    }
    return `${blk.id} · ${made.length} picked up: ${made.join(', ')}${extra}`;
  },
  GRP_RELEASE:k=>{
    const {s,cmd,uid,req,open,assert}=k;req('reservations');open();begin(k);const blk=blockOf(k,cmd.id);
    assert(blk.status==='Open','Only an open block can release rooms.','INVALID_TRANSITION');
    if(cmd.roomType){
      const t=String(cmd.roomType),rem=grpRemaining(s,blk,t),n=Number(cmd.count??rem);
      assert(rem>0,`No unpicked ${t} rooms to release.`);assert(Number.isInteger(n)&&n>=1&&n<=rem,`Release 1–${rem} ${t} rooms.`);
      blk.allot[t]-=n;blk.released[t]=(blk.released[t]||0)+n;
      return `${blk.id} · released ${n} ${t}`;
    }
    for(const t of Object.keys(blk.allot)){const rem=grpRemaining(s,blk,t);blk.allot[t]-=rem;blk.released[t]=(blk.released[t]||0)+rem;}
    blk.status='Released';blk.releasedBy=uid;blk.releasedAt=s.clock;
    return `${blk.id} · all unpicked rooms released; pickup closed`;
  },
  GRP_SWEEP:k=>{const {req}=k;req('reservations');grpEnsure(k.s);const ids=grpSweep(k.s);return ids.length?`Auto-release at cut-off · ${ids.join(', ')}`:'Auto-release · nothing due';},
  GRP_BLOCK_CANCEL:k=>{
    const {s,cmd,req,open,assert,uid}=k;req('reservations');open();begin(k);const blk=blockOf(k,cmd.id);
    assert(blk.status!=='Cancelled','This block is already cancelled.','INVALID_TRANSITION');
    const live=grpMembers(s,blk).filter(m=>['Held','Confirmed','In house'].includes(m.b.status));
    assert(!live.length,`Cancel or check out the ${live.length} member booking${live.length===1?'':'s'} first (${live.map(m=>m.b.id).join(', ')}).`,'INVALID_TRANSITION');
    const reason=str(cmd.reason,200);assert(reason.length>=3,'Give a cancellation reason.');
    for(const t of Object.keys(blk.allot)){const rem=Math.max(0,blk.allot[t]-grpPicked(s,blk,t));blk.allot[t]-=rem;blk.released[t]=(blk.released[t]||0)+rem;}
    blk.status='Cancelled';blk.cancelReason=reason;blk.releasedBy=uid;blk.releasedAt=s.clock;
    return `${blk.id} · cancelled · ${reason}`;
  },
};
