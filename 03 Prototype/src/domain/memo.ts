// Small memo helpers for pure selectors. State is immutable in the app (execute() clones it), so a result keyed on the identity of the
// state slices a selector reads stays valid until one of those slices is replaced. Nothing here mutates or stores state.
import type {State} from './types';
import {reportPack,type ReportPack} from './streams/closing';
import {cbRange,type CBRange} from './streams/cashbook';

/** Keeps the last `size` results; a hit needs every dependency to be identical (Object.is) and the same extra key. */
export function memoByDeps<R>(size=8){
  const store:{deps:readonly unknown[];key:string;value:R}[]=[];
  return (deps:readonly unknown[],key:string,compute:()=>R):R=>{
    for(let i=0;i<store.length;i++){const e=store[i];
      if(e.key===key&&e.deps.length===deps.length&&e.deps.every((d,j)=>Object.is(d,deps[j]))){if(i){store.splice(i,1);store.unshift(e);}return e.value;}}
    const value=compute();store.unshift({deps,key,value});if(store.length>size)store.pop();return value;};
}
/** Everything reportPack / cbRange read from the state. */
const moneyDeps=(s:State)=>[s.bookings,s.expenses,s.rooms,s.posX,s.closings,s.closedDates,s.date,s.openingCash,s.shiftClose,s.cashbookX];
const packMemo=memoByDeps<ReportPack>(6),cbMemo=memoByDeps<CBRange>(6);
/** reportPack for a range, reused while the underlying records are unchanged. Treat the result as read-only. */
export const reportPackMemo=(s:State,from:string,to:string)=>packMemo(moneyDeps(s),from+'|'+to,()=>reportPack(s,from,to));
/** cbRange for a range (totals computed once per range), reused while the underlying records are unchanged. */
export const cbRangeMemo=(s:State,from:string,to:string)=>cbMemo(moneyDeps(s),from+'|'+to,()=>cbRange(s,from,to));
