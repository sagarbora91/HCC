// Staff directory (W4 N3 / E2E2 N6): display names for every signed-in role. Screens look names up in s.users (requested by, prepared
// by, decided by, assigned to); only administrators load the full user list (/admin/users), so everyone else saw ids or blanks.
// Server: GET /me/directory (Access module): ids, display names, enabled flag and role codes; no login names, no credentials; any signed-in user.
// This area patches `users` (kind 'patch'): for administrators it leaves the admin list alone; for other roles it fills the
// directory entries (name, enabled, current role: the role is what lets a supervisor's assignee list find the housekeepers).
import type {SyncArea} from './types';
import type {State} from '../../domain/types';
import {protoUserId} from './map';
import {ROLE_MAP} from './capabilities';
import {HccClientError} from '../errors';

export type SrvDirectory={items:{userId:number;displayName:string;enabled?:boolean;roles?:string[]}[]};

export function directoryUsers(d:SrvDirectory,known:State['users']):State['users']{
  const have=new Set(known.map(u=>u.id));
  const extra=d.items.filter(x=>x.displayName&&!have.has(protoUserId(x.userId)))
    .map(x=>({id:protoUserId(x.userId),name:x.displayName,role:(x.roles??[]).map(r=>ROLE_MAP[r]).find(Boolean)??'Reception',enabled:x.enabled!==false}));
  return [...known,...extra];
}

export const directory:SyncArea={
  id:'directory',
  loaders:[{slice:'users',kind:'patch',reads:['GET /me/directory'],
    async load(c){
      const known=c.current('users')??[];
      if(c.can('admin'))return known; // the admin area already holds every user
      const d=await c.api.get<SrvDirectory>('/me/directory');
      return directoryUsers(d,known);
    }}],
  commands:[],
  unmapped:{},
};
