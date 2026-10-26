// MA-2 · Meal plans, extras catalogue and discount reasons — commands (domain: domain/streams/catalogue.ts).
//   CAT_CHARGE {id, extraId, qty?, label?, amount?, gst?}   catalogue charge (PERM-FOL-002, coarse billing): name, price × qty and the
//                       item's GST class are the defaults, every one editable (DEC-MA-4: no approval); the line records extraId.
//                       Free text stays CHARGE ("Other (describe)"), unchanged.
//   CAT_DISCOUNT {id, amount, reasonCode, note?}             discount with a reason from the master: the role may use the reason, the
//                       amount stays within the reason's maximum %, above the reason's approval % (and not the Owner) it becomes an
//                       approval request; otherwise the DISCOUNT rules run unchanged (role limits, DEC-063). The line / request
//                       records the reason code. Free-text reasons stay DISCOUNT.
//   CAT_PLAN_SET {id, plan}                                  meal plan of a booking (Held / Confirmed: reservations; In house: billing);
//                       applies from the next room night posted (posted nights keep their lines). '' = no plan.
//   APPROVAL (wraps folio's)                                 an approved discount request keeps its reason code on the posted line.
// The master lists themselves change through the generic MST_MASTER_* commands (handlers/masters.ts; Owner-only money masters).
import type {Handler,Kit} from '../commands';
import type {Approval} from '../../../domain/types';
import {money} from '../../../domain/money';
import {totals,can} from '../../../domain/selectors';
import {gstForCharge,GST_CHARGE_CATEGORIES} from '../../../domain/gst';
import {rbacCanDiscount} from '../../../domain/streams/rbac';
import {folioExpiresAt,folioKind,folioApprovalExpired} from '../../../domain/streams/folio';
import {mstFail} from '../../../domain/streams/masters';
import {catExtra,catExtraPrice,catReason,catReasonLimits,catCheckPlan,catPlanLabel,CAT_QTY_MAX} from '../../../domain/streams/catalogue';
import {handlers as folio} from './folio';

const CAT_CHARGE:Handler=k=>{
  const {s,cmd,req,b,financial,positive,assert,id}=k;
  req('billing');const x=b();financial(x);
  const item=catExtra(s,String(cmd.extraId??''));
  assert(item,'Choose an item from the catalogue, or “Other (describe)” for a free-text charge.','NOT_FOUND');
  if(!item!.active)mstFail('MASTER_RETIRED',`${item!.name} is retired — choose another item or “Other (describe)”.`,'extraId');
  const qty=cmd.qty===undefined||cmd.qty===''?1:Number(cmd.qty);
  assert(Number.isInteger(qty)&&qty>=1&&qty<=CAT_QTY_MAX,`Quantity: a whole number from 1 to ${CAT_QTY_MAX}.`);
  const price=catExtraPrice(item!,s.date);
  const amount=cmd.amount===undefined||cmd.amount===''||cmd.amount===null?(price===undefined?undefined:price*qty):Number(cmd.amount);
  assert(amount!==undefined,`${item!.name} has no price on ${s.date} — enter the amount.`);
  positive(amount!);
  const label=String(cmd.label??'').trim()||(qty>1?`${item!.name} × ${qty}`:item!.name);
  assert(label.length<=120,'Keep the description to 120 characters.');
  const gst=cmd.gst===undefined||cmd.gst===''?item!.gst??'other':cmd.gst;
  assert(GST_CHARGE_CATEGORIES.some(c=>c.id===gst),'Choose a GST category: food, laundry, banquet or other.');
  x.lines.push({id:id('line'),label,amount:amount!,date:s.date,kind:'service',taxBps:gstForCharge(s,s.date,gst).bps,extraId:item!.id});x.version++;
  const edited=[label!==item!.name&&!(qty>1&&label===`${item!.name} × ${qty}`)?'name':'',price!==undefined&&amount!==price*qty?'price':'',gst!==item!.gst?'GST class':''].filter(Boolean);
  return `${x.id} · ${label} · ${money(amount!)} · catalogue ${item!.code}${qty>1?` × ${qty}`:''}${edited.length?` (changed: ${edited.join(', ')})`:''}`;
};

/** Approval request bound to the folio version (same shape as folio.ts request()). */
function requestApproval(k:Kit,bookingId:string,version:number,amount:number,reason:string,reasonCode:string):Approval{
  const a:Approval={id:k.id('APR'),booking:bookingId,actor:k.uid,version,status:'Pending',expiresAt:folioExpiresAt(k.s.clock),amount,reason,meta:{command:'APPROVAL',reasonCode}};
  k.s.approvals.push(a);return a;
}

