// Wave C3 · System health — SYS_RETRY_PARKED {consumer, outboxId}: release a parked outbox event for one more delivery attempt
// (server RetryParkedEvent, PERM-SYS-001 = Owner + Tech admin; prototype gate 'admin'). Same refusals and audit text as the server.
// The demo has no poller: the released event counts as delivered on the next poll, so it leaves the list (the server shows it again,
// with one more attempt, only if it fails again).
import type {Handler} from '../commands';
import {sysHealthView,sysConsumerLabel,sysParkedKey} from '../../../domain/streams/syshealth';

export const handlers:Record<string,Handler>={
  SYS_RETRY_PARKED:k=>{
    const {s,cmd,req,assert,fail}=k;req('admin');
    const consumer=String(cmd.consumer??'').trim(),id=String(cmd.outboxId??'').trim();
    assert(consumer.length>0&&consumer.length<=40,'Choose the consumer of the parked event (1 to 40 characters).');
    assert(/^\d+$/.test(id)&&Number(id)>0,'Choose the parked event (outbox id).');
    const v=sysHealthView(s),p=v.parked.find(x=>x.consumer===consumer&&x.outboxId===id);
    if(!p)fail('NOT_FOUND','This parked event was not found: it may have been retried or delivered already. Refresh System health.');
    const key=sysParkedKey(p!);
    s.sysHealthX={...(s.sysHealthX??{}),source:v.source,parked:v.parked.filter(x=>sysParkedKey(x)!==key)};
    return `Parked event ${id} (${p!.eventType}) of ${sysConsumerLabel(consumer)} [${consumer}] released for one more delivery attempt after ${p!.attempts} failed attempt(s) (${p!.errorType})`;
  },
};
