// A09 performance probe (audit only). Drives a locally running HCC API (Development, disposable HccTest_perf_* database)
// through simulated business days, then times the heaviest reads. Fictional data only; credentials are generated in memory
// and never written to disk or printed.
// Usage: node A09_perf_seed.mjs <baseUrl e.g. http://127.0.0.1:5291/api/v1> <outDir> <startDate yyyy-mm-dd> <days> <seedMinutes>
import fs from 'node:fs';
import path from 'node:path';
import { randomUUID, randomInt, randomBytes } from 'node:crypto';

const [BASE, OUT, START, DAYS_S, MIN_S] = process.argv.slice(2);
const DAYS = +DAYS_S, BUDGET_MS = +MIN_S * 60_000;
const t0 = Date.now();
const log = (...a) => { const s = `[${((Date.now() - t0) / 1000).toFixed(1)}s] ` + a.join(' '); console.log(s); fs.appendFileSync(path.join(OUT, 'perf-seed.log'), s + '\n'); };
let cookie = '';
const stats = {};
async function call(method, p, body, key) {
  const h = { 'Content-Type': 'application/json' };
  if (cookie) h.Cookie = cookie;
  if (method === 'POST') h['Idempotency-Key'] = key || randomUUID();
  const s = performance.now();
  const r = await fetch(BASE + p, { method, headers: h, body: body ? JSON.stringify(body) : undefined });
  const t = await r.text();
  const ms = performance.now() - s;
  const k = method + ' ' + p.replace(/\/\d+/g, '/{id}').replace(/\?.*/, '');
  (stats[k] ??= []).push(ms);
  let j; try { j = JSON.parse(t); } catch { j = t; }
  const sc = r.headers.get('set-cookie'); if (sc) cookie = sc.split(';')[0];
  return { status: r.status, body: j, ms };
}
async function cmd(name, body) {
  const r = await call('POST', '/commands/' + name, body);
  if (r.status !== 200) throw new Error(`${name} ${r.status} ${JSON.stringify(r.body).slice(0, 400)}`);
  return r.body;
}
const iso = d => d.toISOString().slice(0, 10);
const addDays = (s, n) => { const d = new Date(s + 'T00:00:00Z'); d.setUTCDate(d.getUTCDate() + n); return iso(d); };

// ---- fictional masters ----
const first = ['Aarav', 'Vivaan', 'Aditya', 'Vihaan', 'Arjun', 'Sai', 'Reyansh', 'Krishna', 'Ishaan', 'Rohan', 'Ananya', 'Diya', 'Saanvi', 'Aadhya', 'Kavya', 'Meera', 'Pooja', 'Sneha', 'Neha', 'Riya', 'Sagar', 'Amit', 'Rahul', 'Vikram', 'Suresh', 'Kiran', 'Nikhil', 'Prakash', 'Ganesh', 'Mahesh', 'Asha', 'Lata'];
const last = ['Patil', 'Deshmukh', 'Kulkarni', 'Joshi', 'Shinde', 'Pawar', 'Jadhav', 'More', 'Gaikwad', 'Chavan', 'Bhosale', 'Kale', 'Mane', 'Sawant', 'Rao', 'Iyer', 'Shah', 'Mehta', 'Gupta', 'Sharma', 'Verma', 'Reddy', 'Nair', 'Menon', 'Kapoor', 'Agarwal', 'Bora', 'Jain', 'Thakur', 'Naik', 'Salunkhe', 'Wagh', 'Kadam', 'Lokhande', 'Shetty', 'Pillai'];
const customers = [];
for (let i = 0; customers.length < 1155; i++) customers.push({ name: `${first[i % first.length]} ${last[Math.floor(i / first.length) % last.length]} ${i >= first.length * last.length ? i : ''}`.trim() + ' (fictional)', phone: '9' + String(700000000 + i * 37).padStart(9, '0') });
const roomTypesCsv = 'code,name,maxOccupancy,rackRate,sortOrder\nSTD,Standard Room,2,2800,1\nDLX,Deluxe Room,3,4200,2\nSTE,Suite,4,8200,3\n';
const roomsList = [...Array.from({ length: 12 }, (_, i) => [String(101 + i), 'STD']), ...Array.from({ length: 8 }, (_, i) => [String(201 + i), 'DLX']), ...Array.from({ length: 4 }, (_, i) => [String(301 + i), 'STE'])];
const roomsCsv = 'roomNo,roomType\n' + roomsList.map(r => r.join(',')).join('\n') + '\n';
const tariffsCsv = `roomType,effectiveFrom,rate\nSTD,${START},3000\nDLX,${START},4500\nSTE,${START},8200\n`;
const guestsCsv = 'name,phone,email,address,nationality,company,gstin,notes\n' + customers.map(c => `${c.name},${c.phone},,Latur,Indian,,,`).join('\n') + '\n';
const tillCsv = 'tillCode,tillName,openingCash\nFD-1,Front desk drawer,25000.00\n';
const samples = 'E:/Projects/HCC/Hotel Control Centre/08 Production/deployment/import-templates/samples/';

