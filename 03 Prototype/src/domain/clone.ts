// Deep copy for JSON-shaped state. Uses structuredClone where available; older Android System WebViews (Chrome < 98) lack it
// and fall back to a JSON round trip. Only JSON-shaped persisted state is supported;
// undefined object fields are omitted by the fallback, as in persisted snapshots.
export const clone=<T,>(v:T):T=>typeof structuredClone==='function'?structuredClone(v):JSON.parse(JSON.stringify(v)) as T;
