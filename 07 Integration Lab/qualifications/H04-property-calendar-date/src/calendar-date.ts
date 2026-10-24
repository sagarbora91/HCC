/*
 * HCC calendar-date conversion for an instant in the property's IANA timezone.
 *
 * Derived from HAIP, file apps/api/src/common/date/property-business-date.ts (calendarDateInTimeZone)
 *   repository https://github.com/TelivityAI/haip.git, commit 3a2f1c89139e8d7d915528abac706a512af16970
 * Licensed under the Apache License, Version 2.0 — see ./LICENSE-HAIP-Apache-2.0.txt (copy of the HAIP root LICENSE).
 *
 * Modified by HCC, 24 Oct 2026 (Apache-2.0 §4(b) notice of changes):
 *  - an invalid timezone is a configuration error (InvalidTimeZoneError), never a silent UTC fallback;
 *  - an invalid Date instant is rejected;
 *  - assertTimeZone() added for validation when property configuration is saved;
 *  - scope note: returns a CALENDAR date only. The HCC business date is persisted state advanced by day close (DEC-042),
 *    never derived from this function or from midnight.
 * Not activated in the prototype (the prototype uses a simulated clock string). Intended for the production clock adapter (A4).
 */

export class InvalidTimeZoneError extends Error {
  constructor(public timeZone: string) { super(`Invalid property timezone: "${timeZone}". Configure a valid IANA zone such as Asia/Kolkata.`); this.name = 'InvalidTimeZoneError'; }
}

export function assertTimeZone(timeZone: string): void {
  if (typeof timeZone !== 'string' || !timeZone.trim()) throw new InvalidTimeZoneError(String(timeZone));
  try { new Intl.DateTimeFormat('en-US', { timeZone }); } catch { throw new InvalidTimeZoneError(timeZone); }
}

/** YYYY-MM-DD of `instant` as seen in `timeZone`. */
export function calendarDateInTimeZone(instant: Date, timeZone: string): string {
  if (!(instant instanceof Date) || Number.isNaN(instant.getTime())) throw new RangeError('Invalid instant.');
  assertTimeZone(timeZone);
  const parts = new Intl.DateTimeFormat('en-US', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(instant);
  const v = Object.fromEntries(parts.map(p => [p.type, p.value]));
  return `${v.year}-${v.month}-${v.day}`;
}