async function setup() {
  const ownerPassword = 'Perf-' + randomBytes(12).toString('base64url');   // in memory only
  let r = await call('POST', '/setup/SetupProperty', { name: 'Perf Probe Residency (fictional)', address: '1 Test Road, Latur', code: 'MAIN', state: 'Maharashtra', ownerLoginName: 'owner', ownerDisplayName: 'Perf Owner', ownerPassword, businessDate: START, gstin: '27AAAAA0000A1Z2' });
  log('SetupProperty', r.status, JSON.stringify(r.body).slice(0, 200));
  r = await call('POST', '/auth/sign-in', { loginName: 'owner', password: ownerPassword });
  log('sign-in', r.status);
  if (r.status !== 200) throw new Error('sign-in failed');
  const up = async (master, fileName, csvText, asOfDate) => {
    const u = await call('POST', '/commands/UploadImportBatch', { master, fileName, csvText, asOfDate });
    const id = u.body?.batchId ?? u.body?.batch?.batchId;
    if (!id) { log(master, 'upload', u.status, JSON.stringify(u.body).slice(0, 300)); return; }
    const c = await call('POST', '/commands/CommitImportBatch', { batchId: id });
    log(master, 'upload', u.status, 'commit', c.status, c.status === 200 ? '' : JSON.stringify(c.body).slice(0, 300));
  };
  await up('room-types', 'room-types.csv', roomTypesCsv);
  await up('rooms', 'rooms.csv', roomsCsv);
  await up('tariffs', 'tariffs.csv', tariffsCsv);
  await up('ledgers', 'ledgers.csv', fs.readFileSync(samples + 'ledgers.csv', 'utf8'));
  await up('opening-balances', 'opening-balances.csv', fs.readFileSync(samples + 'opening-balances.csv', 'utf8'), START);
  await up('till-cash', 'till-cash.csv', tillCsv);
  await up('guests', 'guests.csv', guestsCsv);
}

async function rooms() {
  const b = await call('GET', '/rooms/board?pageSize=100');
  return (b.body.rows ?? []).map(r => ({ roomId: r.roomId, roomNo: r.roomNo }));
}
async function tillId() {
  const d = await call('GET', '/cashier/desk');
  const s = JSON.stringify(d.body);
  const m = s.match(/"tillId":(\d+)/);
  log('cashier/desk', d.status, s.slice(0, 300));
  return m ? +m[1] : null;
}

const losPick = () => { const x = randomInt(100); return x < 30 ? 1 : x < 60 ? 2 : x < 80 ? 3 : x < 90 ? 4 : 5 + randomInt(3); };
const tenders = ['Cash', 'UPI', 'Card', 'UPI', 'Card'];

async function hkCleanAll() {
  // Turn every Dirty room Ready through the housekeeping task flow (Owner does assign/start/check/complete/inspect).
  // The task list is paged (max 100) and stayover tasks pile up (the probe never cleans them), so take the checkout
  // tasks from the room board's openTasks instead (one call).
  const board = (await call('GET', '/rooms/board?pageSize=100')).body.rows ?? [];
  const open = board.flatMap(r => r.openTasks ?? []).filter(t => t.kind === 'checkout' && t.status !== 'Done' && t.status !== 'Cancelled');
  for (const t of open) {
    try {
      let v = t.version;
      const me = await meId();
      let s = await cmd('TransitionHousekeepingTask', { taskId: t.taskId, expectedVersion: v, action: 'assign', assigneeId: me }); v = s.task.version;
      s = await cmd('TransitionHousekeepingTask', { taskId: t.taskId, expectedVersion: v, action: 'start' }); v = s.task.version;
      for (let i = 0; i < (s.task.checklist?.length ?? 3); i++) { s = await cmd('TransitionHousekeepingTask', { taskId: t.taskId, expectedVersion: v, action: 'check', index: i }); v = s.task.version; }
      s = await cmd('TransitionHousekeepingTask', { taskId: t.taskId, expectedVersion: v, action: 'complete' }); v = s.task.version;
      await cmd('TransitionHousekeepingTask', { taskId: t.taskId, expectedVersion: v, action: 'inspect' });
    } catch (e) { log('hk', t.taskId, e.message.slice(0, 200)); }
  }
}
let hkLogged = 0; let _me; async function meId() { if (!_me) _me = (await call('GET', '/me/capabilities')).body.userId; return _me; }

