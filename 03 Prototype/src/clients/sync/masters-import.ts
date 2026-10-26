// MA-8 · Production mapping of MST_IMPORT (masters JSON import and "Paste rows"): each list is sent to the server import as a CSV file with the
// template headers (08 Production/deployment/import-templates, ImportLists.cs) — UploadImportBatch (validate + preview), then CommitImportBatch when the
// file has no refused rows and something to change. The server owns the rules and the rights (money lists commit as the Owner only, a file never
// retires a row); a refusal is shown as the first row problems. Room types and rooms are not in this path (their own import, older columns).
import type {CommandMapper} from './types';
import {HccClientError} from '../errors';
import {MST_IO_WIRE,mstCsvOfRows,mstSpec,mstImportOrder,type MstIoSet,type MstIoRow,type Command} from '../../services/mock';

type SrvReport={batchId:number;master:string;errorCount:number;errors:{row:number;column:string;message:string}[];counts:{create:number;update:number;unchanged:number;remove:number};
  commitAllowed:boolean;commitBlockedReason?:string|null};

const setsOf=(cmd:Command):MstIoSet[]=>Array.isArray(cmd.sets)?cmd.sets as MstIoSet[]:[];

export const MST_IMPORT_COMMANDS:CommandMapper[]=[
  {type:'MST_IMPORT',reload:['mastersX'],
    precheck(cmd){
      const sets=setsOf(cmd);
      if(!sets.length)return 'Nothing to import: send the lists and their rows.';
      for(const x of sets){
        if(!mstSpec(x.master))return `Not available on the server yet: the ${String(x.master)} list.`;
        if(!MST_IO_WIRE[x.master])return `${mstSpec(x.master)!.label} have their own import (Rooms); they cannot be pasted or imported here.`;
      }
      return undefined;
    },
    async send(c){
      const sets=setsOf(c.cmd),order=mstImportOrder(sets.map(x=>x.master));
      const tablesOf=(outlet:string)=>(sets.find(x=>x.master==='posTables')?.rows??[]).filter((r:MstIoRow)=>String(r.outlet??'').toUpperCase()===outlet.toUpperCase()).map((r:MstIoRow)=>String(r.tableNo??'')).filter(Boolean).join(';');
      let added=0,changed=0;const done:string[]=[];
      for(const id of order){
        const set=sets.find(x=>x.master===id)!,spec=mstSpec(id)!,wire=MST_IO_WIRE[id];
        const csv=mstCsvOfRows(c.state,spec,set.rows,id==='outlets'?{tables:r=>tablesOf(String(r.code??''))}:{});
        const up=await c.api.command<SrvReport>('UploadImportBatch',{master:wire,fileName:wire+'.csv',csvText:csv},{step:'up:'+wire});
        if(up.errorCount>0)throw new HccClientError('VALIDATION',`${spec.label}: ${up.errorCount} row problem${up.errorCount===1?'':'s'} — nothing was imported from this list. ${up.errors.slice(0,3).map(e=>`row ${e.row}${e.column?' · '+e.column:''}: ${e.message}`).join(' · ')}`);
        if(!up.commitAllowed){continue;}
        await c.api.command('CommitImportBatch',{batchId:up.batchId},{step:'commit:'+wire});
        added+=up.counts.create;changed+=up.counts.update;done.push(wire);
      }
      return {result:{added,changed,lists:done}};
    }},
];
