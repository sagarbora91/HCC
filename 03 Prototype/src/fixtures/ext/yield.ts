// Module 13 · Advanced tariff & yield — fixture extensions (fictional data only).
// Rules and a SIMULATED forecast of rooms sold per night (23 sellable rooms; room 308 is blocked). No money is posted; s.tariffs is not touched.
import type {State} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
import {yldEmpty} from '../../domain/streams/yield';
import {addDays} from '../../domain/money';

/** Simulated forecast rooms sold, from 2026-10-24 (Sat). Diwali (fictional demand peak) around 6–10 Nov. */
const FORECAST=[14,16,9,10,12,14,18,20,13,8,9,12,16,21,22,22,20,15,10,9,10,11,12,15,17,12,10,10,11,13];
export function extendBase(s:State):void{
  const y=yldEmpty();
  FORECAST.forEach((n,i)=>{y.forecast[addDays(s.date,i)]=n;});
  y.seasons=[
    {id:'YLDS-1',name:'Diwali festive',from:'2026-11-06',to:'2026-11-10',adjBps:2000,types:[],priority:2},
    {id:'YLDS-2',name:'Latur MIDC trade expo (fictional)',from:'2026-10-28',to:'2026-10-30',adjBps:800,types:['Deluxe','Suite'],priority:1},
    {id:'YLDS-3',name:'Wedding season',from:'2026-11-20',to:'2026-12-14',adjBps:1000,types:['Deluxe','Suite'],priority:1},
  ];
  y.dow=[0,0,0,0,0,500,1000];
  y.occ=[{id:'YLDO-1',op:'>=',occ:80,adjBps:1000},{id:'YLDO-2',op:'>=',occ:90,adjBps:1500},{id:'YLDO-3',op:'<',occ:40,adjBps:-1000}];
  y.restrictions=[
    {id:'YLDR-1',type:'*',from:'2026-11-07',to:'2026-11-09',minStay:2,cta:false,note:'Diwali weekend'},
    {id:'YLDR-2',type:'Suite',from:'2026-11-08',to:'2026-11-08',minStay:1,cta:true,note:'Lakshmi Puja day — suites sold only as part of longer stays'},
  ];
  y.history=[{id:'YLDH-0',at:s.date+'T07:30',actor:'Sagar · Owner',area:'Setup',before:'—',after:'Demo yield rules loaded (fictional)',reason:'Fixture'}];
  s.yieldX=y;
}
export const scenarioPatches:Record<string,(s:State)=>void>={};
export const scenarioInfo:ScenarioInfo[]=[];
