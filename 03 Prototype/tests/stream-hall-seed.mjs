// F6 H2 · hall seed + import templates (contract §7 migration 4349, §8). Names prefixed [HSEED].
import {readFileSync} from 'node:fs';
export function register({test,M,assert}){
  const DIR='../08 Production/deployment/import-templates/';
  test('[HSEED] seed shape: halls with slots + rates, three T&C templates with the ten headings, all placeholders, effective 1 April 2026',()=>{
    const x=M.hallSeed();assert.ok(x.venues.every(v=>v.slots.length&&v.slots.every(s=>Number.isSafeInteger(s.ratePaise))));
    const t=M.hallTncSeed();assert.deepEqual(t.map(t=>t.id),['T-GEN','T-WEDDING','T-CORPORATE']);
    for(const tpl of t){const v=tpl.versions[0];assert.equal(v.effectiveFrom,'2026-04-01');assert.deepEqual(v.clauses.map(c=>c.heading),[...M.HALL_TNC_HEADINGS]);
      assert.deepEqual(v.clauses.map(c=>c.n),[1,2,3,4,5,6,7,8,9,10]);assert.ok(M.hallHasPlaceholder(v.clauses));}
    assert.notEqual(M.hallTncSeed(),M.hallTncSeed(),'a fresh copy each call');
  });
  test('[HSEED] halls.csv = the seed halls (one row per hall × slot) and round-trips',()=>{
    const csv=readFileSync(DIR+'halls.csv','utf8');assert.equal(csv,M.hallVenuesToCsv(M.hallSeed().venues));
    const r=M.hallVenuesFromCsv(csv);assert.deepEqual(r.errors,[]);assert.deepEqual(r.rows,M.hallSeed().venues);
  });
  test('[HSEED] tnc-templates.csv = the seed templates and round-trips (quoted commas / quotes / new lines survive)',()=>{
    const csv=readFileSync(DIR+'tnc-templates.csv','utf8');assert.equal(csv,M.hallTncToCsv(M.hallTncSeed()));
    assert.deepEqual(M.hallTncFromCsv(csv).rows,M.hallTncSeed());
    const odd=[{id:'T-X',eventType:'OTHER',versions:[{v:1,effectiveFrom:'2026-11-01',clauses:[{n:1,heading:'Pay, then "book"',text:'Line one,\nline two'}]}]}];
    assert.deepEqual(M.hallTncFromCsv(M.hallTncToCsv(odd)).rows,odd);
  });
  test('[HSEED] CSV import refuses a wrong header, formula cells and bad rows (line numbers)',()=>{
    assert.match(M.hallTncFromCsv('a,b\n').errors[0].message,/first line/);
    const bad=M.hallTncFromCsv(M.HALL_TNC_CSV.join(',')+'\nT-X,OTHER,1,2026-11-01,1,=cmd(),x\nT-X,NOPE,1,2026-11-01,1,H,t\n');
    assert.deepEqual(bad.errors.map(e=>e.line),[2,3]);assert.match(bad.errors[0].message,/formula/);
    const v=M.hallVenuesFromCsv(M.HALL_VENUE_CSV.join(',')+'\nV-A,A,HALL,HALL_RENT,,,,,,,,EVENING,18:00,23:00,100,\nV-A,A,HALL,HALL_RENT,,,,,,,,EVENING,18:00,23:00,100,\n');
    assert.match(v.errors[0].message,/twice/);assert.equal(v.rows.length,1);
  });
}
