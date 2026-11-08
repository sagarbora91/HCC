// F8C-FIT: the kitchen display (kds) and the PM calendar tab (maintenance > pm-calendar) are in every fit-check list and guard, and their page UI
// keeps the DEC-213 rule (lists scroll in their panel, a task never scrolls, no font shrinking, no hex colour). The browser run itself is
// tools/fit-screen-check.mjs (see 05 Delivery Evidence/F8C/F8C-FIT-DONE.md).
import {readFileSync} from 'node:fs';
const read=f=>readFileSync(f,'utf8');
const tag='[F8C-FIT]';
export function register({test,assert}){
  test(`${tag} fit-screen-check visits kds (PAGE_CATEGORY) and every maintenance sub-screen incl. pm-calendar`,()=>{
  const nav=read('src/app/navmap.ts'),pc=nav.slice(nav.indexOf('export const PAGE_CATEGORY'),nav.indexOf('};',nav.indexOf('export const PAGE_CATEGORY')));
  assert.ok(/(^|[,{\s])'?kds'?\s*:\s*'cash'/.test(pc),'kds listed in PAGE_CATEGORY, so ALL_PAGES of the fit check contains it');
  const subs=read('src/app/subscreens.ts');
  assert.ok(/\{\s*page:\s*'maintenance'\s*,\s*id:\s*'pm-calendar'/.test(subs),'pm-calendar is a registered sub-screen, so the fit check opens it');
  const chk=read('tools/fit-screen-check.mjs');
  assert.ok(chk.includes("opt('--theme')"),'the fit check can run in the dark and high-contrast themes');
  });
  test(`${tag} kds page: fill layout, board columns scroll inside their panels, 44/48 px targets, no font-size override, tokens only`,()=>{
  const src=read('src/features/pos/KdsPage.tsx');
  assert.ok(src.includes('layout="fill"')&&src.includes('BoardColumns'),'FitPage fill + BoardColumns');
  const css=read('src/styles/kds.css').replace(/\/\*[^]*?\*\//g,'');
  assert.ok(!/#[0-9a-f]{3,8}\b/i.test(css),'colours are tokens');
  assert.ok(/\.kds-bump\{[^}]*min-height:48px/.test(css),'bump button 48 px');
  assert.ok(!/\.kds-bump\{[^}]*font-size/.test(css),'bump button keeps the shared button size (fit check: button.btn fonts equal main)');
  assert.ok(/\.kds-seg button\{[^}]*min-height:44px/.test(css),'picker 44 px');
  assert.ok(/\.kds-card header\{[^}]*flex-wrap:wrap/.test(css),'ticket header wraps at large text');
  assert.ok(/\.kds-card\{[^}]*overflow-wrap:anywhere/.test(css),'long item names wrap inside the ticket');
  });
  test(`${tag} pm calendar: occurrence text wraps inside the day cell (never clipped), panel scrolls`,()=>{
  const src=read('src/features/maintenance/PmCalendar.tsx');
  assert.ok(src.includes("whiteSpace:'normal'")&&src.includes("overflowWrap:'anywhere'"),'occurrence button wraps');
  assert.ok(src.includes('minWidth:0')&&src.includes('<ScrollPanel'),'grid cell can shrink; the calendar lives in a ScrollPanel');
  assert.ok(!/font-?[sS]ize/.test(src),'no font-size in the page file');
  });

  test(`${tag} F8D-FIT: PM plan modal opener in fit-modals.json; fit tool has an opt-in phone viewport`,()=>{
  const m=JSON.parse(read('tools/fit-modals.json')).find(x=>x.page==='maintenance'&&x.tab==='pm-calendar');
  assert.ok(m&&m.selector.includes('Preventive maintenance calendar'),'pm-calendar occurrence opener present');
  const t=read('tools/fit-screen-check.mjs');
  assert.ok(/id:'phone'/.test(t)&&t.includes('!v.phone'),'phone viewport only on --viewports phone');
  });

}
