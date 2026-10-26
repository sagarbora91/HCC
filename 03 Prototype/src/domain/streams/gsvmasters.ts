// MA-7 · Guest-services masters (plan Part 2 §8 M25): complaint categories and lost-and-found categories, each with a department and an
// SLA in hours. Registered in the masters framework in the group "Front-desk policies" (next to the guest-service SLAs); changed through
// the generic MST_MASTER_* commands (Owner + Tech admin). Helpers (resolve, labels, SLA, reports) live in services.ts.
import {registerMaster,type MasterSpec} from './masters';
import {GSV_DEPTS,GCC,LFC,GCC_SEED,LFC_SEED,gcatKeyOf,lfcKeyOf,gsvSlice,type GcatRow,type LfcRow} from './services';

const deptOptions=GSV_DEPTS.map(d=>({value:d,label:d}));
const common=(sla:string)=>[
  {key:'code',label:'Code',type:'text' as const,required:true,maxLength:20,placeholder:'NOISE',help:'Short code. Cannot change once records use it.'},
  {key:'name',label:'Name',type:'text' as const,required:true,maxLength:60},
  {key:'sortOrder',label:'Sort order',type:'number' as const,min:0,max:1_000_000},
  {key:'department',label:'Department',type:'select' as const,required:true,options:deptOptions,help:'Who normally handles it.'},
  {key:'slaHours',label:'SLA (hours)',type:'number' as const,required:true,min:1,max:720,help:sla},
];

export const GCC_SPEC:MasterSpec<GcatRow>={
  id:GCC,label:'Complaint categories',singular:'complaint category',group:'frontdesk',prefix:'GCC',money:false,permission:'masters',
  help:'Categories offered when logging a complaint, with the department and the hours within which it should be answered. Guest services and the complaints report group by them.',
  fields:[...common('Hours from logging within which the complaint should be answered or resolved; Guest services flags “Over SLA”.'),{key:'active',label:'Active',type:'toggle'}],
  inUse:(s,row)=>{const n=gsvSlice(s).complaints.filter(c=>gcatKeyOf(s,c)===row.code).length;return {count:n,where:n?[`${n} complaint${n===1?'':'s'}`]:[]};},
  seed:()=>GCC_SEED.map(([code,name,department,slaHours],i)=>({id:'gcc-'+code.toLowerCase(),code,name,sortOrder:(i+1)*10,active:true,department,slaHours})),
};
export const LFC_SPEC:MasterSpec<LfcRow>={
  id:LFC,label:'Lost-and-found categories',singular:'lost-and-found category',group:'frontdesk',prefix:'LFC',money:false,permission:'masters',
  help:'Categories offered when logging a found item. Valuable categories are always held in the front-office safe.',
  fields:[...common('Hours from finding within which the owner should be traced and informed (shown on the item).'),
    {key:'valuable',label:'Valuable',type:'toggle' as const,help:'Held in the front-office safe; disposal needs a manager.'},{key:'active',label:'Active',type:'toggle'}],
  inUse:(s,row)=>{const n=gsvSlice(s).items.filter(i=>lfcKeyOf(i)===row.code).length;return {count:n,where:n?[`${n} item${n===1?'':'s'}`]:[]};},
  seed:()=>LFC_SEED.map(([code,name,department,slaHours,valuable],i)=>({id:'lfc-'+code.toLowerCase(),code,name,sortOrder:(i+1)*10,active:true,department,slaHours,valuable})),
};
registerMaster(GCC_SPEC as unknown as MasterSpec);
registerMaster(LFC_SPEC as unknown as MasterSpec);
