// W0-C · stay-restriction UI state (DEC-053): pure helpers shared by the desktop dialogs, the phone wizard and the rooming list.
// No React here, so tests/stream-a2-ui.mjs can bundle it. The server (BOOK / CHANGE_DATES / GRP_ROOMING) re-checks everything.
import {State,Booking,can,yldStayViolations,YLDViolation} from '../../services/mock';

export const RST_MIN_REASON=3;
export type RstState={violations:YLDViolation[];canOverride:boolean;overridden:boolean;canSubmit:boolean;
  /** Value for cmd.restrictionOverride: set only when there are violations and the override is ticked. */
  restrictionOverride?:string};

/** Restrictions a stay breaks for one room type (or several, e.g. a group block), and whether the dialog may submit.
 *  `override` = the reason typed with the override ticked; null/undefined = not ticked. Unknown type ('' / 'All') → no check. */
export function rstState(s:State,uid:string|undefined,type:string|string[],arrival:string,departure:string,override?:string|null):RstState{
  const types=[...new Set((Array.isArray(type)?type:[type]).filter(t=>t&&t!=='All'))];
  const seen=new Set<string>(),violations:YLDViolation[]=[];
  for(const t of types)for(const v of yldStayViolations(s,t,arrival,departure)){
    const k=v.code+v.restriction+v.message;if(!seen.has(k)){seen.add(k);violations.push(v);}}
  const canOverride=!!uid&&can(s,uid,'approve');
  const reason=override==null?'':String(override).trim();
  const overridden=violations.length>0&&canOverride&&override!=null&&reason.length>=RST_MIN_REASON;
  return {violations,canOverride,overridden,canSubmit:!violations.length||overridden,restrictionOverride:overridden?reason:undefined};
}

/** The override recorded on a booking's notes by BOOK / CHANGE_DATES ("restriction overridden (MIN_STAY YLDR-1): reason"). */
export function rstOverrideNotes(b:Pick<Booking,'notes'>){
  const out:{rules:string;reason:string}[]=[];const re=/restriction overridden \(([^)]*)\): (.*?)(?= · restriction overridden|$)/g;
  for(const m of String(b.notes||'').matchAll(re))out.push({rules:m[1],reason:m[2].trim()});
  return out;
}
