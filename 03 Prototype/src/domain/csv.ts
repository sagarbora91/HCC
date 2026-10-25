// Shared CSV cell helpers (W05 review F15). Text that Excel would run as a formula (= + - @, tab, CR at the start) gets a leading apostrophe,
// so a vendor or guest typed as =HYPERLINK(...) stays text. Plain numbers such as -5.00 or 1,234.50 are left alone.
const NUMERIC=/^[+-]?\d[\d,]*(\.\d+)?$/;
export const csvSafe=(v:string|number)=>{const t=String(v);return typeof v==='string'&&/^[=+\-@\t\r]/.test(t)&&!NUMERIC.test(t)?"'"+t:t;};
/** Always quoted (cash book, reports). */
export const csvQuoted=(v:string|number)=>'"'+csvSafe(v).split('"').join('""')+'"';
/** Quoted only when needed (generic voucher import format). */
export const csvMinimal=(v:string|number)=>{const t=csvSafe(v);return /[",\r\n]/.test(t)?'"'+t.split('"').join('""')+'"':t;};
