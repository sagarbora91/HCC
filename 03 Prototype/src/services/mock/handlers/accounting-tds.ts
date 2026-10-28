// ACC-2 · lane acc2-tds — command handlers (contract §4, §5, §9; rulebook C9, E5; DEC-146, DEC-058). Audit text = the return value.
//   ACC_TDS_SETTINGS {hotelDeductsTds?, tdsDeductorType?}   Owner only (money setting, DEC-058): the two firm-profile switches of contract §4.
//                                                            (CMP_PROFILE_SET is the compliance module's command; this one changes only the switches.)
//   ACC_TDS_REMIT {month, section, amount, method, challan, date?}   Owner / Accountant (PERM-ACC-013; prototype: 'reports' + role): TDS deposited
//                                                            (E5) as the manual template 'tds-remittance' — Dr 2150 TDS payable · Cr 1030/[bank];
//                                                            an Owner's entry is approved at once, an Accountant's waits for the Owner (contract §2).
// The rates themselves are dated Master configuration settings (SETTING_SET on tds.*; registered in domain/streams/accounting-tds.ts).
// Production: clients/sync/accounting-tds.ts.
import type {Handler,Kit} from '../commands';
import {money} from '../../../domain/money';
import {accSlice,accMapping,type ACCState,type ACCManualVoucher,type ACCManualLine,type ACCManualTemplate} from '../../../domain/streams/accounting';
import {accLedger} from '../../../domain/streams/accounting-chart';
import {accPost} from '../../../domain/streams/accounting-parties';
import {cmpSlice} from '../../../domain/streams/compliance';
import {tdsSettings,tdsBaseSection,tdsSectionDef,tdsPayableOutstanding,tdsRemitNarration,ACC_TDS_PAYABLE_KEY,TDS_LEDGER} from '../../../domain/streams/accounting-tds';

const MONTH=/^\d{4}-(0[1-9]|1[0-2])$/,DATE=/^\d{4}-\d{2}-\d{2}$/;
const TAX_ROLES=['Owner','Accountant'];
const owner=(k:Kit)=>{if(k.user.role!=='Owner')k.fail('FORBIDDEN','Access denied: only the Owner changes the TDS settings (money settings, DEC-058).');};
const tax=(k:Kit)=>{k.req('reports');if(!TAX_ROLES.includes(k.user.role))k.fail('FORBIDDEN','Access denied: TDS entries are for the Owner and the Accountant (PERM-ACC-013).');};

/**
 * LOCAL STUB of the manual lane's `accManualSave` (acc2-manual, contract §2) — the integrator replaces it with the manual lane's export.
 * Stores the voucher in s.accountingX.manual: the Owner's own is approved at once, an Accountant's is pending. The manual lane's guards
 * (balanced, active ledgers, locked day, cash guards) and its posting section of accLiveVouchers come with that merge.
 */
function accManualSave(k:Kit,v:{date:string;type:ACCManualVoucher['type'];narration:string;reference?:string;lines:ACCManualLine[];template:ACCManualTemplate}):ACCManualVoucher{
  const x:ACCState={...accSlice(k.s)};k.s.accountingX=x;
  const dr=v.lines.reduce((a,l)=>a+l.debit,0),cr=v.lines.reduce((a,l)=>a+l.credit,0);
  k.assert(dr===cr&&dr>0,'The entry does not balance.');
  const approved=k.user.role==='Owner';
  const mv:ACCManualVoucher={id:k.id('MV'),date:v.date,type:v.type,narration:v.narration,...(v.reference?{reference:v.reference}:{}),lines:v.lines,template:v.template,
    status:approved?'approved':'pending',by:k.uid,at:k.s.clock,...(approved?{decidedBy:k.uid,decidedAt:k.s.clock}:{}),bookedOn:v.date};
  x.manual=[...(x.manual??[]),mv];
  return mv;
}

