// Long lists render a page at a time (desktop tables 100 rows, phone lists 50 cards) with a "Show more" button (plan §4.6).
import React,{useState} from 'react';
import {usePrefs,resolvePageSize} from '../app/prefs';
export const DESKTOP_PAGE=100,PHONE_PAGE=50;
/** CP-1 C7: DESKTOP_PAGE / PHONE_PAGE follow the device preference (phones capped at 100); any other step (e.g. 31 closings) is kept. */
export function usePageStep(step:number){const {prefs}=usePrefs();return step===DESKTOP_PAGE?resolvePageSize(prefs.listPageSize,false):step===PHONE_PAGE?resolvePageSize(prefs.listPageSize,true):step;}
export function usePaged<T>(rows:readonly T[],requested:number){
  const step=usePageStep(requested);
  const [n,setN]=useState(step);
  const [seen,setSeen]=useState(step);if(seen!==step){setSeen(step);setN(step);} // preference changed: start again at one page
  const shown=rows.slice(0,n);
  return {shown,total:rows.length,more:n<rows.length,showMore:()=>setN(x=>x+step),step};
}
export function ShowMore({p}:{p:{shown:readonly unknown[];total:number;more:boolean;showMore:()=>void;step:number}}){
  if(!p.more)return null;
  return <div className="show-more"><span role="status">Showing {p.shown.length.toLocaleString('en-IN')} of {p.total.toLocaleString('en-IN')} rows. Totals above include every row.</span>
    <button type="button" className="btn" onClick={p.showMore}>Show {Math.min(p.step,p.total-p.shown.length)} more</button></div>;
}
