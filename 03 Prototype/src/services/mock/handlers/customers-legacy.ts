// LG-UI · the prototype's answer to the legacy part of `GET /customers/profile`: the six fictional stays of src/fixtures/ext/legacy-sample.ts.
// Read only: no command, no State slice, nothing persisted. The production client (clients/sync/customers-legacy.ts) maps the server's
// `LegacyStays` into the same LegacyStay shape; Customers 360 asks `legacyLookup()` and cannot tell the two apart.
import {LEGACY_SAMPLE} from '../../../fixtures/ext/legacy-sample';
import type {LegacyStay} from '../../../domain/streams/customers';

/** Legacy stays of one Customer 360 key (last 10 digits of the mobile number), newest arrival first. */
export function legacyProfileRead(key:string):LegacyStay[]{
  return [...(LEGACY_SAMPLE[key]??[])].sort((a,b)=>b.arrival.localeCompare(a.arrival)||b.bookingId-a.bookingId);
}
