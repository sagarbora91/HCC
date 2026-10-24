// Module 14 · Group & corporate — fixture extensions (fictional data only).
// Companies use clearly fictional GSTIN-format placeholders (PAN part "ZZFIC…"). No money is posted into existing bookings:
// the released Deccan Pharma block only *links* the two Corporate in-house stays (HCC-1044, HCC-1048) as members.
import type {State} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
import type {GRPState} from '../../domain/streams/groups';
const at=(s:State)=>s.date+'T08:00';
export function extendBase(s:State):void{
  const x:GRPState={companies:[
    {id:'CO-1',name:'Deccan Pharma Distributors (fictional)',gstin:'27ZZFIC0001F1Z5',contact:'Rahul Joshi',phone:'+91 90000 00011',email:'travel@deccanpharma.example.test',city:'Pune',
      rates:{Classic:175000,Deluxe:260000,Suite:400000},creditLimit:15000000,openingOutstanding:6200000,limitHistory:[]},
    {id:'CO-2',name:'Godavari Agro Inputs (fictional)',gstin:'27ZZFIC0002F1Z4',contact:'Sunita Patil',phone:'+91 90000 00022',email:'accounts@godavariagro.example.test',city:'Nanded',
      rates:{Classic:180000,Deluxe:270000},creditLimit:10000000,openingOutstanding:8800000,limitHistory:[]},
    {id:'CO-3',name:'Sahyadri Infra Projects (fictional)',gstin:'27ZZFIC0003F1Z3',contact:'Imran Shaikh',phone:'+91 90000 00033',email:'admin@sahyadriinfra.example.test',city:'Latur',
      rates:{Classic:170000,Deluxe:250000,Suite:380000},creditLimit:20000000,openingOutstanding:0,limitHistory:[]},
  ],blocks:[
    {id:'GB-1',name:'Deccan Pharma regional review',company:'CO-1',billTo:'company',arrival:'2026-10-23',departure:'2026-10-26',cutoff:'2026-10-21',
      allot:{Classic:2},rates:{Classic:175000},released:{Classic:2},members:[{booking:'HCC-1044',type:'Classic'},{booking:'HCC-1048',type:'Classic'}],
      status:'Released',releasedBy:'auto',releasedAt:'2026-10-22T08:00',contact:'Rahul Joshi',createdBy:'manager',createdAt:'2026-10-12T11:20'},
    {id:'GB-2',name:'Godavari Agro dealers meet',company:'CO-2',billTo:'company',arrival:'2026-10-30',departure:'2026-11-01',cutoff:'2026-10-27',
      allot:{Classic:6,Deluxe:3},rates:{Classic:180000,Deluxe:270000},released:{},members:[],status:'Open',contact:'Sunita Patil',notes:'Dealer meet · breakfast included (simulated)',createdBy:'manager',createdAt:at(s)},
    {id:'GB-3',name:'Kulkarni–Joshi wedding party',billTo:'guest',arrival:'2026-11-06',departure:'2026-11-08',cutoff:'2026-10-24',
      allot:{Classic:5,Suite:1},rates:{Classic:190000,Suite:420000},released:{},members:[],status:'Open',contact:'Mrs. Kulkarni (fictional)',notes:'Each family pays its own folio',createdBy:'reception',createdAt:at(s)},
    {id:'GB-4',name:'Sahyadri Infra site team',company:'CO-3',billTo:'company',arrival:'2026-10-25',departure:'2026-10-28',cutoff:'2026-10-25',
      allot:{Deluxe:3},rates:{Deluxe:250000},released:{},members:[],status:'Open',contact:'Imran Shaikh',createdBy:'manager',createdAt:at(s)},
  ],requests:[
    {id:'GLR-1',company:'CO-2',from:10000000,to:15000000,reason:'Dealer meet billing in two instalments (simulated)',actor:'reception',at:at(s),status:'Pending'},
  ],overrides:[]};
  s.groupsX=x;
}
export const scenarioPatches:Record<string,(s:State)=>void>={};
export const scenarioInfo:ScenarioInfo[]=[];
