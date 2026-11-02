// F4-T2 · Owner digest command (F4-CONTRACT §3). DIGEST_REDRAFT {date} rewrites the two held drafts of a closed date. Nothing here sends.
import type {Handler} from '../commands';
import {dgsCanView,dgsWriteDrafts} from '../../../domain/streams/digest';
export const handlers:Record<string,Handler>={
  DIGEST_REDRAFT:k=>{
    const {s,cmd,uid,assert,fail}=k;
    if(!dgsCanView(s,uid))fail('FORBIDDEN','Access denied: PERM-DGS-001 (owner digest) is Owner only.');
    const date=String(cmd.date??'');assert(!!s.closings?.[date],'That business date has no closing record.','NOT_FOUND');
    s.digestX=dgsWriteDrafts(s,uid,date);
    return `Digest drafts for ${date} written (held, not sent)`;
  },
};