const CAT_DISCOUNT:Handler=k=>{
  const {s,cmd,req,b,financial,positive,assert,fail,uid,user}=k;
  req('billing');if(!rbacCanDiscount(s,uid))fail('FORBIDDEN','Access denied: discounts are given by Reception, a Manager or the Owner — not at the cash counter (DEC-063).');
  const x=b();financial(x);positive(cmd.amount);
  const r=catReason(s,String(cmd.reasonCode??'').trim().toUpperCase());
  assert(r,'Choose a discount reason, or “Other” and type the reason.','NOT_FOUND');
  if(!r!.active)mstFail('MASTER_RETIRED',`${r!.name} is retired — choose another reason.`,'reasonCode');
  if(r!.roles?.length&&!r!.roles.includes(user.role))fail('FORBIDDEN',`Access denied: “${r!.name}” is used by ${r!.roles.join(', ')} only.`);
  const note=String(cmd.note??'').trim();assert(note.length<=100,'Keep the note to 100 characters.');
  const lim=catReasonLimits(r!,x);
  assert(cmd.amount<=lim.max,`“${r!.name}” allows at most ${(r!.maxPct??10000)/100} % of the charges (${money(lim.max)} on this folio).`);
  const reason=note?`${r!.name} · ${note}`:r!.name;
  if(lim.approvalAbove!==undefined&&cmd.amount>lim.approvalAbove&&user.role!=='Owner'){
    assert(cmd.amount<=totals(x).net,'Discount cannot exceed existing net charges.');
    assert(!s.approvals.some(a=>a.booking===x.id&&folioKind(a)==='discount'&&a.status==='Pending'&&!folioApprovalExpired(s,a)),'A discount request is already pending.','INVALID_TRANSITION');
    requestApproval(k,x.id,x.version,cmd.amount,reason,r!.code);
    return `${x.id} · approval requested (${r!.code}: above ${(r!.approvalAbovePct??0)/100} % of the charges)`;
  }
  const lines=x.lines.length,approvals=s.approvals.length;
  const detail=folio.DISCOUNT({...k,cmd:{...cmd,type:'DISCOUNT',reason}});
  const y=s.bookings.find(v=>v.id===x.id)!;
  if(y.lines.length>lines&&y.lines[y.lines.length-1].kind==='discount')y.lines[y.lines.length-1].discountReason=r!.code;
  if(s.approvals.length>approvals){const a=s.approvals[s.approvals.length-1];a.meta={...(a.meta??{command:'APPROVAL'}),reasonCode:r!.code};}
  return `${detail} · reason ${r!.code}`;
};

const CAT_PLAN_SET:Handler=k=>{
  const {s,cmd,req,b,open,assert,fail,uid}=k;
  if(!can(s,uid,['reservations','billing']))fail('FORBIDDEN','Access denied: reservations or billing permission is required.');
  const x=b();
  assert(['Held','Confirmed','In house'].includes(x.status),'This booking is no longer open.','INVALID_TRANSITION');
  req(x.status==='In house'?'billing':'reservations');open();
  const room=s.rooms.find(r=>r.id===x.room);
  const p=catCheckPlan(s,cmd.plan,room?.type??'',x.plan);
  const next=p?.code,before=x.plan;
  assert(next!==before,'Nothing changed.');
  if(next)x.plan=next;else delete x.plan;x.version++;
  return `${x.id} · meal plan ${catPlanLabel(s,before)} → ${catPlanLabel(s,next)}${x.status==='In house'?' (from the next room night posted)':''}`;
};

const APPROVAL:Handler=k=>{
  const a=k.s.approvals.find(x=>x.id===k.cmd.id);
  const code=a&&folioKind(a)==='discount'?a.meta?.reasonCode as string|undefined:undefined;
  const x=code?k.s.bookings.find(v=>v.id===a!.booking):undefined,n=x?.lines.length??0;
  const detail=folio.APPROVAL(k);
  if(code&&x){const y=k.s.bookings.find(v=>v.id===x.id)!;const l=y.lines[y.lines.length-1];if(y.lines.length>n&&l.kind==='discount')l.discountReason=code;}
  return detail;
};

export const handlers:Record<string,Handler>={CAT_CHARGE,CAT_DISCOUNT,CAT_PLAN_SET,APPROVAL};
