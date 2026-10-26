// React hooks for the server reports (Wave 4 gap G3). Imported only from `process.env.HCC_BUILD==='production'` branches.
import {useEffect,useRef,useState} from 'react';
import {getSyncRuntime} from './instance';
import {useSync} from './react';
import type {SyncApi} from './types';
import {loadServerReport,loadServerHistory,type ServerReport,type ServerHistory} from './reports';

export type Fetched<T>={status:'idle'|'loading'|'ready'|'error';data?:T;error?:string};

/** Fetches when `key` is set; refetches when the key or the runtime's data stamp changes (Refresh, a command, focus). Keeps the last
 *  result of the SAME key while reloading or when the server is unreachable (status 'error' + data = shown "as of" its time). */
export function useServerFetch<T>(key:string|null,load:(api:SyncApi)=>Promise<T>):Fetched<T>{
  const snap=useSync();const [st,setSt]=useState<Fetched<T>&{key?:string}>({status:'idle'});
  const loadRef=useRef(load);loadRef.current=load;
  useEffect(()=>{
    if(key===null||snap.status!=='ready'){setSt(p=>p.status==='idle'?p:{status:'idle'});return;}
    let dead=false;
    setSt(p=>({status:'loading',data:p.key===key?p.data:undefined,key}));
    loadRef.current(getSyncRuntime().api).then(
      data=>{if(!dead)setSt({status:'ready',data,key});},
      e=>{if(!dead)setSt(p=>({status:'error',data:p.key===key?p.data:undefined,key,error:String((e as {message?:string})?.message??e)}));});
    return ()=>{dead=true;};
  },[key,snap.asOf,snap.status]);// eslint-disable-line react-hooks/exhaustive-deps
  return st;
}
export const useServerReport=(from:string,to:string,enabled=true):Fetched<ServerReport>=>useServerFetch(enabled?`${from}|${to}`:null,api=>loadServerReport(api,from,to));
export const useServerHistory=(date:string,enabled=true):Fetched<ServerHistory>=>useServerFetch(enabled&&date?date:null,api=>loadServerHistory(api,date));
