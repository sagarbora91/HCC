// F7-R1 · OP-13 computed-column hook (RPT-ARCHITECTURE §6). Today's only engine is fixed code: `expr` names a registered function over other
// columns of the same row. Integer paise, half away from zero — the server FixedCodeEngine gives the same results.
import type {RptColumn,RptRow,RptCell,RptComputed} from './types';

/** Half away from zero for n / d (d ≠ 0). */
export const rptDivRound=(n:number,d:number)=>{if(!d)return 0;const q=Math.abs(n)*2+Math.abs(d),r=Math.floor(q/(2*Math.abs(d)));return (n<0)!==(d<0)?-r:r;};
type Fn=(a:number[])=>number;
const FNS:Record<string,Fn>={
  add:a=>a.reduce((x,y)=>x+y,0),
  sub:a=>a.slice(1).reduce((x,y)=>x-y,a[0]??0),
  /** Integer division, half away from zero (ADR = room revenue / nights). */
  div:a=>rptDivRound(a[0]??0,a[1]??0),
  /** Percent with one decimal (49.4 = 49.4 %), half away from zero on the tenth. */
  pct:a=>rptDivRound((a[0]??0)*1000,a[1]??0)/10,
  neg:a=>-(a[0]??0),
};
const PARSE=/^([a-z]+)\(([a-zA-Z0-9_,\s]*)\)$/;
export interface RptComputedEngine{name:string;canEvaluate(c:RptComputed):boolean;evaluate(c:RptComputed,cells:Record<string,RptCell>):RptCell}
export const fixedCodeEngine:RptComputedEngine={name:'fixed-code',
  canEvaluate:c=>{const m=PARSE.exec(c.expr);return !!m&&m[1] in FNS;},
  evaluate:(c,cells)=>{const m=PARSE.exec(c.expr)!;const args=m[2].split(',').map(x=>x.trim()).filter(Boolean);
    if(args.some(k=>cells[k]===null||cells[k]===undefined||typeof cells[k]!=='number'))return null;
    return FNS[m[1]](args.map(k=>cells[k] as number));}};
const ENGINES:RptComputedEngine[]=[fixedCodeEngine];
/** Registers another engine (OP-13 calculation engine) ahead of the fixed code. */
export const registerComputedEngine=(e:RptComputedEngine)=>{ENGINES.unshift(e);};
export function evaluateComputed(c:RptComputed,cells:Record<string,RptCell>):RptCell{
  const e=ENGINES.find(x=>x.canEvaluate(c));if(!e)throw new Error(`No engine evaluates ${c.expr}.`);return e.evaluate(c,cells);
}
/** Fills every computed column the selector left empty (undefined), on rows, subtotals and totals alike (a percent total is computed, not summed). */
export function computeColumns(columns:RptColumn[],rows:RptRow[]):RptRow[]{
  const comp=columns.filter(c=>c.computed);if(!comp.length)return rows;
  return rows.map(r=>{const cells={...r.cells};for(const c of comp)if(cells[c.key]===undefined)cells[c.key]=evaluateComputed(c.computed!,cells);return {...r,cells};});
}
/** A computed column whose deps include a money column is a money column (§6). */
export function moneyColumns(columns:RptColumn[]):Set<string>{
  const m=new Set(columns.filter(c=>c.money).map(c=>c.key));let grew=true;
  while(grew){grew=false;for(const c of columns)if(!m.has(c.key)&&c.computed?.deps.some(d=>m.has(d))){m.add(c.key);grew=true;}}
  return m;
}
