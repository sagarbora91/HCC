// Shared app context: current state, demo user, navigation and the single run() entry point for commands.
import React,{createContext,useContext} from 'react';
import {State,Command,Permission,User,Room,Booking,Task,RoomStatus,metrics} from '../services/mock';
import {FaultMode} from '../services/mock/faults';
import type {Display} from './display';
import type {Metrics} from './useMetrics';
export type Modal={type:string;id?:string;room?:string;key:string}|null;
export type RunOptions={close?:boolean;after?:()=>void};
export type Ctx={
  s:State;uid:string;user:User;m:Metrics;
  page:string;go:(p:string,opts?:{tab?:string;replace?:boolean})=>void;
  modal:Modal;open:(m:{type:string;id?:string;room?:string})=>void;closeModal:()=>void;booking:(id:string)=>void;
  error:string;busy:boolean;
  run:(cmd:Command,msg?:string,opts?:RunOptions)=>Promise<boolean>;
  toast:(text:string,error?:boolean)=>void;
  permission:(p:Permission|Permission[])=>boolean;
  act:(p:Permission,fn:()=>void,label:string,style?:string)=>React.ReactNode;
  search:string;setSearch:(v:string)=>void;filter:string;setFilter:(v:string)=>void;
  tab:string;setTab:(v:string)=>void;
  arrivals:Booking[];departures:Booking[];pending:number;taskList:Task[];
  roomStatus:(r:Room)=>RoomStatus;
  switchUser:(id:string)=>void;
  fault:FaultMode;setFault:(f:FaultMode)=>void;
  loadScenario:(id:string)=>void;resetScenario:()=>void;
  /** Device display mode (MB-01) — per device, not hotel data. */
  display:Display;
  /** Mark the open sheet/dialog as having unsaved input (Android back asks before discarding). */
  setDirty:(v:boolean)=>void;
  /** Opens the global search palette (Ctrl+K). */
  openSearch:()=>void;
  /** Opens the hamburger menu drawer. */
  openMenu:()=>void;
  /** Back to the staff-picker sign-in screen. */
  signOut:()=>void;
};
export const HccContext=createContext<Ctx|null>(null);
export function useHcc(){const c=useContext(HccContext);if(!c)throw new Error('HccContext missing');return c;}
