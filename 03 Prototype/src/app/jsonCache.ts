// JSON text already produced for a state slice (by the scenario worker), so the first save does not stringify it again.
// Keyed by object identity; slices are immutable once built (all changes go through execute(), which copies a slice first).
const cache=new WeakMap<object,string>();
export const rememberJson=(v:object,json:string)=>{cache.set(v,json);};
export const knownJson=(v:object)=>cache.get(v);
