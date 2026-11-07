// F12 MEAS · meas-rpt-hk — helpers shared by the six housekeeping / engineering / asset measurement reports (pure; read-only over State).
// Red rows: a row that breaks the entry's exception rule carries `group:'red'` (both sides); the Owner digest and the screen read it. The flag never
// reaches Excel / PDF cells (group is not a column), so screen = Excel = PDF text is unchanged.
import type {State} from '../../types';
import type {RptColumn,RptRow,RptCell} from '../types';
import {addDays,nights} from '../../money';
import {HccError} from '../../errors';

export const T=(key:string,label:string):RptColumn=>({key,label,type:'text',money:false});
export const I=(key:string,label:string):RptColumn=>({key,label,type:'int',money:false});
export const P=(key:string,label:string):RptColumn=>({key,label,type:'pct',money:false});
export const D=(key:string,label:string):RptColumn=>({key,label,type:'date',money:false});
export const M=(key:string,label:string):RptColumn=>({key,label,type:'money',money:true});
/** The red-row flag value (row.group). */
export const RED='red';
export const mkRow=(cells:Record<string,RptCell>,red=false):RptRow=>({kind:'row',cells,...(red?{group:RED}:{})});
export const isRed=(r:RptRow)=>r.group===RED;
/** "Capture needed" note appended to the date basis when nothing is captured yet (zero rows, never an error). */
export const captureNote=(basis:string,need:string)=>`${basis} Nothing is captured yet — capture needed: ${need}.`;
export const median=(xs:number[]):number|null=>{if(!xs.length)return null;const a=[...xs].sort((x,y)=>x-y),m=a.length>>1;return a.length%2?a[m]:(a[m-1]+a[m])/2;};
export const mean=(xs:number[]):number|null=>xs.length?xs.reduce((a,b)=>a+b,0)/xs.length:null;
export const r1=(n:number)=>Math.round(n*10)/10;
/** Fixed two-decimal text (no decimal column type exists); null stays null. */
export const dec2=(n:number|null|undefined)=>n===null||n===undefined||!Number.isFinite(n)?null:(Math.round(n*100)/100).toFixed(2);
export const minutesBetween=(a:string,b:string)=>Math.round((Date.parse(b)-Date.parse(a))/60000);
export function rangeGuard(from:string|undefined,to:string|undefined,maxDays:number,today:string){
  if(from&&to&&nights(from,to)+1>maxDays)throw new HccError('VALIDATION',`The range is longer than ${maxDays} days. Narrow the range.`);
  if(to&&to>today)throw new HccError('VALIDATION',`The report cannot run for a date after the business date (${today}).`);
}
export const eachDay=(from:string,to:string)=>{const out:string[]=[];for(let d=from;d<=to&&out.length<400;d=addDays(d,1))out.push(d);return out;};
/** Rooms in service (not retired): the "available rooms" of a night. */
export const roomsInService=(s:State)=>s.rooms.filter(r=>r.status!=='retired').length;
export const userName=(s:State,id?:string)=>id?(s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id):'';
/** Whole months between two ISO dates (completed months). */
export function monthsBetween(a:string,b:string):number{
  const [ya,ma,da]=a.split('-').map(Number),[yb,mb,db]=b.split('-').map(Number);
  let m=(yb-ya)*12+(mb-ma);if(db<da)m--;return Math.max(0,m);
}
/** Adds whole months to an ISO date (day clipped to the month's end). */
export function addMonths(d:string,n:number):string{
  const [y,m,day]=d.split('-').map(Number);const t=y*12+(m-1)+n,ny=Math.floor(t/12),nm=t%12;
  const last=new Date(Date.UTC(ny,nm+1,0)).getUTCDate();return `${ny}-${String(nm+1).padStart(2,'0')}-${String(Math.min(day,last)).padStart(2,'0')}`;
}
export const ageText=(months:number)=>`${Math.floor(months/12)} y ${months%12} m`;
