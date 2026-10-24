// Shared helpers and types for the FY history module generators.
import type {Line} from '../../domain/types';
import type {GRPBlock} from '../../domain/streams/groups';
import type {POSOrder} from '../../domain/streams/pos';
export type HistoryContext={
  /** Group blocks created in the pre-pass (members are real, already-built stays). */
  groups:GRPBlock[];
  /** Room nights reserved for maintenance downtime (no stay may overlap [from, to)). */
  downtimes:{room:string;from:string;to:string;reason:string}[];
  /** Room-service checks posted to a folio during stay generation; bill numbers are assigned later in date order. */
  roomOrders:{order:POSOrder;line:Line}[];
};
export type HistoryWindow={from:string;to:string};
export const pad=(n:number,w=4)=>String(n).padStart(w,'0');
export const roundTo=(n:number,step:number)=>Math.round(n/step)*step;
export const FIRST_OF:Record<string,string>={reception:'Aarav',cashier:'Priya',supervisor:'Neha',housekeeper:'Ravi',housekeeper2:'Kavya',maint:'Joseph',manager:'Meera',owner:'Sagar',accountant:'Farhan'};
