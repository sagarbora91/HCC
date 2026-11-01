// FIT: In-house stays and Guests keep their old "first record is selected" behaviour on the wide (side-by-side) layout. Below 900 px the record
// slides over the list (SplitView), so nothing is pre-selected there: the list shows first and a tap opens the record.
import {useEffect,useState} from 'react';
const NARROW='(max-width: 899.98px)';
export function useWideSplit():boolean{
  const q=()=>typeof window==='undefined'||typeof window.matchMedia!=='function'||!window.matchMedia(NARROW).matches;
  const [w,setW]=useState(q);
  useEffect(()=>{if(typeof window==='undefined'||typeof window.matchMedia!=='function')return;const m=window.matchMedia(NARROW);const on=()=>setW(!m.matches);on();
    m.addEventListener?.('change',on);return ()=>m.removeEventListener?.('change',on);},[]);
  return w;
}
