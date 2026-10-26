// GSTR-1 working summary from the server (DEC-072 / audit A03a-01): GET /invoices/gstr1?from=&to= (PERM-INV-001; 08 Production
// InvoiceQueries.Gstr1Async) — rate-wise B2B / B2CL / B2CS / CDNR / CDNUR, the SAC table and the documents table (INV, RST, BOS, CRN with the
// cancelled count) — mapped to the prototype's Gstr1Summary shape so the Compliance › GSTR-1 dialog renders the same way in both builds.
// Production build only: reached from `process.env.HCC_BUILD==='production'` branches (no top-level calls).
import type {SyncApi} from './types';
import type {Gstr1Summary,Gstr1Section} from '../../domain/gstr1';
import {paise} from './map';

// ── Server DTOs (08 Production/src/Hcc.Contracts/Invoices/InvoiceContracts.cs) ──
type SrvGstr1Rate={section:string;taxBps:number;taxablePaise:number;cgstPaise:number;sgstPaise:number;igstPaise:number;documents:number};
type SrvGstr1Sac={sac:string;taxBps:number;taxablePaise:number;cgstPaise:number;sgstPaise:number;igstPaise:number};
type SrvGstr1Doc={docType:string;first:string;last:string;count:number;cancelled?:number};
export type SrvGstr1View={from:string;to:string;rates:SrvGstr1Rate[];sac:SrvGstr1Sac[];documents:SrvGstr1Doc[];note:string};

/** Server summary → prototype shape (money stays integer paise; nothing is recomputed). */
export function gstr1FromServer(v:SrvGstr1View):Gstr1Summary{
  return {from:v.from,to:v.to,
    rates:v.rates.map(r=>({section:r.section as Gstr1Section,bps:r.taxBps,taxable:paise(r.taxablePaise,'taxablePaise'),cgst:paise(r.cgstPaise,'cgstPaise'),
      sgst:paise(r.sgstPaise,'sgstPaise'),igst:paise(r.igstPaise,'igstPaise'),documents:r.documents})),
    sac:v.sac.map(r=>({sac:r.sac,bps:r.taxBps,taxable:paise(r.taxablePaise,'taxablePaise'),cgst:paise(r.cgstPaise,'cgstPaise'),sgst:paise(r.sgstPaise,'sgstPaise'),
      igst:paise(r.igstPaise,'igstPaise')})),
    documents:v.documents.map(d=>({docType:d.docType,first:d.first,last:d.last,count:d.count,cancelled:d.cancelled??0})),
    note:v.note};
}
export async function loadServerGstr1(api:SyncApi,from:string,to:string):Promise<Gstr1Summary>{
  return gstr1FromServer(await api.get<SrvGstr1View>('/invoices/gstr1',{from,to}));
}