export const handlers:Record<string,Handler>={
  ACC_TDS_SETTINGS:k=>{
    const {s,cmd,assert,uid}=k;owner(k);
    const x=cmpSlice(s);assert(x.profile,'Set the compliance firm profile first (Owner: Compliance calendar › Firm profile).','INVALID_TRANSITION');
    const has=(f:string)=>cmd[f]!==undefined&&cmd[f]!==null;
    assert(has('hotelDeductsTds')||has('tdsDeductorType'),'Choose “hotel deducts TDS” or the rate column.');
    if(has('hotelDeductsTds'))assert(typeof cmd.hotelDeductsTds==='boolean','“Hotel deducts TDS” is on or off.');
    if(has('tdsDeductorType'))assert(cmd.tdsDeductorType==='individual'||cmd.tdsDeductorType==='other','Choose the rate column: individual / HUF or other.');
    const before=tdsSettings(s);
    const profile={...x.profile!,...(has('hotelDeductsTds')?{hotelDeductsTds:cmd.hotelDeductsTds as boolean}:{}),
      ...(has('tdsDeductorType')?{tdsDeductorType:cmd.tdsDeductorType as 'individual'|'other'}:{}),updatedBy:uid,updatedAt:s.clock};
    s.complianceX={...x,profile};
    const after=tdsSettings(s);
    return `TDS settings · hotel deducts TDS ${before.hotelDeductsTds?'on':'off'} → ${after.hotelDeductsTds?'on':'off'} · rate column ${before.tdsDeductorType} → ${after.tdsDeductorType} (DEC-146; CA to confirm)`;
  },
  ACC_TDS_REMIT:k=>{
    const {s,cmd,assert,positive}=k;tax(k);
    const month=String(cmd.month??'');assert(MONTH.test(month),'Choose the month the TDS was deducted in (YYYY-MM).');
    assert(month<=s.date.slice(0,7),'The month cannot be after the business date.');
    const section=tdsBaseSection(String(cmd.section??''));assert(section&&!String(cmd.section).includes(':'),'Choose the TDS section (rent, contractor, professional or commission).');
    positive(cmd.amount);const amount=cmd.amount as number;
    const challan=String(cmd.challan??'').trim();assert(challan.length>=3&&challan.length<=40,'Enter the challan number (3–40 characters).');
    const date=String(cmd.date??s.date);assert(DATE.test(date),'Enter the payment date.');
    assert(date<=s.date,'The payment date cannot be after the business date.');assert(date>=month+'-01','The payment date cannot be before the month it pays for.');
    const method=String(cmd.method??'Bank transfer'),map=accMapping(s),code=map['method:'+method];
    assert(code&&accLedger(s,code)?.group==='bank',`TDS is paid from a bank account: choose a bank method (${method} does not settle to a bank).`);
    const out=tdsPayableOutstanding(s,section!,month);
    assert(out>0,`No TDS is held back for ${tdsSectionDef(section!)!.label.toLowerCase()} in ${month}.`,'INVALID_TRANSITION');
    assert(amount<=out,`The remittance cannot exceed the TDS held back for ${tdsSectionDef(section!)!.label.toLowerCase()} in ${month} and not yet remitted (${money(out)}). Interest or a late fee is a separate entry on 5400.`);
    const payable=map[ACC_TDS_PAYABLE_KEY]??TDS_LEDGER.payable,bank=accPost(s,code!,{method});
    const v=accManualSave(k,{date,type:'Payment',narration:tdsRemitNarration(section!,month,challan),reference:challan,template:'tds-remittance',
      lines:[{account:payable,debit:amount,credit:0},{account:bank,debit:0,credit:amount}]});
    return `${v.id} · TDS remittance · ${tdsSectionDef(section!)!.label} · ${month} · ${money(amount)} by ${method} · challan ${challan} · Dr ${payable} · Cr ${bank} · ${v.status==='approved'?'approved':'waiting for the Owner'}`;
  },
};
