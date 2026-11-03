// Module 27 · Hall booking T&C seed (F6 H2; spec §7, contract §7 migration 4349). Every clause text is the placeholder "OWNER TO REPLACE" until the
// owner's paper T&C arrive; a quotation whose snapshot still holds a placeholder cannot be sent unless the Owner overrides with a reason.
// hallTemplates(x) (domain/streams/hall-quote.ts) returns these while s.hallX.templates is empty; the first HALL_TNC_TEMPLATE_SAVE materialises them.
import type {TncTemplate,TncClause} from '../../domain/streams/hall';

/** The ten seeded clause headings (spec §7), in print order. */
export const HALL_TNC_HEADINGS=['Advance and payment','Cancellation slabs','Guaranteed pax and extra plates','Timings and overtime','Damages',
  'Outside caterers and decorators','Music and noise limits','Liquor and licences (customer’s responsibility)','GST note','Force majeure'] as const;
const placeholderClauses=():TncClause[]=>HALL_TNC_HEADINGS.map((heading,i)=>({n:i+1,heading,text:'OWNER TO REPLACE'}));
export const HALL_TNC_EFFECTIVE='2026-04-01';
/** A fresh copy each call: General (used where an event type has no own template), Wedding and Corporate. */
export function hallTncSeed():TncTemplate[]{
  return [
    {id:'T-GEN',eventType:'GENERAL',versions:[{v:1,effectiveFrom:HALL_TNC_EFFECTIVE,clauses:placeholderClauses()}]},
    {id:'T-WEDDING',eventType:'WEDDING',versions:[{v:1,effectiveFrom:HALL_TNC_EFFECTIVE,clauses:placeholderClauses()}]},
    {id:'T-CORPORATE',eventType:'CORPORATE',versions:[{v:1,effectiveFrom:HALL_TNC_EFFECTIVE,clauses:placeholderClauses()}]},
  ];
}