async function simulate(till) {
  const roomList = await rooms();
  log('rooms', roomList.length);
  const busyUntil = new Map(); // roomId -> departure date (exclusive)
  const inHouse = []; // {stayId, folioId, stayVersion, departure, roomId}
  let bookings = 0, cust = 0, closed = 0, firstErr = 0;
  const dayRows = [];
  for (let d = 0; d < DAYS; d++) {
    if (Date.now() - t0 > BUDGET_MS) { log('seed time budget reached after', d, 'days'); break; }
    const D = addDays(START, d), ds = Date.now();
    // 1) departures due today: settle and check out
    const due = inHouse.filter(s => s.departure === D);
    let shift = null;
    if (till) { try { shift = await cmd('OpenShift', { tillId: till, openingFloatPaise: null }); } catch (e) { if (!firstErr++) log('OpenShift', e.message.slice(0, 300)); } }
    for (const s of due) {
      try {
        const f = (await call('GET', `/folios/${s.folioId}`)).body;
        const bal = f?.totals?.balancePaise ?? 0;
        if (bal > 0) { const tender = shift ? tenders[randomInt(tenders.length)] : 'UPI'; await cmd('RecordReceipt', { folioId: s.folioId, kind: 'payment', tender, amountPaise: bal, reference: tender === 'Cash' ? null : 'REF-' + s.folioId, tillId: tender === 'Cash' ? till : null }); }
        await cmd('CheckoutStay', { stayId: s.stayId, expectedVersion: s.stayVersion });
        try { await cmd('IssueInvoice', { sourceKind: 'folio', sourceId: s.folioId }); } catch (e) { if (firstErr++ < 20) log('invoice', e.message.slice(0, 300)); }
        inHouse.splice(inHouse.indexOf(s), 1);
      } catch (e) { if (firstErr++ < 20) log('checkout', D, e.message.slice(0, 300)); }
    }
    if (due.length) { await new Promise(r => setTimeout(r, 2500)); await hkCleanAll(); }
    // 2) arrivals: ~5 per day into rooms free tonight
    const board = (await call('GET', '/rooms/board?pageSize=100')).body.rows ?? [];
    const ready = new Set(board.filter(r => r.status === 'Ready').map(r => r.roomId));
    if (d === 0) log('board row', JSON.stringify(board[0]).slice(0, 300));
    const free = roomList.filter(r => !(busyUntil.get(r.roomId) > D) && ready.has(r.roomId));
    const n = Math.min(free.length, 3 + randomInt(5));
    for (let i = 0; i < n; i++) {
      const room = free.splice(randomInt(free.length), 1)[0];
      const los = losPick(), dep = addDays(D, los), c = customers[cust++ % customers.length];
      try {
        const q = await cmd('QuoteStay', { arrival: D, departure: dep, adults: 2, children: 0, roomId: room.roomId });
        const b = await cmd('CreateReservation', { quoteId: q.quoteId, quoteVersion: q.version, guest: { name: c.name, phone: c.phone }, roomId: room.roomId });
        let det = (await call('GET', `/reservations/${b.reservationId}`)).body;
        await cmd('RegisterGuest', { reservationId: b.reservationId, expectedVersion: det.version, guest: { name: c.name, phone: c.phone, address: 'Test Road, Latur (fictional)', nationality: 'Indian', idType: 'Aadhaar', idLast4: String(1000 + randomInt(9000)) } });
        det = (await call('GET', `/reservations/${b.reservationId}`)).body;
        const st = await cmd('CheckIn', { reservationId: b.reservationId, expectedVersion: det.version });
        inHouse.push({ stayId: st.stayId, folioId: st.folioId, stayVersion: st.stayVersion, departure: dep, roomId: room.roomId });
        busyUntil.set(room.roomId, dep); bookings++;
      } catch (e) { if (firstErr++ < 20) log('arrival', D, room.roomNo, e.message.slice(0, 300)); }
    }
    // 3) cash count (exact) and day close
    if (shift) {
      try { const v = (await call('GET', `/shifts/${shift.shiftId}`)).body; const exp = v.expected?.expectedPaise ?? 0; await cmd('SubmitShiftCount', { shiftId: shift.shiftId, countedPaise: exp, notes: null, coinsPaise: exp, reason: null, expectedVersion: v.shift?.version }); }
      catch (e) { if (firstErr++ < 20) log('count', D, e.message.slice(0, 300)); }
    }
    const cs = Date.now();
    try { const r = await cmd('RunBusinessDayClose', { businessDate: D, runId: 'perf-' + D.replaceAll('-', '') }); if (r.status === 'Completed') closed++; else log('close', D, r.status); }
    catch (e) { log('close FAILED', D, e.message.slice(0, 600)); break; }
    dayRows.push({ date: D, arrivals: n, departures: due.length, inHouse: inHouse.length, closeMs: Date.now() - cs, dayMs: Date.now() - ds });
    if (d % 10 === 0) log('day', D, 'bookings', bookings, 'inHouse', inHouse.length, 'closeMs', Date.now() - cs, 'dayMs', Date.now() - ds);
  }
  fs.writeFileSync(path.join(OUT, 'perf-days.json'), JSON.stringify(dayRows));
  return { bookings, closed, lastDate: dayRows.at(-1)?.date, longFolio: inHouse[0]?.folioId };
}

