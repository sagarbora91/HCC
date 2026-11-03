// F7-R4 · shared helpers of the money-a reports (batch 1). No figures of their own: every number comes from an existing selector.
import type {State} from '../../types';
import type {RptColumn,RptRow,RptParam} from '../types';
import {rptHolds} from '../mask';

export const col=(key:string,label:string,type:RptColumn['type'],money=false,extra:Partial<RptColumn>={}):RptColumn=>({key,label,type,money,...extra});
export const userName=(s:State,id:string|undefined)=>!id?'':s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id;
export const rangeParams=(dc:'D-B'|'D-F'):RptParam[]=>[{key:'from',label:'From',type:'date',dateClass:dc},{key:'to',label:'To',type:'date',dateClass:dc}];
/** True when the caller sees the whole property (PERM-RPT-001 or the finance gate without the own-shift scope); a Cashier holds only V·S. */
export const seesAll=(s:State,uid:string)=>rptHolds(s,uid,'PERM-RPT-001',false)||rptHolds(s,uid,'PERM-RPT-002',false);
export const sumCells=(rows:RptRow[],key:string)=>rows.reduce((a,r)=>a+(Number(r.cells[key])||0),0);
export const rowOf=(kind:RptRow['kind'],cells:RptRow['cells'],group?:string):RptRow=>({kind,cells,...(group!==undefined?{group}:{})});
