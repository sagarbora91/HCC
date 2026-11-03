// F7-R6 · rpt.acc.tally-xml — Tally export (masters per FY + vouchers per range), ACC-006 (catalogue §3.5). The report is the file summary with a screen preview
// of what the two files hold; the XML download itself stays the ACC-3 product (GET /accounting/tally/masters, /vouchers; recorded as ACC_TALLY_EXPORT).
// Adapter over accTallyMasters / accTallyVouchers. Nothing is sent to Tally. The 31-day cap is a file rule.
import {registerReport} from '../catalogue';
import {accTallyMasters,accTallyVouchers} from '../../streams/accounting-tally';
import {accYearProvisional,ACC_PROVISIONAL_TEXT} from '../../streams/accounting-year';
import {HccError} from '../../errors';
import {addDays} from '../../money';
import {T,I,ACC_TALLY,row} from './shared';

export const TALLY_COLS=[T('file','File'),I('vouchers','Vouchers'),I('ledgers','Ledgers'),T('notes','Notes')];
export const tallyXml=registerReport({
  id:'rpt.acc.tally-xml',title:'Tally export (masters and vouchers)',area:'acc',priority:'P1',status:'ACC-3',
  moneyFlag:false,viewPerms:[ACC_TALLY],exportPerm:ACC_TALLY,dateClass:'D-FY',
  params:[{key:'fy',label:'Masters FY',type:'fy',dateClass:'D-FY'},{key:'vFrom',label:'Vouchers from',type:'date'},{key:'vTo',label:'Vouchers to',type:'date'}],
  columns:TALLY_COLS,
  dateBasis:'Masters: the chart and every party ledger with the opening balance at the FY start. Vouchers: the range (at most 31 days). A simulated export — nothing is sent to Tally.',
  formats:['screen','xml'],phone:'yes',
  select:(s,_uid,p)=>{
    const vTo=(p.vTo as string|undefined)??s.date,vFrom=(p.vFrom as string|undefined)??addDays(vTo,-6);
    const m=accTallyMasters(s,p.fy as string);if(m.error)throw new HccError('VALIDATION',m.error);
    const v=accTallyVouchers(s,vFrom,vTo);if(v.error)throw new HccError('VALIDATION',v.error);
    const yr=accYearProvisional(s,vFrom)||accYearProvisional(s,vTo)||accYearProvisional(s,m.from);
    const note=(prov:boolean,extra?:string)=>[prov||yr?'PROVISIONAL':'',yr?ACC_PROVISIONAL_TEXT:'',extra??''].filter(Boolean).join(' · ');
    const rows=[row({file:'masters.xml',vouchers:null,ledgers:m.ledgers,notes:note(m.provisional)}),
      row({file:'vouchers.xml',vouchers:v.count,ledgers:null,notes:note(v.provisional.length>0,v.reexport.length?`re-export ${v.reexport.length} date(s)`:'')})];
    return {params:p,columns:TALLY_COLS,rows,about:{provisional:m.provisional||v.provisional.length>0||yr,from:vFrom,to:vTo}};
  },
});
