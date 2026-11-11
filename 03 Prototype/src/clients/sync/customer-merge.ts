// FX-MERGE-UI · Customers 360 "Possible duplicates" in the production sync layer.
//   server  GET /customers/duplicates · /customers/merges · /customers/profile?key=   POST /commands/ProposeCustomerMerge | DecideCustomerMerge
//           (Hcc.Api CustomersEndpoints, contracts Hcc.Contracts/Customers; right PERM-CRM-003 = Owner and Manager, both steps, maker-checker)
// Slice customerMergeX: `server.pairs` = the server's groups cut into pairs (suggested survivor against each other member), `merges` = the merge requests.
// Keys are the server's opaque ck_ tokens (never a phone number); phone and e-mail arrive masked for readers without contact rights and stay masked.
// "Not the same person" has no server command: CUSTMERGE_DISMISS stays unmapped and the production screen does not offer it.
// A role without PERM-CRM-003 sends no GET (the slice says canMerge:false and the screen shows no list and no action).
import {HccClientError} from '../errors';
import {protoUserId} from './map';
import type {SyncArea,LoaderCtx} from './types';
import type {CMXSlice,CMXMember,CMXPair,CMXMerge,CMXReason} from '../../domain/streams/customer-merge';
import {cmxPairId} from '../../domain/streams/customer-merge';

const PROFILE_GROUPS=20;
const text=(v:unknown)=>String(v??'').trim();

export type SrvSummary={key:string;name:string;phone:string;email:string|null;masked:boolean;stays:number;completedStays:number;nights:number;lifetimePaise:number|null;lastStay:string|null;
  inHouse:boolean;tags:string[];legacyStays?:number;firstStay?:string|null};
export type SrvGroup={reason:string;customers:SrvSummary[];suggestedSurvivor:string};
export type SrvProfile={preferences:string|null;notes:{body:string}[];tags:string[]};
export type SrvMerge={mergeId:number;sourceKey:string;targetKey:string;sourceName:string;targetName:string;reason:string;status:string;proposedBy:number;proposedUtc:string;
  decidedBy:number|null;decidedUtc:string|null;decisionNote:string|null;version:string};

const memberOf=(c:SrvSummary,p?:SrvProfile):CMXMember=>({key:c.key,name:c.name,phone:c.phone||'—',email:c.email??'',stays:c.completedStays+(c.legacyStays??0),nights:c.nights,
  ...(c.firstStay?{firstStay:c.firstStay}:{}),...(c.lastStay?{lastStay:c.lastStay}:{}),...(c.lifetimePaise!==null&&c.lifetimePaise!==undefined?{lifetime:c.lifetimePaise}:{}),
  tags:[...new Set([...(c.tags??[]),...(p?.tags??[])])],...(p?{preferences:text(p.preferences),notes:(p.notes??[]).map(n=>text(n.body)).filter(Boolean).join(' | ')}:{}),inHouse:c.inHouse});
/** Server groups → pairs (suggested survivor against every other member). `profiles` by token. Pure; tested. */
export function cmxPairsFromServer(groups:SrvGroup[],profiles:Record<string,SrvProfile|undefined>={}):CMXPair[]{
  const out=new Map<string,CMXPair>();
  for(const g of groups){
    const reason:CMXReason=g.reason==='email'?'email':'name-without-phone';
    const best=g.customers.find(c=>c.key===g.suggestedSurvivor)??g.customers[0];
    for(const c of g.customers){
      if(!best||c.key===best.key)continue;
      const id=cmxPairId(best.key,c.key);if(out.has(id))continue;
      const [x,y]=best.key<c.key?[best,c]:[c,best];
      out.set(id,{id,reason,a:memberOf(x,profiles[x.key]),b:memberOf(y,profiles[y.key]),suggested:best.key});
    }
  }
  return [...out.values()];
}
export const cmxMergeFromServer=(m:SrvMerge):CMXMerge=>({id:String(m.mergeId),source:m.sourceKey,target:m.targetKey,sourceName:m.sourceName,targetName:m.targetName,reason:m.reason,
  status:m.status==='Approved'?'Approved':m.status==='Rejected'?'Rejected':'Pending',proposedBy:protoUserId(m.proposedBy),proposedAt:m.proposedUtc,
  ...(m.decidedBy!==null&&m.decidedBy!==undefined?{decidedBy:protoUserId(m.decidedBy)}:{}),...(m.decidedUtc?{decidedAt:m.decidedUtc}:{}),...(m.decisionNote?{note:m.decisionNote}:{}),version:m.version});

async function loadMerge(c:LoaderCtx):Promise<CMXSlice>{
  const out:CMXSlice={aliases:{},merges:[],dismissed:[],seq:0,server:{canMerge:false,pairs:[]}};
  if(!c.mayRead('GET /customers/duplicates','GET /customers/merges'))return out;
  const dups=await c.api.get<{groups:SrvGroup[]}>('/customers/duplicates');
  const merges=await c.api.get<{merges:SrvMerge[]}>('/customers/merges');
  const groups=dups.groups??[];
  // Preferences and notes are not in the list view: read the profile of every member of the first groups (a failed read leaves "Not loaded").
  const keys=[...new Set(groups.slice(0,PROFILE_GROUPS).flatMap(g=>g.customers.map(x=>x.key)))];
  const got=await Promise.allSettled(keys.map(k=>c.api.get<SrvProfile>('/customers/profile',{key:k})));
  const profiles:Record<string,SrvProfile|undefined>={};
  keys.forEach((k,i)=>{const r=got[i];if(r.status==='fulfilled')profiles[k]=r.value;});
  out.merges=(merges.merges??[]).map(m=>{c.ids.setVersion('custMerge',String(m.mergeId),m.version);return cmxMergeFromServer(m);})
    .sort((a,b)=>(a.status==='Pending'?0:1)-(b.status==='Pending'?0:1)||b.proposedAt.localeCompare(a.proposedAt)).slice(0,60);
  out.server={canMerge:true,pairs:cmxPairsFromServer(groups,profiles)};
  return out;
}

export const customerMerge:SyncArea={
  id:'customer-merge',
  loaders:[{slice:'customerMergeX',async load(c){return loadMerge(c);}}],
  commands:[
    {type:'CUSTMERGE_PROPOSE',reload:['customerMergeX'],
      precheck:cmd=>text(cmd.source)&&text(cmd.target)?(text(cmd.source)===text(cmd.target)?'Choose two different customers.':undefined):'Choose two customers.',
      async send(c){await c.api.command('ProposeCustomerMerge',{sourceKey:text(c.cmd.source),targetKey:text(c.cmd.target),reason:text(c.cmd.reason)});}},
    {type:'CUSTMERGE_DECIDE',reload:['customerMergeX'],
      async send(c){
        const id=text(c.cmd.id),version=c.ids.version('custMerge',id);
        if(!version||!/^\d+$/.test(id))throw new HccClientError('VALIDATION','This merge request has not been loaded. Refresh and try again.');
        await c.api.command('DecideCustomerMerge',{mergeId:Number(id),approve:c.cmd.approve===true,note:text(c.cmd.note)||undefined},{expectedVersion:version});}},
  ],
  unmapped:{CUSTMERGE_DISMISS:'"Not the same person" has no server command (no table holds dismissed pairs and no migration was allowed in wave FX); the production screen hides it.'},
};
