// ACC-2 · acc2-ui — cash-guard messages (contract §7): a refusal (CASH_LIMIT, cash from one party ≥ ₹2,00,000 a day) and a warning (cash above ₹10,000
// to one person: allowed, the record carries cashDeductionLost). The forms show the verdict before sending; the command decides (a refusal arrives as the
// command's own error toast, a warning as a toast after saving); the expense card carries the badge. Wording comes from the guard, never repeated here.
import React from 'react';
import {AlertTriangle,Ban} from 'lucide-react';
import type {ACCCashGuardVerdict} from './acc2-views';

/** Inline note under a form: the guard's refusal (blocks) or warning (allows). Nothing when the guard says nothing. */
export function CashGuardNote({verdict}:{verdict?:ACCCashGuardVerdict}){
  if(!verdict||(verdict.ok&&!verdict.warning))return null;
  if(!verdict.ok)return <div className="info-note acc-guard acc-guard-refuse" role="alert"><Ban size={16} aria-hidden/><span><strong>Cash refused.</strong> {verdict.refusal}</span></div>;
  return <div className="info-note f-warn acc-guard acc-guard-warn" role="status"><AlertTriangle size={16} aria-hidden/><span><strong>Allowed, with a warning.</strong> {verdict.warning}</span></div>;
}
/** The toast to show after a saved cash record: the guard's warning (undefined = nothing to say). */
export const cashGuardToast=(v?:ACCCashGuardVerdict)=>v?.ok&&v.warning?v.warning:undefined;
/** Badge on an expense card / row whose cash payment lost its income-tax deduction (cashDeductionLost, B10). */
export function CashDeductionBadge({lost}:{lost?:boolean}){
  if(!lost)return null;
  return <span className="pill orange acc-cash-lost" title="Cash above the limit to one person in a day: the deduction is lost for income tax (VERIFY the section number).">Cash deduction lost</span>;
}
