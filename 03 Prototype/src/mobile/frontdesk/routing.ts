// F8C phone · bill routing at check-in (SF-CONTRACT §2.2, §3, §4). Pure: turns "who pays the room / who pays the extras" into the existing
// SF commands (SF_FOLIO_OPEN / SF_ROUTE_SET / SF_FOLIO_REMOVE). Nothing here computes money; the commands validate every rule (limit, one company
// folio, group member, routes exclusive). Same wording as the desk dialog (features/folio/Split.tsx). A group master payer is not offered (SFQ-10).
import type {Booking,Command,State} from '../../domain/types';
import {sfFolios,sfIsSplit,sfRouteTarget,SF_PRIMARY,SF_ROUTE_CATEGORIES,type SFFolio,type SFRouteCategory} from '../../domain/streams/folio-split';
import {grpSlice} from '../../domain/streams/groups';

/** 'guest' | 'company:<account id>' | 'udhaar:<guest id>' (the value of a payer select). */
export type MfPayerKey=string;
export type MfRouting=Record<SFRouteCategory,MfPayerKey>;
export const MF_CATEGORY_LABEL:Record<SFRouteCategory,string>={room:'Room (nights, meal plan, room discounts)',extras:'Extras (laundry, food to room, services)'};
export const MF_GROUP_MESSAGE='This stay is a member of a group block: its billing is routed by the group (Groups & corporate › master folio), not split here.';

export const mfPayerKey=(f:Pick<SFFolio,'payer'>):MfPayerKey=>f.payer.kind==='company'||f.payer.kind==='udhaar'?`${f.payer.kind}:${f.payer.ref??''}`:'guest';
/** Group members refuse any split (SF_GROUP_MEMBER). */
export const mfIsGroupMember=(s:State,b:Booking):boolean=>grpSlice(s).blocks.some(blk=>blk.members.some(m=>m.booking===b.id));
/** The payer now routed for each category (guest when the stay is not split). */
export function mfCurrentRouting(b:Booking):MfRouting{
  const out={room:'guest',extras:'guest'} as MfRouting;
  if(!sfIsSplit(b))return out;
  for(const c of SF_ROUTE_CATEGORIES){const id=sfRouteTarget(b,c),f=sfFolios(b).find(x=>x.id===id);out[c]=f?mfPayerKey(f):'guest';}
  return out;
}
const kindRef=(k:MfPayerKey)=>{const i=k.indexOf(':');return i<0?{kind:k,ref:''}:{kind:k.slice(0,i),ref:k.slice(i+1)};};
/** Why a choice cannot be applied (desk wording of SF_COMPANY_FOLIO), or ''. */
export function mfRoutingProblem(b:Booking,want:MfRouting):string{
  const cos=new Set(SF_ROUTE_CATEGORIES.map(c=>want[c]).filter(k=>k.startsWith('company:')));
  if(cos.size>1)return 'A stay has at most one company folio. Choose the same company for room and extras.';
  // The existing company folio stays when it has postings or still holds a category the clerk keeps on that company; an empty one is replaced.
  const keep=sfFolios(b).find(f=>f.id!==SF_PRIMARY&&f.payer.kind==='company'&&(b.lines.some(l=>l.folio===f.id)||b.receipts.some(r=>r.folio===f.id)||f.routes.some(c=>want[c]===mfPayerKey(f))));
  if(keep&&cos.size===1&&!cos.has(mfPayerKey(keep)))return 'This stay already has a company folio. A stay has at most one company folio.';
  return '';
}
/**
 * The commands that turn the current routing into `want`, in a safe order: give categories back (a category is held by one folio only), remove
 * folios left empty, then add. Posted lines never move (SFQ-5): the routing applies to later postings only. `key` prefixes the idempotency keys.
 */
export function mfRoutingPlan(b:Booking,want:MfRouting,key:string,reason='Set at phone check-in'):Command[]{
  const have=mfCurrentRouting(b);const cmds:Command[]=[];
  const folios=sfFolios(b).map(f=>({id:f.id as string,key:mfPayerKey(f),routes:[...f.routes] as SFRouteCategory[],empty:!b.lines.some(l=>l.folio===f.id)&&!b.receipts.some(r=>r.folio===f.id),existing:true}));
  const take=(f:typeof folios[number],c:SFRouteCategory)=>{f.routes=f.routes.filter(x=>x!==c);};
  const changed=SF_ROUTE_CATEGORIES.filter(c=>have[c]!==want[c]);
  // 1. give back
  for(const c of changed){const f=folios.find(x=>x.id!==SF_PRIMARY&&x.routes.includes(c));
    if(f){take(f,c);cmds.push({type:'SF_ROUTE_SET',id:b.id,folio:f.id,routes:[...f.routes],reason,key:`${key}:give:${f.id}:${c}`});}}
  // 2. remove folios that are now empty and unrouted
  for(const f of folios)if(f.id!==SF_PRIMARY&&f.existing&&!f.routes.length&&f.empty&&changed.length&&cmds.some(x=>x.folio===f.id))
    cmds.push({type:'SF_FOLIO_REMOVE',id:b.id,folio:f.id,key:`${key}:rm:${f.id}`});
  const gone=new Set(cmds.filter(x=>x.type==='SF_FOLIO_REMOVE').map(x=>x.folio as string));
  // 3. add: group the changed categories by wanted payer
  const byPayer=new Map<MfPayerKey,SFRouteCategory[]>();
  for(const c of changed)if(want[c]!=='guest')byPayer.set(want[c],[...(byPayer.get(want[c])??[]),c]);
  for(const [p,cats] of byPayer){
    const f=folios.find(x=>x.id!==SF_PRIMARY&&!gone.has(x.id)&&x.key===p);
    if(f){f.routes=[...f.routes,...cats];cmds.push({type:'SF_ROUTE_SET',id:b.id,folio:f.id,routes:[...f.routes],reason,key:`${key}:add:${f.id}`});}
    else{const {kind,ref}=kindRef(p);cmds.push({type:'SF_FOLIO_OPEN',id:b.id,payer:{kind,ref},routes:cats,reason,key:`${key}:open:${p}`});}
  }
  return cmds;
}
