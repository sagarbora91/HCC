// W05-A · Master configuration UI helpers shared by the desktop hub, the phone hub and the setting sheet (no React state here).
import {cfgDefs,cfgGroupAccess,cfgGroupSummary,cfgSearch,CFG_GROUPS,mstSearch,mstSpecsOfGroup,mstMaskMoney,type CfgSettingDef,type CfgGroupId,type State} from '../../../services/mock';

/** Editor text for a raw value (money in rupees, percent in %, boolean on/off). */
export function cfgToInput(def:CfgSettingDef,v:unknown):string{
  if(v===undefined||v===null)return '';
  switch(def.type){
    case 'money':return String(Number(v)/100);
    case 'percent':return String(Number(v)/100);
    case 'boolean':return v?'on':'off';
    default:return String(v);
  }
}
/** Raw value from editor text (validation happens in the command; this only converts units). */
export function cfgFromInput(def:CfgSettingDef,t:string):unknown{
  switch(def.type){
    case 'money':case 'percent':return t.trim()===''?NaN:Math.round(Number(t)*100);
    case 'integer':return t.trim()===''?NaN:Number(t);
    case 'boolean':return t==='on';
    case 'choice':return (def.options||[]).find(o=>String(o)===t)??t;
    default:return t;
  }
}
/** MA-10 (DEC-061): roles without money visibility (housekeeping, maintenance, supervisor, roles with no money permission) see no rupee amount anywhere in Master configuration. */
export const cfgMoneyMask=mstMaskMoney;
export type CfgListRow={id:CfgGroupId;label:string;section:string;summary:string;readOnly:boolean;reason?:string;hits?:number;money:boolean;
  /** MA-0: masters (lists) of the group that match the search. */listHits?:number};
/** Rows for the group list (search applied). */
export function cfgListRows(s:State,uid:string,q:string):CfgListRow[]{
  const hits=q.trim()?cfgSearch(s,q):undefined;
  const lists=q.trim()?mstSearch(q):[]; // MA-0: the search also finds masters by label (e.g. "floors")
  const role=s.users.find(u=>u.id===uid)?.role;
  return CFG_GROUPS.filter(g=>g.id!=='internet'||role==='Owner'||role==='Manager').filter(g=>!hits||hits.some(h=>h.group.id===g.id)||lists.some(x=>x.group===g.id)).map(g=>{
    const a=cfgGroupAccess(s,uid,g.id);const h=hits?.find(x=>x.group.id===g.id);const lh=lists.filter(x=>x.group===g.id).length;
    const nl=mstSpecsOfGroup(g.id).length;
    return {id:g.id,label:g.label,section:g.section,summary:cfgMoneyMask(s,uid,cfgGroupSummary(s,g.id))+(nl?` · ${nl} list${nl>1?'s':''}`:''),readOnly:a.readOnly,reason:a.reason,
      hits:(h?.defs.length??0)+lh||undefined,money:g.money,listHits:lh||undefined};
  });
}
/** Settings shown in a group's details (search narrows to the matching ones unless the group itself matched). */
export function cfgDetailDefs(s:State,g:CfgGroupId,q:string):CfgSettingDef[]{
  const all=cfgDefs(s).filter(d=>d.group===g&&!d.panel&&g!=='internet'); // the Internet group is the panel (F5-U1)
  if(!q.trim())return all;
  const h=cfgSearch(s,q).find(x=>x.group.id===g);
  if(!h)return [];
  return h.defs.length?h.defs.filter(d=>!d.panel):all;
}
export const CFG_INVARIANTS:[string,string][]=[
  ['Authorisation enforcement','Every command is checked against module access, the access policy and separation of duty. No setting can switch this off.'],
  ['Room overlap protection','A room can never hold two active stays for the same night.'],
  ['Duplicate payment protection','Each action carries an idempotency key; repeating it never posts a second receipt.'],
  ['Audit history','Every change is written to the audit log with actor and simulated time. The log cannot be disabled or edited.'],
  ['Demonstration watermark','Printed documents always say “Demonstration — not a tax invoice / fictional”.'],
];
/** Group to open when another page links into the hub (read once by the hub on mount). */
export const cfgNav:{group?:CfgGroupId}={};
