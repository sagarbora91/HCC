// FX merge-ui · preferences, notes and tags of the two customers of an opened duplicate pair (production only).
// The list loader does not read profiles (each GET /customers/profile is a full customer scan on the server): the compare opens them on demand,
// two reads per pair, remembered per customer key. `setPairProfileFetcher` is wired by the production composition (sync/runtime.ts); the prototype never sets it.
export type PairProfile={preferences:string;notes:string;tags:string[]};
const cache=new Map<string,PairProfile>();
let fetcher:((key:string)=>Promise<PairProfile>)|undefined;
export function setPairProfileFetcher(f:((key:string)=>Promise<PairProfile>)|undefined){fetcher=f;cache.clear();}
export const getPairProfile=(key:string)=>cache.get(key);
export const clearPairProfiles=()=>cache.clear();
/** Loads the profiles not yet remembered (one GET each). Resolves true when something new arrived; a failed read leaves "Not loaded". */
export async function ensurePairProfiles(keys:string[]):Promise<boolean>{
  if(!fetcher)return false;
  const need=[...new Set(keys)].filter(k=>!cache.has(k));
  const got=await Promise.allSettled(need.map(k=>fetcher!(k)));
  let changed=false;
  got.forEach((r,i)=>{if(r.status==='fulfilled'){cache.set(need[i],r.value);changed=true;}});
  return changed;
}
