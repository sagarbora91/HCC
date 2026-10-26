# A08-01 outbox poll: before and after the fix (fix wave A, 26 Oct 2026)

**What changed.** Migration `2900_outbox_consumers.sql` adds the index `IX_outbox_type (event_type, outbox_id) INCLUDE (property_id)`, a per-consumer low watermark (`plat.outbox_consumer`), stored subscriptions (`plat.outbox_subscription`) and per-event failure records (`plat.consumer_failure`). `OutboxConsumer` now polls from the watermark with one index seek per event type (`CROSS APPLY` over a `VALUES` list of parameters, not `OPENJSON`). The retention purge removes processed events older than 30 days.

**How it was measured.** The same data as `A08_outbox_poll.sql`: 30 event types, 5 consumers, every event processed except the last 20 per consumer, payloads of about 300 characters. Each run used a throwaway `HccTest_OUTBOX_*` database on `.\SQLEXPRESS` that was created and then dropped. Before: `A08_outbox_poll.sql` (the old statement). After: `A08_outbox_poll_after.sql` (the same data plus migration 2900, and the statement the new `OutboxConsumer` sends, run through `sp_executesql`). Each run held the SQL test lock. Raw output is in `evidence/A08/outbox_poll_{before,after}_fix_{100000,250000}.txt` and `outbox_v1_scan_before_fix_250000.txt`.

## Accounting poll (12 types), per poll

| Outbox rows | Before: logical reads (outbox + dedup) | Before: CPU / elapsed | After, steady state: logical reads (all tables) | After: CPU / elapsed |
|---|---|---|---|---|
| 100,000 | 8,374 + 192 | 1.08–1.11 s / 1.1 s | 47 + 48 + 3 + 33 = 131 | < 16 ms (shown as 0) / < 5 ms |
| 250,000 | 21,435 + 476 | 4.03–4.06 s / 5.9–6.3 s | 47 + 48 + 3 + 33 = 131 | < 16 ms (shown as 0) / < 5 ms |

- **Housekeeping poll (1 type).** At 250k rows it took 937 ms CPU and 21,435 reads before the fix. After the fix it takes 4 + 3 + 3 reads and about 0 ms.
- **V1 scan (`V1_outbox_scan.sql`, 250k rows, nothing pending, before the fix).** Accounting took 2.6 s CPU and 17,943 + 738 reads. OTA took 0.7 s CPU and 17,943 + 151 reads.
- **First poll after the upgrade.** The watermark starts at 0, so the first poll walks the history once. At 250k rows this took 418 + 5,712 reads and about 0.5 s CPU. At 100k it took 212 + 2,304 reads and 0.19 s. After that one poll, every later poll costs the same as the steady state above.
- **Steady state is independent of table size.** The reads are identical at 100k and at 250k: one B-tree seek per type, plus the dedup probe, the "settled" head read and the payload lookups. Table size enters only through the B-tree depth, which grows logarithmically.

## Notes
- The first version of the fix used `event_type IN (@t0 … @t11) AND outbox_id > @low`. SQL Server turned the parameter IN list into a Merge Interval range seek on `event_type` alone, so the poll still read 418 pages. The per-type `CROSS APPLY` is what reaches the 47-page seek. The plan shows `SEEK: event_type = t.event_type AND outbox_id > @low`.
- The watermark never passes an event younger than 60 s (`OutboxConsumer.SettleSeconds`), and the pending read is a locking read (`READCOMMITTEDLOCK`). As a result, an event still being written can never be skipped, including under a future READ_COMMITTED_SNAPSHOT setting.
- The before figures here are lower than the audit's figures (5.5–6.1 s at 365k), because the audit ran while the instance was busy. They still grow linearly with table size.
