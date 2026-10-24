// MB-08 tiny UI store shared between a phone page and its sheet (sheets render in ModalHost, outside the page tree).
// Per-device UI state only — never hotel data, never persisted.
import {useSyncExternalStore} from 'react';
import type {AdminAuditFilter} from '../../services/mock';
type MaUi={reportFrom:string;reportTo:string;audit:AdminAuditFilter};
let ui:MaUi={reportFrom:'',reportTo:'',audit:{}};
const subs=new Set<()=>void>();
export function maSetUi(patch:Partial<MaUi>){ui={...ui,...patch};subs.forEach(f=>f());}
export function useMaUi():MaUi{return useSyncExternalStore(f=>{subs.add(f);return ()=>{subs.delete(f);};},()=>ui,()=>ui);}
/** Report range; empty = today's business date. */
export function maRange(u:MaUi,today:string){return {from:u.reportFrom||today,to:u.reportTo||today};}
