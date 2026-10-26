// CP-1 → CP-4 bridge. CP-4 adds setHonourSystemFont(flag) / readSystemFontScale() to src/app/native.ts (Android TextZoom plugin).
// Until that lands (or outside the APK) these calls do nothing. Looked up by name so this file compiles and builds without warnings either way.
import * as native from '../../app/native';
const lookup=(name:string):unknown=>(native as unknown as Record<string,unknown>)[name];
/** Tells the APK whether the WebView keeps following the Android system font size. Resolves false when the bridge is missing. */
export async function applyHonourSystemFont(flag:boolean):Promise<boolean>{
  const fn=lookup('setHonourSystemFont');
  if(typeof fn!=='function')return false;
  try{await (fn as (f:boolean)=>unknown)(flag);return true;}catch{return false;}
}
/** Android system font scale (1 = normal) when the bridge reports it; undefined otherwise. */
export async function systemFontScale():Promise<number|undefined>{
  const fn=lookup('readSystemFontScale');
  if(typeof fn!=='function')return undefined;
  try{const v=await (fn as ()=>unknown)();return typeof v==='number'&&v>0?v:undefined;}catch{return undefined;}
}
