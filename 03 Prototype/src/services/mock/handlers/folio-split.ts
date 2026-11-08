// F8 SF · split folio commands (02 Planning/agents/SF-CONTRACT.md §2.2, §3, §4, §7; DEC-152). Lane sf-domain.
// SF_FOLIO_OPEN {id, payer:{kind,ref?,name?,gstin?}, routes, reason?} · SF_ROUTE_SET {id, folio, routes, reason} · SF_FOLIO_REMOVE {id, folio}.
// These commands write no money rows (R7): they configure the stay (who pays which folio, where later postings land) and are audited.
// sfAfterCommand is the routing hook called by services/mock/commands.ts (F8-SF-DOMAIN block) after every handler, before the result is committed.
import type {Handler,Kit} from '../commands';
import {HccError,type ErrorCode} from '../../../domain/errors';
import type {Booking,State,Command} from '../../../domain/types';
import {totals} from '../../../domain/selectors'; // F8D-LOW (L9)
import {sfView} from '../../../domain/streams/folio-split'; // F8D-LOW (L9)
import {grpCompany,grpSlice,grpExposure,grpCreditRefusal,sfFolioExposure} from '../../../domain/streams/groups';
import {SF_FOLIO_IDS,SF_PRIMARY,SF_ROUTE_CATEGORIES,SF_NIGHT_COMMANDS,sfFolios,sfIsSplit,sfCompanyFolio,sfNewLineFolio,sfNewReceiptFolio,
  type SFFolio,type SFFolioId,type SFPayer,type SFReason,type SFRouteCategory} from '../../../domain/streams/folio-split';

/** Refusal with `details.reason` (prototype pattern of FOLIO_DUP_REASON). */
export function sfFail(code:ErrorCode,reason:SFReason,msg:string):never{
  throw Object.assign(new HccError(code,msg),{details:{reason}});
}
const txt=(v:unknown)=>String(v??'').trim();
const GSTIN=/^[0-9]{2}[A-Z0-9]{13}$/;

function parseRoutes(k:Kit,raw:unknown):SFRouteCategory[]{
  const list=raw===undefined||raw===null?[]:raw;
  k.assert(Array.isArray(list),'Routes must be a list of categories (room, extras).');
  const out:SFRouteCategory[]=[];
  for(const r of list as unknown[]){
    k.assert(SF_ROUTE_CATEGORIES.includes(r as SFRouteCategory),'Route a category: room or extras.');
    if(!out.includes(r as SFRouteCategory))out.push(r as SFRouteCategory);
  }
  return out;
}
/** Folio A takes every category no other live folio holds. */
function syncPrimary(x:Booking){
  const fs=x.folios!;const a=fs.find(f=>f.id===SF_PRIMARY)!;
  const held=new Set(fs.filter(f=>f.id!==SF_PRIMARY&&!f.removedAt).flatMap(f=>f.routes));
  a.routes=SF_ROUTE_CATEGORIES.filter(c=>!held.has(c));
}
function ensureRoutesFree(x:Booking,routes:SFRouteCategory[],self?:string){
  for(const c of routes){
    const h=(x.folios??[]).find(f=>f.id!==SF_PRIMARY&&f.id!==self&&!f.removedAt&&f.routes.includes(c));
    if(h)sfFail('VALIDATION','SF_ROUTE_TAKEN',`${c==='room'?'Room':'Extras'} is already routed to folio ${h.id} (${h.payer.name}). Change that folio's routing first.`);
  }
}
const isGroupMember=(s:State,x:Booking)=>grpSlice(s).blocks.some(blk=>blk.members.some(m=>m.booking===x.id));

