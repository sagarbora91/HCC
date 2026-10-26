// V1 probe (A03a-04): re-implements the CreditNoteRules.Plan tax guard (CreditNoteRules.cs:76-80) with MoneyMath.TaxOf = half away from zero.
const taxOf = (p, bps) => { const n = p * bps, q = Math.trunc(n / 10000), r = n % 10000; return Math.abs(r) * 2 >= 10000 ? q + Math.sign(n) : q; };
const bps = 500, T = 100000, lineTax = taxOf(T, bps);           // ₹1,000 room line at 5 % -> ₹50.00 tax
let credited = 0;
const credit = (a, label) => { const t = taxOf(a, bps); const ok = Math.abs(credited + t) <= Math.abs(lineTax);
  console.log(`${label}: taxable ${a} tax ${t} -> cumulative ${credited + t} vs charged ${lineTax}: ${ok ? 'OK' : 'REFUSED'}`); if (ok) credited += t; return ok; };
credit(33330, 'partial credit #1 (amount mode share ending in 10 paise)');
credit(T - 33330, 'full mode on the rest (what the refusal text suggests)');
credit(66600, 'whole-rupee amount (other suggestion)');
credit(70, 'then the remaining 70 paise');
console.log('fraction of paise amounts whose 5 % tax is exactly x.5 paise:', [...Array(2000).keys()].filter(a => (a * bps) % 10000 === 5000).length / 2000);
