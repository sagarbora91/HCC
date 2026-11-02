// LF-ALERTS · A8 function keys (LF contract §9.3). Desktop layout only. F1, F5, F11 and F12 are never used (help, reload, full screen, devtools).
// The table is pure data + two helpers; App.tsx adds ONE keydown listener (its LF-ALERTS block). A shortcut fires only when the user holds the
// permission, no dialog is open, and the page (or modal) it opens exists.
import type {Permission} from '../domain/types';
export type Shortcut={id:string;key:string;label:string;/** page id */page:string;modal?:string;permission:Permission|Permission[];legacy?:string;
  /** F9 and Alt+B open dialogs / pages from other lanes: the shortcut degrades to the page when the modal is not registered. */optional?:boolean};
/** Modifier rule per key: F-keys alone; Alt+B and Alt+/ with Alt only. */
export const SHORTCUTS:Shortcut[]=[
  {id:'new-booking',key:'F2',label:'New booking',page:'reservations',modal:'new',permission:'reservations',legacy:'F7 advance booking'},
  {id:'arrivals',key:'F3',label:'Arrivals & check-in',page:'arrivals',permission:'checkin',legacy:'F4 edit check-in'},
  {id:'inhouse',key:'F4',label:'In-house stays',page:'inhouse',permission:'billing'},
  {id:'rooms',key:'F6',label:'Room board',page:'rooms',permission:'reservations',legacy:'F6 room status'},
  {id:'folios',key:'F7',label:'Guests & folios (payment / deposit)',page:'folios',permission:['billing','payments'],legacy:'F5 deposit'},
  {id:'checkout',key:'F8',label:'Check-out (In-house stays)',page:'inhouse',permission:'checkout'},
  {id:'rate-change',key:'F9',label:'Rate / guest change (selected stay)',page:'inhouse',modal:'stay-rate',permission:'checkin',legacy:'F9 rate / pax change',optional:true},
  {id:'cashier',key:'F10',label:'Cashier shift',page:'cashier',permission:'payments',legacy:'F3 counter cashier'},
  {id:'counter-bill',key:'Alt+B',label:'Counter bill',page:'counter-bills',permission:['billing','payments'],legacy:'F11 direct bill',optional:true},
  {id:'shortcut-list',key:'Alt+/',label:'Keyboard shortcuts',page:'',modal:'shortcuts',permission:[] as Permission[],legacy:'F1 help'},
];
/** The shortcut a keydown event stands for (undefined = not ours). Pure: takes the fields of the event. */
export function shortcutFor(e:{key:string;altKey?:boolean;ctrlKey?:boolean;metaKey?:boolean;shiftKey?:boolean}):Shortcut|undefined{
  const ctrl=!!(e.ctrlKey||e.metaKey);
  if(/^F\d{1,2}$/.test(e.key)){if(ctrl||e.altKey||e.shiftKey)return undefined;return SHORTCUTS.find(x=>x.key===e.key);}
  if(e.altKey&&!ctrl&&!e.shiftKey){const k=e.key.length===1?e.key.toLowerCase():e.key;return SHORTCUTS.find(x=>x.key==='Alt+'+(k==='b'?'B':k));}
  return undefined;
}
/** Is the user allowed this shortcut (permission any-of; an empty list = everyone)? */
export function shortcutAllowed(sc:Shortcut,has:(p:Permission|Permission[])=>boolean):boolean{const p=sc.permission;return Array.isArray(p)&&!p.length?true:has(p);}
/** The shortcut key for a page (search palette hint); the first entry that opens the page without a dialog. */
export const shortcutKeyOfPage=(page:string)=>SHORTCUTS.find(x=>x.page===page&&!x.modal)?.key;