const SF_FOLIO_OPEN:Handler=k=>{
  // PERM-FOL-002 (billing; SFQ-4 default: no new permission)
  const {s,cmd,req,open,b,assert,uid}=k;
  req('billing');open();const x=b();
  assert(x.status==='Confirmed'||x.status==='In house','A folio can be opened only on a confirmed or in-house stay.','INVALID_TRANSITION');
  if(isGroupMember(s,x))sfFail('INVALID_TRANSITION','SF_GROUP_MEMBER','This stay is a member of a group block: its billing is routed by the group (Groups & corporate › master folio), not split here.');
  const p=(cmd.payer??{}) as Partial<SFPayer>;
  const kind=p.kind;
  if(kind!=='guest'&&kind!=='company'&&kind!=='udhaar')sfFail('VALIDATION','SF_PAYER_KIND','A split folio is paid by a guest, a company account or an Udhaar credit guest.');
  const used=(x.folios??[]).map(f=>f.id);
  const live=sfFolios(x);
  const next=SF_FOLIO_IDS.find(l=>l!==SF_PRIMARY&&!used.includes(l));
  if(live.length>=SF_FOLIO_IDS.length||!next)sfFail('INVALID_TRANSITION','SF_FOLIO_LIMIT',`A stay has at most ${SF_FOLIO_IDS.length} folios (A–D); a removed folio's letter is not reused.`);
  const routes=parseRoutes(k,cmd.routes);
  const reason=txt(cmd.reason);assert(reason.length<=200,'Keep the reason to 200 characters.');
  let payer:SFPayer;
  if(kind==='company'){
    const c=grpCompany(s,txt(p.ref));
    assert(c,'Choose the company account that pays this folio.','NOT_FOUND');
    assert(c!.active!==false,`${c!.name} is retired: choose an active company account.`,'INVALID_TRANSITION');
    if(sfCompanyFolio(x))sfFail('INVALID_TRANSITION','SF_COMPANY_FOLIO','This stay already has a company folio. A stay has at most one company folio.');
    if(x.company?.account&&x.company.account!==c!.id)sfFail('INVALID_TRANSITION','SF_COMPANY_MISMATCH',`This booking is billed to another company account (${x.company.name}). A company folio must name the same company.`);
    payer={kind:'company',name:c!.name,ref:c!.id,...(c!.gstin?{gstin:c!.gstin}:{})};
  }else if(kind==='udhaar'){
    const ref=txt(p.ref);
    const g=(s.guests??[]).find(g=>g.creditAccount&&(g.id===ref||g.creditAccount.customerKey===ref));
    assert(g,'Choose a guest with a credit account (Customers › Credit account) to pay this folio on Udhaar.','NOT_FOUND');
    assert(g!.creditAccount!.active,`${g!.name}'s credit account is inactive.`,'INVALID_TRANSITION');
    payer={kind:'udhaar',name:g!.name,ref:g!.id};
  }else{
    const name=txt(p.name);assert(name.length>=2&&name.length<=120,'Enter the name of the person who pays this folio.');
    const gstin=txt(p.gstin).toUpperCase();assert(!gstin||GSTIN.test(gstin),'Enter a 15-character GSTIN or leave it blank.');
    payer={kind:'guest',name,...(gstin?{gstin}:{})};
  }
  ensureRoutesFree(x,routes);
  if(!x.folios?.length){
    // First split: folio A (the guest) is written and every existing line and receipt is stamped 'A' (amount, tax and date never change).
    const a:SFFolio={id:SF_PRIMARY,payer:{kind:'guest',name:x.guest},routes:[...SF_ROUTE_CATEGORIES],openedAt:s.clock,openedBy:uid};
    x.folios=[a];
    for(const l of x.lines)if(!l.folio)l.folio=SF_PRIMARY;
    for(const r of x.receipts)if(!r.folio)r.folio=SF_PRIMARY;
  }
  const f:SFFolio={id:next!,payer,routes,openedAt:s.clock,openedBy:uid};
  x.folios!.push(f);syncPrimary(x);x.version++;
  return `${x.id} · folio ${f.id} opened for ${payer.name} (${payer.kind})`+(routes.length?` · routes ${routes.join(' + ')}`:'')+(reason?` · ${reason}`:'');
};

const SF_ROUTE_SET:Handler=k=>{
  const {cmd,req,open,b,assert,mutable}=k;
  req('billing');open();const x=b();mutable(x);
  const id=txt(cmd.folio);
  const f=(x.folios??[]).find(f=>f.id===id&&!f.removedAt);
  if(!f||!sfIsSplit(x))sfFail('NOT_FOUND','SF_FOLIO_UNKNOWN',`This stay has no open folio ${id||'(none chosen)'}.`);
  const routes=parseRoutes(k,cmd.routes);
  const reason=txt(cmd.reason);assert(reason.length>=3&&reason.length<=200,'Give the reason for changing the routing (3–200 characters).');
  if(f!.id===SF_PRIMARY){
    // Routing a category back to the guest's folio A: the folio that held it gives it up.
    for(const o of x.folios!)if(o.id!==SF_PRIMARY)o.routes=o.routes.filter(c=>!routes.includes(c));
  }else{ensureRoutesFree(x,routes,f!.id);f!.routes=routes;}
  syncPrimary(x);x.version++;
  return `${x.id} · folio ${f!.id} routes ${f!.routes.length?f!.routes.join(' + '):'none'} (later postings only) · ${reason}`;
};

