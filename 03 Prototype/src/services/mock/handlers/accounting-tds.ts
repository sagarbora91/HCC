// ACC-2 · lane acc2-tds — command handlers (contract §4, §5, §9; rulebook C9, E5; DEC-146, DEC-058). Audit text = the return value.
//   ACC_TDS_SETTINGS {hotelDeductsTds?, tdsDeductorType?}   Owner only (money setting, DEC-058): the two firm-profile switches of contract §4.
//                                                            (CMP_PROFILE_SET is the compliance module's command; this one changes only the switches.)
//   ACC_TDS_REMIT {month, section, amount, method, challan, date?}   Owner / Accountant (PERM-ACC-013; prototype: 'reports' + role): TDS deposited
//                                                            (E5) as the manual template 'tds-remittance' — Dr 2150 TDS payable · Cr 1030/[bank];
//                                                            an Owner's entry is approved at once, an Accountant's waits for the Owner (contract §2).
// The rates themselves are dated Master configuration settings (SETTING_SET on tds.*; registered in domain/streams/accounting-tds.ts).
// Production: clients/sync/accounting-tds.ts.
import type {Handler,Kit} from '../commands';
import {accManualSave as accManualSaveApi} from './accounting-manual'; // ACC-2 integration (contract §2)
import {accMarkComplianceCard} from './accounting-gst'; // ACC-2 integration (ruling 9): the remittance marks the month's TDS card
import {money} from '../../../domain/money';
import {accSlice,accMapping,type ACCState,type ACCManualVoucher,type ACCManualLine,type ACCManualTemplate} from '../../../domain/streams/accounting';
import {accLedger} from '../../../domain/streams/accounting-chart';
import {accPost} from '../../../domain/streams/accounting-parties';
import {cmpSlice} from '../../../domain/streams/compliance';
import {tdsSettings,tdsBaseSection,tdsPayableOutstanding,tdsRemitNarration,tdsCodeLabel,tdsOldOf,tdsForm168Refusal,tdsRemittances,ACC_TDS_PAYABLE_KEY,TDS_LEDGER} from '../../../domain/streams/accounting-tds';

const MONTH=/^\d{4}-(0[1-9]|1[0-2])$/,DATE=/^\d{4}-\d{2}-\d{2}$/;
const TAX_ROLES=['Owner','Accountant'];
const owner=(k:Kit)=>{if(k.user.role!=='Owner')k.fail('FORBIDDEN','Access denied: only the Owner changes the TDS settings (money settings, DEC-058).');};
const tax=(k:Kit)=>{k.req('reports');if(!TAX_ROLES.includes(k.user.role))k.fail('FORBIDDEN','Access denied: TDS entries are for the Owner and the Accountant (PERM-ACC-013).');};

/** ACC-2 integration: the manual lane's voucher API (contract §2) replaces the lane's local stub — every §2 guard, Owner approved / Accountant pending. */
const accManualSave=(k:Kit,v:{date:string;type:ACCManualVoucher["type"];narration:string;reference?:string;lines:ACCManualLine[];template:ACCManualTemplate}):ACCManualVoucher=>accManualSaveApi(k,v).voucher;

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
    // ACC-3 C6: a sub-section code (rent:plant = 194-I(a), professional:technical = 194J(a)) is remitted on its own.
    const code=String(cmd.section??'').trim(),section=tdsBaseSection(code);assert(section,'Choose the TDS section (rent, contractor, professional or commission).');
    const what=code.includes(':')?`${tdsCodeLabel(code).toLowerCase()} (old ${tdsOldOf(code)})`:tdsCodeLabel(code).toLowerCase();
    positive(cmd.amount);const amount=cmd.amount as number;
    const challan=String(cmd.challan??'').trim();assert(challan.length>=3&&challan.length<=40,'Enter the challan number (3–40 characters).');
    const date=String(cmd.date??s.date);assert(DATE.test(date),'Enter the payment date.');
    assert(date<=s.date,'The payment date cannot be after the business date.');assert(date>=month+'-01','The payment date cannot be before the month it pays for.');
    const method=String(cmd.method??'Bank transfer'),map=accMapping(s),acct=map['method:'+method];
    assert(acct&&accLedger(s,acct)?.group==='bank',`TDS is paid from a bank account: choose a bank method (${method} does not settle to a bank).`);
    const f168=cmd.form168===undefined||cmd.form168===null?undefined:cmd.form168;
    if(f168!==undefined){const why=tdsForm168Refusal(s,f168);assert(!why,why);}
    const out=tdsPayableOutstanding(s,code,month);
    assert(out>0,`No TDS is held back for ${what} in ${month}.`,'INVALID_TRANSITION');
    assert(amount<=out,`The remittance cannot exceed the TDS held back for ${what} in ${month} and not yet remitted (${money(out)}). Interest or a late fee is a separate entry on 5400.`);
    const payable=map[ACC_TDS_PAYABLE_KEY]??TDS_LEDGER.payable,bank=accPost(s,acct!,{method});
    const v=accManualSave(k,{date,type:'Payment',narration:tdsRemitNarration(code,month,challan),reference:challan,template:'tds-remittance',
      lines:[{account:payable,debit:amount,credit:0},{account:bank,debit:0,credit:amount}]});
    if(f168!==undefined){const x:ACCState={...accSlice(k.s)};x.tdsForm168={...(x.tdsForm168??{}),[v.id]:{...(f168 as Record<string,number>)}};k.s.accountingX=x;}
    // ACC-2 integration (ruling 9): as the gst lane does for PF / ESIC / PT, the month's TDS compliance card is marked filed with the challan.
    const card=accMarkComplianceCard(k,'TDS',month,date,challan,`TDS ${tdsCodeLabel(code)} (old ${tdsOldOf(code)}) ${money(amount)} remitted · ${method} · manual voucher ${v.id}`);
    return `${v.id} · TDS remittance · ${tdsCodeLabel(code)} (old ${tdsOldOf(code)}) · ${month} · ${money(amount)} by ${method} · challan ${challan} · Dr ${payable} · Cr ${bank} · ${v.status==='approved'?'approved':'waiting for the Owner'}${f168?` · Form 168 figures for ${Object.keys(f168).length} compan${Object.keys(f168).length===1?'y':'ies'}`:''}${card}`;
  },
  // ACC-3 C7: ACC_TDS_FORM168 {remittance, form168} — Owner / Accountant: the Form 168 figures (company → paise) kept on a TDS remittance; the
  // receivable reconciliation reads them (tdsForm168Of). Replaces the remittance's earlier figures.
  ACC_TDS_FORM168:k=>{
    const {s,cmd,assert}=k;tax(k);
    const id=String(cmd.remittance??''),r=tdsRemittances(s).find(x=>x.id===id);if(!r)k.fail('NOT_FOUND','TDS remittance not found.');
    assert(r!.status==='approved'||r!.status==='pending','This remittance is not live.','INVALID_TRANSITION');
    const why=tdsForm168Refusal(s,cmd.form168);assert(!why,why);
    const x:ACCState={...accSlice(s)};x.tdsForm168={...(x.tdsForm168??{}),[id]:{...(cmd.form168 as Record<string,number>)}};k.s.accountingX=x;
    return `${id} · Form 168 figures · ${Object.entries(cmd.form168 as Record<string,number>).map(([c,v])=>`${c} ${money(v)}`).join(', ')||'cleared'}`;
  },
};
