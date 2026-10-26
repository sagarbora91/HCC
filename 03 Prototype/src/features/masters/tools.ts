// MA-1 · Extension point of MasterList / MasterListPhone: a master may add its own tools above the list (rooms: range add, renumber,
// out of order), hide the generic "Add" (rows created by the tools only) and hide the reorder arrows (order derived from the data).
// Providers register at module load (features/<module>/register.tsx); the framework only reads the registry.
import type React from 'react';

export type MstToolsProps={specId:string;phone?:boolean};
export type MstToolsEntry={Tools?:React.ComponentType<MstToolsProps>;hideAdd?:boolean;noReorder?:boolean};
export const mstTools:Record<string,MstToolsEntry>={};
export function registerMasterTools(specId:string,entry:MstToolsEntry){mstTools[specId]=entry;}
