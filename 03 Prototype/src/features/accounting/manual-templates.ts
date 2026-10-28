// ACC-2 · acc2-ui — the guided templates of contract §2: each one is a short form whose answers fill the voucher lines (the UI lane fills the
// lines; the guards and the posting are the manual lane's). Pure display wiring: a template says which fields it asks and which lines they make.
import type {ACCManualLine,ACCManualTemplate,ACCManualVoucher} from './_contract-stubs-2';

export type TplFieldKind='amount'|'bank'|'tender'|'loan'|'debtor';
export type TplField={key:string;label:string;kind:TplFieldKind;help?:string};
export type TplAnswers=Record<string,string>;
export type Tpl={id:ACCManualTemplate;label:string;type:ACCManualVoucher['type'];blurb:string;fields:TplField[];
  /** Bad debt is Owner-only (A18). */ ownerOnly?:boolean;
  /** Builds the lines from the answers (paise amounts already parsed); [] while the answers are incomplete. */
  build:(a:TplAnswers,n:(k:string)=>number)=>ACCManualLine[]};
const L=(account:string,debit:number,credit:number,note?:string):ACCManualLine=>({account,debit,credit,...(note?{note}:{})});

export const MANUAL_TEMPLATES:Tpl[]=[
  {id:'journal',label:'Journal / free entry',type:'Journal',blurb:'Any balanced entry. Choose the voucher type and add the lines yourself.',fields:[],build:()=>[]},
  {id:'contra',label:'Contra (cash ↔ bank)',type:'Contra',blurb:'Move money between cash and bank ledgers: deposit, withdrawal, transfer between accounts.',
    fields:[{key:'from',label:'From (money leaves)',kind:'tender'},{key:'to',label:'To (money arrives)',kind:'tender'},{key:'amount',label:'Amount (₹)',kind:'amount'}],
    build:(a,n)=>a.from&&a.to&&n('amount')>0?[L(a.to,n('amount'),0),L(a.from,0,n('amount'))]:[]},
  {id:'capital-introduced',label:'Capital introduced',type:'Receipt',blurb:'The Owner puts money into the business: Dr bank or cash · Cr Owner’s capital (3100).',
    fields:[{key:'tender',label:'Received in',kind:'tender'},{key:'amount',label:'Amount (₹)',kind:'amount'}],
    build:(a,n)=>a.tender&&n('amount')>0?[L(a.tender,n('amount'),0),L('3100',0,n('amount'))]:[]},
  {id:'owner-drawing-bank',label:'Owner drawing from the bank',type:'Payment',blurb:'The Owner takes money out of the bank: Dr Owner’s drawings (3000) · Cr bank.',
    fields:[{key:'bank',label:'Taken from bank account',kind:'bank'},{key:'amount',label:'Amount (₹)',kind:'amount'}],
    build:(a,n)=>a.bank&&n('amount')>0?[L('3000',n('amount'),0),L(a.bank,0,n('amount'))]:[]},
  {id:'loan-received',label:'Loan received',type:'Receipt',blurb:'A loan is paid into the bank: Dr bank · Cr the loan (2400).',
    fields:[{key:'bank',label:'Received in bank account',kind:'bank'},{key:'loan',label:'Loan',kind:'loan'},{key:'amount',label:'Amount (₹)',kind:'amount'}],
    build:(a,n)=>a.bank&&a.loan&&n('amount')>0?[L(a.bank,n('amount'),0),L(a.loan,0,n('amount'))]:[]},
  {id:'loan-emi',label:'Loan EMI',type:'Payment',blurb:'An instalment paid from the bank: Dr the loan (principal) · Dr Interest on loans (5090) · Cr bank.',
    fields:[{key:'bank',label:'Paid from bank account',kind:'bank'},{key:'loan',label:'Loan',kind:'loan'},{key:'principal',label:'Principal (₹)',kind:'amount'},{key:'interest',label:'Interest (₹)',kind:'amount'}],
    build:(a,n)=>a.bank&&a.loan&&n('principal')+n('interest')>0?[...(n('principal')?[L(a.loan,n('principal'),0)]:[]),...(n('interest')?[L('5090',n('interest'),0)]:[]),L(a.bank,0,n('principal')+n('interest'))]:[]},
  {id:'bank-charges',label:'Bank charges',type:'Payment',blurb:'Charges the bank debited: Dr Bank & card charges (5080) · Cr bank.',
    fields:[{key:'bank',label:'Debited from bank account',kind:'bank'},{key:'amount',label:'Amount (₹)',kind:'amount'}],
    build:(a,n)=>a.bank&&n('amount')>0?[L('5080',n('amount'),0),L(a.bank,0,n('amount'))]:[]},
  {id:'interest-income',label:'Interest income',type:'Receipt',blurb:'Interest the bank credited: Dr bank · Cr Interest & other income (4960).',
    fields:[{key:'bank',label:'Credited to bank account',kind:'bank'},{key:'amount',label:'Amount (₹)',kind:'amount'}],
    build:(a,n)=>a.bank&&n('amount')>0?[L(a.bank,n('amount'),0),L('4960',0,n('amount'))]:[]},
  {id:'bad-debt',label:'Bad debt written off',type:'Journal',ownerOnly:true,blurb:'Owner only. A receivable that will not be paid: Dr Bad debts (5300) · Cr the company (1110) or customer (1120).',
    fields:[{key:'debtor',label:'Company or customer',kind:'debtor'},{key:'amount',label:'Amount (₹)',kind:'amount'}],
    build:(a,n)=>a.debtor&&n('amount')>0?[L('5300',n('amount'),0),L(a.debtor,0,n('amount'))]:[]},
];
/** Templates whose own tab holds the form (statutory remittance / GST payment: GST month tab; TDS remittance: TDS tab). */
export const REMITTANCE_TEMPLATES:ACCManualTemplate[]=['statutory-remittance','gst-payment','tds-remittance'];
export const TEMPLATE_LABEL:Record<ACCManualTemplate,string>={journal:'Journal','contra':'Contra','capital-introduced':'Capital introduced','owner-drawing-bank':'Owner drawing (bank)','loan-received':'Loan received',
  'loan-emi':'Loan EMI','bank-charges':'Bank charges','interest-income':'Interest income','bad-debt':'Bad debt','statutory-remittance':'Statutory remittance','gst-payment':'GST payment','tds-remittance':'TDS remittance'};
export const tplOf=(id?:string)=>MANUAL_TEMPLATES.find(t=>t.id===id);