async function timeReads(info) {
  const S = START, E = info.lastDate ?? START;
  const month = E.slice(0, 8) + '01';
  const folios = await call('GET', '/cashier/folios');
  const reads = [
    ['Room board', '/rooms/board?pageSize=50'],
    ['Bookings list (default view)', '/reservations'],
    ['Bookings list (full year range)', `/reservations?from=${S}&to=${E}`],
    ['Folio (in-house)', `/folios/${info.longFolio ?? 1}`],
    ['Cash book range (year)', `/cashbook?from=${S}&to=${E}`],
    ['Trial balance (cumulative, last day)', `/accounting/trial-balance?date=${E}&cumulative=true`],
    ['GSTR-1 view (one month)', `/invoices/gstr1?from=${month}&to=${E}`],
    ['GSTR-1 view (whole year)', `/invoices/gstr1?from=${S}&to=${E}`],
    ['Customers search', '/customers?q=Patil&page=1&pageSize=50'],
    ['Customers list (no filter)', '/customers?page=1&pageSize=50'],
    ['Reports day pack (one day)', `/reports/daily?from=${E}&to=${E}`],
    ['Reports day pack (year range)', `/reports/daily?from=${S}&to=${E}`],
    ['Cashier folios', '/cashier/folios'],
  ];
  const out = [];
  for (const [name, p] of reads) {
    const first = await call('GET', p);
    const ms = [];
    for (let i = 0; i < 10; i++) ms.push((await call('GET', p)).ms);
    ms.sort((a, b) => a - b);
    const size = JSON.stringify(first.body).length;
    out.push({ name, path: p, status: first.status, coldMs: +first.ms.toFixed(1), minMs: +ms[0].toFixed(1), medianMs: +((ms[4] + ms[5]) / 2).toFixed(1), maxMs: +ms[9].toFixed(1), responseBytes: size });
    log('read', name, first.status, 'median', ((ms[4] + ms[5]) / 2).toFixed(1), 'ms', size, 'bytes');
  }
  return { reads: out, cashierFolios: folios.status };
}

try {
  fs.mkdirSync(OUT, { recursive: true });
  await setup();
  const till = await tillId();
  const info = await simulate(till);
  log('seed done', JSON.stringify(info));
  const r = await timeReads(info);
  const cmdStats = Object.fromEntries(Object.entries(stats).map(([k, v]) => { v.sort((a, b) => a - b); return [k, { n: v.length, medianMs: +v[Math.floor(v.length / 2)].toFixed(1), p95Ms: +v[Math.floor(v.length * 0.95)].toFixed(1), maxMs: +v.at(-1).toFixed(1) }]; }));
  fs.writeFileSync(path.join(OUT, 'perf-results.json'), JSON.stringify({ start: START, info, ...r, callStats: cmdStats, wallSeconds: (Date.now() - t0) / 1000 }, null, 1));
  log('done');
} catch (e) { log('FATAL', e.stack); process.exitCode = 1; }
