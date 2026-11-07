// Training driver: the act mini-language (plan §7.3). Pure parsing, unit-tested in tests/stream-training.mjs.
export type ActKind='nav'|'user'|'click'|'type'|'select'|'check'|'uncheck'|'key'|'menu'|'modal'|'wait'|'js'|'display'|'signout'|'hover'|'scroll'|'focus';
export type Act={kind:ActKind;arg:string;value?:string;raw:string};
const KINDS:readonly string[]=['nav','user','click','type','select','check','uncheck','key','menu','modal','wait','js','display','signout','hover','scroll','focus'];
const TWO=new Set(['type','select']); // "<target>|<value>"
/** "type:field:Guest name|Rahul" -> {kind:'type',arg:'field:Guest name',value:'Rahul'}. Throws on an unknown kind. */
export function parseAct(raw:string):Act{
  const i=raw.indexOf(':');const kind=(i<0?raw:raw.slice(0,i)).trim();
  if(!KINDS.includes(kind))throw new Error(`unknown act "${raw}"`);
  const rest=i<0?'':raw.slice(i+1);
  if(TWO.has(kind)){const j=rest.indexOf('|');if(j<0)throw new Error(`act "${raw}" needs "<target>|<value>"`);return {kind:kind as ActKind,arg:rest.slice(0,j),value:rest.slice(j+1),raw};}
  return {kind:kind as ActKind,arg:rest,raw};
}
export type KeyCombo={key:string;ctrlKey:boolean;shiftKey:boolean;altKey:boolean;metaKey:boolean};
/** "ctrl+k" / "Escape" / "F2" -> KeyboardEvent init. */
export function parseKey(spec:string):KeyCombo{
  const parts=spec.split('+').map(p=>p.trim());const key=parts.pop()!;const mods=parts.map(p=>p.toLowerCase());
  const k=key.length===1?key.toLowerCase():key;
  return {key:k,ctrlKey:mods.includes('ctrl')||mods.includes('control'),shiftKey:mods.includes('shift'),altKey:mods.includes('alt'),metaKey:mods.includes('meta')||mods.includes('cmd')};
}
/** "Front Desk>Room board" -> ['Front Desk','Room board'] ("Dashboard" alone has one part). */
export const parseMenuPath=(p:string)=>p.split('>').map(x=>x.trim()).filter(Boolean);
