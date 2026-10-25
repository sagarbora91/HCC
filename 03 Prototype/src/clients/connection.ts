// Server reachability as seen by the HTTP adapter (contracts §6). 'unavailable' = read-only mode: critical mutations are
// disabled and nothing is written locally as if committed. The mock adapter never changes it.
export type ConnectionState={status:'online'|'unavailable';/** Last error text while unavailable. */reason?:string;since?:string};

export function createConnection(){
  let state:ConnectionState={status:'online'};
  const subs=new Set<()=>void>();
  const set=(next:ConnectionState)=>{if(next.status===state.status&&next.reason===state.reason)return;state=next;subs.forEach(f=>f());};
  return {
    get:()=>state,
    subscribe(f:()=>void){subs.add(f);return ()=>{subs.delete(f);};},
    markOnline(){if(state.status!=='online')set({status:'online'});},
    markUnavailable(reason:string,at=new Date().toISOString()){set({status:'unavailable',reason,since:state.status==='unavailable'?state.since:at});},
    get readOnly(){return state.status==='unavailable';},
  };
}
export type Connection=ReturnType<typeof createConnection>;
