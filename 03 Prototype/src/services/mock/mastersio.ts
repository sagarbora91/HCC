// MA-8 · Dry run of MST_IMPORT: runs the real command on a draft of the state (execute never mutates its input) and reads the report the
// handler attaches to MstImportPreview — so the preview of a pasted list or a masters file is exactly what Import would do.
import {execute} from './commands';
import type {State} from '../../domain/types';
import {MstImportPreview} from './handlers/masters';
import type {MstIoSet,MstImportReport} from '../../domain/streams/mastersio';

export function mstImportPreview(s:State,uid:string,sets:MstIoSet[]):MstImportReport{
  try{execute(s,uid,{type:'MST_IMPORT',sets,dryRun:true});}
  catch(e){if(e instanceof MstImportPreview)return e.report;throw e;}
  throw new Error('MST_IMPORT dry run did not produce a report.');
}
