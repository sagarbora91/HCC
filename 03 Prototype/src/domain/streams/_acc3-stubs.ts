// ACC-3 lane acc3-exports · LOCAL STUBS of the report shapes of 02 Planning/agents/ACC3-REPORTS-CONTRACT.md §2 and §3, copied exactly.
// The report lanes (acc3-pl: accounting-pl.ts, acc3-ageing: accounting-ageing.ts / accounting-gst-summary.ts) had not landed when this
// lane was built, so accounting-exports.ts types its builders against these. THE INTEGRATOR DELETES THIS FILE and points the imports of
// accounting-exports.ts at the real modules (type-only imports; the names are identical). Where the contract gives a field list without
// types (cash ratio, GST summary) the types below are the plain reading of the contract text and are flagged "(read from the text)".
export type ACCPlLine = {code:string; name:string; paise:number; group:string};
export type ACCPlGroup = {name:string; lines:ACCPlLine[]; total:number};
export type ACCPl = {
  from:string; to:string; provisional:boolean;
  revenue:{lines:ACCPlLine[]; total:number};
  otherIncome:{lines:ACCPlLine[]; total:number};
  expenses:{consumption:ACCPlGroup; employee:ACCPlGroup; finance:ACCPlGroup; depreciation:ACCPlGroup; other:ACCPlGroup; total:number};
  profitBeforeTax:number;
  byTallyGroup:{group:string; name:string; paise:number}[];
};
export type ACCBsBlock = {name:string; lines:{code:string; name:string; paise:number}[]; total:number};
export type ACCBalanceSheet = {asOf:string; provisional:boolean;
  liabilities:{ownersFunds:ACCBsBlock; nonCurrent:ACCBsBlock; current:ACCBsBlock; total:number};
  assets:{nonCurrent:ACCBsBlock; current:ACCBsBlock; total:number};
  balanced:boolean; suspense:number; openingDifference:number};
/** (read from the text) §2 "Cash-ratio report": `{fy, cashReceipts, totalReceipts, cashPayments, totalPayments, receiptsPct, paymentsPct, flagged}`. */
export type ACCCashRatio = {fy:string; cashReceipts:number; totalReceipts:number; cashPayments:number; totalPayments:number; receiptsPct:number; paymentsPct:number; flagged:boolean};
export type ACCAgeingBill = {ref:string; date:string; due:string; amount:number; paid:number; open:number; days:number};
export type ACCAgeingRow = {account:string; party:string; name:string; control:string;
  bills:ACCAgeingBill[]; open:number; b0_30:number; b31_60:number; b61_90:number; b90:number};
export type ACCAgeing = {asOf:string; side:'receivable'|'payable'; rows:ACCAgeingRow[]; totals:{open:number; b0_30:number; b31_60:number; b61_90:number; b90:number}};
/** (read from the text) §3 "GST summary"; `setoff` is ACC-2's accGstSetoff result, exported as a flat name → paise map of its numeric fields. */
export type ACCGstSummary = {month:string;
  output:{rate5:number; rate18:number; b2b:number; b2cs:number; advances11A:number; advances11B:number; creditNotes:number};
  itc:{cgst:number; sgst:number; igst:number}; rcm:number; setoff:Record<string,number>; cashPaid:number; tcsCredit:number; cashLedgerBalance:number;
  ties:{gstr1:boolean; gstr3b:boolean; differences:string[]}};
