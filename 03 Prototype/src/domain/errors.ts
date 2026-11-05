// Typed errors returned by the mock service (Prototype Spec §6).
export type ErrorCode='FORBIDDEN'|'NOT_FOUND'|'NOT_AVAILABLE'|'INVALID_TRANSITION'|'VALIDATION'|'STALE_VERSION'|'DAY_CLOSED'|'SIMULATED_REJECTION'|'RESULT_UNKNOWN'|'RESTRICTED'
  |'NET_OFFLINE' // F5-E1: the internet egress gate refused (EGRESS-CONTRACT §1)
  // F6-H1 (F6-CONTRACT §3): hall booking
  |'HCC_HALL_CLASH'|'HCC_TNC_PLACEHOLDER'|'HCC_QUO_NOT_LATEST'|'HCC_QUO_EXPIRED'|'HCC_DISCOUNT_LIMIT'|'HCC_HALL_CAPACITY'|'HCC_TNC_OWNER_ONLY'
  // F6-H1 end
  // F12 MEAS (MEAS-CONTRACT §1.4): a switched-off measurement item / pack, and a meter reading below the last one
  |'MEAS_OFF'|'MEAS_PACK_OFF'|'MEAS_READING_NOT_MONOTONIC'
  // F12 end
  ;
export class HccError extends Error{
  constructor(public readonly code:ErrorCode,message:string){super(message);this.name='HccError';}
}
export const errorTitle:Record<ErrorCode,string>={
  FORBIDDEN:'Not permitted',NOT_FOUND:'Record not found',NOT_AVAILABLE:'Not available',INVALID_TRANSITION:'Action not allowed now',
  VALIDATION:'Check the details',STALE_VERSION:'Record changed',DAY_CLOSED:'Business day closed',
  SIMULATED_REJECTION:'Simulated rejection',RESULT_UNKNOWN:'Result unknown',RESTRICTED:'Stay restriction',
  NET_OFFLINE:'Internet is off', // F5-E1
  // F6-H1
  HCC_HALL_CLASH:'Hall already booked',HCC_TNC_PLACEHOLDER:'T&C not final',HCC_QUO_NOT_LATEST:'Not the latest version',HCC_QUO_EXPIRED:'Quotation expired',
  HCC_DISCOUNT_LIMIT:'Discount needs approval',HCC_HALL_CAPACITY:'Above hall capacity',HCC_TNC_OWNER_ONLY:'Owner only',
  // F6-H1 end
  MEAS_OFF:'Measurement switched off',MEAS_PACK_OFF:'Pack switched off',MEAS_READING_NOT_MONOTONIC:'Reading below the last one', // F12
};
export function codeOf(e:unknown):ErrorCode{return e instanceof HccError?e.code:'VALIDATION';}
