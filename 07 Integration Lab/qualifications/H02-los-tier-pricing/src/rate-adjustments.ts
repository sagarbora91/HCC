/*
 * HCC rate adjustments — length-of-stay tiers and occupancy bands.
 *
 * Derived from HAIP, file apps/api/src/modules/rate-plan/rate-pricing.util.ts
 *   repository https://github.com/TelivityAI/haip.git, commit 3a2f1c89139e8d7d915528abac706a512af16970
 *   source SHA-256 3dcac4f4d29e800654c8f6f84fb6ac0d78670e15c13fcda7534dbcd18fac7c57
 * Licensed under the Apache License, Version 2.0 — see ./LICENSE-HAIP-Apache-2.0.txt (copy of the HAIP root LICENSE).
 *
 * Modified by HCC, 24 Oct 2026 (Apache-2.0 §4(b) notice of changes):
 *  - money is integer paise; percentages are integer basis points (HCC DEC-023, yield module convention);
 *  - tiers and bands are validated (positive integer nights, unique tiers, half-open non-overlapping bands);
 *  - occupancy bands are half-open [fromPct, toPct) so 50 % can never match two bands; a band ending at 100 includes 100;
 *  - LOS and occupancy bps are summed and rounded ONCE (half away from zero, symmetric for negatives), then fixed amounts are added;
 *  - a negative nightly result is an error, never silently clamped;
 *  - donor nightsBetween() dropped (HCC uses domain/money.ts nights() with validated dates).
 * Not activated in the prototype. Lab qualification only.
 */

export type LosTier = { minNights: number; adjBps: number };
export type OccupancyBand = { fromPct: number; toPct: number; adjBps: number };
export type FixedAdjustment = { paise: number };

export class RateRuleError extends Error {
  constructor(public code: 'INVALID_RULE' | 'INVALID_INPUT' | 'NEGATIVE_RATE', message: string) { super(message); this.name = 'RateRuleError'; }
}

const isInt = (n: unknown): n is number => typeof n === 'number' && Number.isSafeInteger(n);
const MAX_BPS = 10000; // ±100 %

export function validateLosTiers(tiers: LosTier[]): void {
  const seen = new Set<number>();
  for (const t of tiers) {
    if (!isInt(t.minNights) || t.minNights < 1) throw new RateRuleError('INVALID_RULE', `LOS tier minNights must be a positive whole number (got ${t.minNights}).`);
    if (!isInt(t.adjBps) || Math.abs(t.adjBps) > MAX_BPS) throw new RateRuleError('INVALID_RULE', `LOS tier adjustment must be whole basis points within ±${MAX_BPS}.`);
    if (seen.has(t.minNights)) throw new RateRuleError('INVALID_RULE', `Two LOS tiers start at ${t.minNights} nights.`);
    seen.add(t.minNights);
  }
}

/** Highest qualifying tier (21+ beats 14+ beats 7+); null when none applies. */
export function selectLosTier(tiers: LosTier[], nights: number): LosTier | null {
  if (!isInt(nights) || nights < 1) throw new RateRuleError('INVALID_INPUT', 'Nights must be a positive whole number.');
  validateLosTiers(tiers);
  return tiers.filter(t => nights >= t.minNights).sort((a, b) => b.minNights - a.minNights)[0] ?? null;
}

export function validateOccupancyBands(bands: OccupancyBand[]): void {
  const sorted = [...bands].sort((a, b) => a.fromPct - b.fromPct);
  for (const b of sorted) {
    if (!isInt(b.fromPct) || !isInt(b.toPct) || b.fromPct < 0 || b.toPct > 100 || b.fromPct >= b.toPct)
      throw new RateRuleError('INVALID_RULE', `Occupancy band ${b.fromPct}–${b.toPct} % is invalid (whole percent, 0 ≤ from < to ≤ 100).`);
    if (!isInt(b.adjBps) || Math.abs(b.adjBps) > MAX_BPS) throw new RateRuleError('INVALID_RULE', 'Band adjustment must be whole basis points within ±10000.');
  }
  for (let i = 1; i < sorted.length; i++)
    if (sorted[i].fromPct < sorted[i - 1].toPct) throw new RateRuleError('INVALID_RULE', `Occupancy bands ${sorted[i - 1].fromPct}–${sorted[i - 1].toPct} and ${sorted[i].fromPct}–${sorted[i].toPct} overlap.`);
}

/** Band containing pct: fromPct ≤ pct < toPct, or pct = 100 for a band ending at 100. */
export function selectOccupancyBand(bands: OccupancyBand[], pct: number): OccupancyBand | null {
  if (!isInt(pct) || pct < 0 || pct > 100) throw new RateRuleError('INVALID_INPUT', 'Occupancy must be a whole percent from 0 to 100.');
  validateOccupancyBands(bands);
  return bands.find(b => (pct >= b.fromPct && pct < b.toPct) || (pct === 100 && b.toPct === 100)) ?? null;
}

/** base × (10000 + bps) / 10000, rounded half away from zero (symmetric for reversals). */
export function applyBps(basePaise: number, bps: number): number {
  if (!isInt(basePaise) || basePaise < 0) throw new RateRuleError('INVALID_INPUT', 'Base rate must be non-negative integer paise.');
  if (!isInt(bps)) throw new RateRuleError('INVALID_INPUT', 'Adjustment must be whole basis points.');
  const num = basePaise * (10000 + bps);
  if (!Number.isSafeInteger(num)) throw new RateRuleError('INVALID_INPUT', 'Rate too large.');
  const sign = num < 0 ? -1 : 1, abs = Math.abs(num);
  let q = Math.floor(abs / 10000);
  if ((abs % 10000) * 2 >= 10000) q++;
  return sign * q;
}

/** One night: (LOS bps + occupancy bps) applied once, then fixed amounts. Throws NEGATIVE_RATE rather than clamping. */
export function priceNight(basePaise: number, opts: { los?: LosTier | null; band?: OccupancyBand | null; fixed?: FixedAdjustment[] } = {}): number {
  const bps = (opts.los?.adjBps ?? 0) + (opts.band?.adjBps ?? 0);
  let rate = applyBps(basePaise, bps);
  for (const f of opts.fixed ?? []) {
    if (!isInt(f.paise)) throw new RateRuleError('INVALID_INPUT', 'Fixed adjustments must be integer paise.');
    rate += f.paise;
  }
  if (rate < 0) throw new RateRuleError('NEGATIVE_RATE', 'Adjustments would make the nightly rate negative.');
  return rate;
}
