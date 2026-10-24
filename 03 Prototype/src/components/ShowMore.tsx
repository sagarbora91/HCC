// Long lists render a page at a time (desktop tables 100 rows, phone lists 50 cards) with a "Show more" button (plan §4.6).
import React,{useState} from 'react';
export const DESKTOP_PAGE=100,PHONE_PAGE=50;
export function usePaged<T>(rows:readonly T[],step:number){
  const [n,setN]=useState(step);
  const shown=rows.slice(0,n);
  return {shown,total:rows.length,more:n<rows.length,showMore:()=>setN(x=>x+step),step};
}
export function ShowMore({p}:{p:{shown:readonly unknown[];total:number;more:boolean;showMore:()=>void;step:number}}){
  if(!p.more)return null;
  return <div className="show-more"><span role="status">Showing {p.shown.length.toLocaleString('en-IN')} of {p.total.toLocaleString('en-IN')} rows. Totals above include every row.</span>
    <button type="button" className="btn" onClick={p.showMore}>Show {Math.min(p.step,p.total-p.shown.length)} more</button></div>;
}