const SF_FOLIO_REMOVE:Handler=k=>{
  const {cmd,req,open,b,mutable,s}=k;
  req('billing');open();const x=b();mutable(x);
  const id=txt(cmd.folio);
  if(id===SF_PRIMARY)sfFail('INVALID_TRANSITION','SF_FOLIO_NOT_EMPTY','Folio A (the guest\'s) cannot be removed.');
  const f=(x.folios??[]).find(f=>f.id===id&&!f.removedAt);
  if(!f)sfFail('NOT_FOUND','SF_FOLIO_UNKNOWN',`This stay has no open folio ${id||'(none chosen)'}.`);
  if(f!.invoiceNo)sfFail('INVALID_TRANSITION','SF_FOLIO_INVOICED',`Folio ${f!.id} is invoiced (${f!.invoiceNo}).`);
  if(x.lines.some(l=>l.folio===id)||x.receipts.some(r=>r.folio===id))sfFail('INVALID_TRANSITION','SF_FOLIO_NOT_EMPTY',`Folio ${id} has lines or receipts: only an empty folio is removed.`);
  f!.removedAt=s.clock;syncPrimary(x);x.version++;
  return `${x.id} · folio ${id} (${f!.payer.name}) removed`;
};

export const handlers:Record<string,Handler>={SF_FOLIO_OPEN,SF_ROUTE_SET,SF_FOLIO_REMOVE};

/**
 * Routing hook (SF-CONTRACT §3 "Prototype mechanism"): runs inside execute() after the handler, on the draft. Only when the command touched
 * the bookings slice (copy-on-write: an untouched slice is still a lazy getter) and only for stays with `folios`:
 *  - every new line without `folio` gets its folio (sfNewLineFolio: reversal → original's, plan line → its night's, else the route);
 *  - every new receipt without `folio` gets 'A' (a refund: the refunded receipt's folio);
 *  - D7 (§7.3): a command other than a night posting that adds a line to a company folio, or opens / re-routes one, is refused when the
 *    company's exposure (that folio counted, the stay's other folios never) goes over the limit. Night postings are never refused.
 * A refusal throws, so execute() returns nothing and the input state stays untouched.
 */
export function sfAfterCommand(input:State,s:State,cmd:Command):void{
  const d=Object.getOwnPropertyDescriptor(s,'bookings');
  if(!d||d.get)return;
  const night=SF_NIGHT_COMMANDS.includes(cmd.type);
  for(const b of s.bookings){
    if(!b.folios?.length)continue;
    const prev=input.bookings.find(x=>x.id===b.id);
    const known=new Set(prev?.lines.map(l=>l.id)??[]);
    for(const l of b.lines)if(!l.folio)l.folio=sfNewLineFolio(b,l);
    for(const r of b.receipts)if(!r.folio)r.folio=sfNewReceiptFolio(b,r);
    if(night)continue;
    const cf=sfCompanyFolio(b);if(!cf||!cf.payer.ref)continue;
    const reconfig=cmd.type==='SF_FOLIO_OPEN'||cmd.type==='SF_ROUTE_SET';
    // F8-INT (review M6): the late check-out half day posted inside CHECKOUT is never refused here (like a night): settlement decides (company credit refused → pay)
    if(!reconfig&&!b.lines.some(l=>l.folio===cf.id&&!known.has(l.id)&&!(cmd.type==='CHECKOUT'&&l.id===`late-half:${b.id}`)))continue;
    const after=sfFolioExposure(s,b,cf.id);
    const before=prev&&sfCompanyFolio(prev,cf.payer.ref)?.id===cf.id?sfFolioExposure(input,prev,cf.id):0;
    if(after<=before)continue;
    const c=grpCompany(s,cf.payer.ref);if(!c)continue;
    // F8D-LOW (money review L9): `used` = the company's exposure without this folio. grpExposure counts a stay only while it is not Departed;
    // after check-out the folio's settled part is already in the company's receivable (company-credit receipt) and the folio itself is not in
    // grpExposure, so nothing is subtracted (subtracting `after` made `used` too small: too lenient) and the folio adds only what is still open
    // on it (its balance; sfFolioExposure would count the receivable part a second time).
    const departed=b.status==='Departed';
    const used=departed?grpExposure(s,c):Math.max(0,grpExposure(s,c)-after),needed=departed?Math.max(0,totals(sfView(b,cf.id)).balance):after;
    const refusal=grpCreditRefusal(c,used,needed,'split folio');
    if(refusal)throw new HccError('INVALID_TRANSITION',refusal);
  }
}
export type {SFFolioId};
