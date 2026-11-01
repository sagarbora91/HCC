# FIT component guide — how a module lane converts a desktop page (wave `claude/fit`, lane fit-shell, 1 Nov 2026)

Read with `FIT-LAYOUT-CONTRACT.md` (the rules win over this guide) and the converted reference page `arrivals` (`03 Prototype/src/features/guests/CheckIn.tsx`, `ArrivalsPage`). Components: `03 Prototype/src/components/fit.tsx`; styles: `src/styles/z-fit.css` (shell-owned — lane CSS goes in the feature's own stylesheet, guarded with `.app-shell`).

## What the shell already does for you

- **Height chain.** `.content` is a flex column that never scrolls. Your page root must be **one element**: the `<FitPage>` section. Until then the shell marks the page's own root `data-fit-scroll="legacy"` (no wrapper, so the page never remounts) and it scrolls there; a page that still returns a fragment makes `.content` itself the legacy scroller. The check reports both as `not-converted` (contract §11.3).
- **Page heading.** `FitPage` renders `.page-title` markup, so the `h1` is 22 px and every other text keeps its old size. Do not set any `font-size` in your CSS (DEC-195). Never put a `PageTitle` inside a `FitPage`.
- **Dialogs.** Every desktop modal opened through `open({type})` gets the class `.fit-modal` (desktop layout only), a scrolling `.modal-body` and a sticky `.modal-actions` bar. Keep rendering `.modal-heading` … `.modal-actions` as today; nothing to wrap. Never write CSS for a bare `.modal` (phone sheets and the phone Day pack share it): desktop dialog rules target `.modal.fit-modal` (§11.1). `ModalFrame` with its `dialog` prop is only for a dialog you render yourself outside `ModalHost` (as `DayPack`, `ServerDayPack` and the guest-ID dialog do): it renders the overlay + `.modal fit-modal` shell on the desktop layout and the dialog's old markup on the phone. Pass `heading` and `actions` as complete elements there (e.g. `<div className="modal-actions no-print">…</div>`). Wide forms (≥ 8 fields): put the fields in `<div className="form fit-form-2col">` (two columns at ≥ 900 px; `.full` spans both).
- **Breadcrumb + search.** The page's own `FitTabs` (passed in the `FitPage` `tabs` slot) shows the selected tab as the last crumb; a nested `FitTabs` inside the page body (e.g. Customers 360's Overview / Stays / …) never takes the crumb and is not in `SUB_SCREENS` (§11.5). Your `SUB_SCREENS` lines make each page tab a search result "Page › Tab" — on the desktop layout only (§11.2).
- **Header actions.** `FitPage` `actions` = the primary action **first**, then the rest. The row never wraps: what does not fit moves into a **More** menu (§11.9). The 56 px top bar shows the user button on touch tablets too (§11.8).
- **Touch sizes.** Buttons, inputs and tabs get the DEC-060 minimums from `z-tablet.css` / `z-fit.css`; do not override `min-height`.

## Rules of thumb

- Each `ScrollPanel` / `SplitView` pane / board column must be **≥ 160 px tall** at 1138 × 640 (check 4, `cramped`). Two stacked panels is the most a body holds at that size; use `fit-cols` (side by side) or `FitTabs` for more.
- A tab bar, toolbar or `StatStrip` never gets `data-fit-scroll` (they may scroll sideways, but the check would call them cramped).
- Lists that render every row adopt `usePaged` + `ShowMore`; the `ShowMore` goes in the panel `footer`.
- Footnotes go in the panel `footer` as `<FitNote text="…"/>`: one line plus a **More** button that opens the full text in place (§11.4; a `title`-only tooltip is not allowed, touch cannot reach it). Long info notes (`.info-note`): `FitNote` too (`lines={2}` if needed) or the tab's empty area. Text unchanged.
- A row that opens a modal today keeps the modal.
- Layout only: never touch selectors, commands, handlers, permissions, fixtures or money code.

## Utility classes (z-fit.css)

| Class | Use |
|---|---|
| `fit-cols` | Two columns of panels inside a body: main `3fr`, side `minmax(240px, 1fr)`; each child is a flex column. Below 900 px the columns stack as grid rows of at least 160 px each (a column with two panels splits its row), so a table panel never collapses (§11.11). Pattern: `<div className="fit-cols"><div>{table}</div><div>{sideA}{sideB}</div></div>`. |
| `fit-stack` | A plain column of panels (gap 12 px) when a body needs a wrapper. |
| `fit-form-2col` | Two field columns in a wide dialog form at ≥ 900 px. |

## 1. Fill — one list (the `arrivals` pattern)

```tsx
import {FitPage,ScrollPanel,StatStrip,FitNote} from '../../components/fit';
export function CashbookPage(){
  const {s,search,setSearch}=useHcc();
  const {rows,more}=usePaged(list);                       // existing hook, unchanged
  return <FitPage title="Cash book" sub={`Business date ${shortDate(s.date)} · every cash movement`}
      actions={<button className="btn primary" onClick={()=>open({type:'cash'})}>Record cash</button>}
      toolbar={<><SearchBox value={search} onChange={setSearch} placeholder="Search…"/>
        <StatStrip items={[{title:'Opening',value:money(o)},{title:'In',value:money(i)},{title:'Closing',value:money(c),tone:'ok'}]}/></>}>
    <ScrollPanel label="Cash movements" footer={<>{more}<FitNote text={NOTE}/></>}>
      <div className="table-wrap"><table>…unchanged rows…</table></div>
    </ScrollPanel>
  </FitPage>;
}
```

`thead th` is sticky inside the panel; `.table-wrap` stops being its own scroller there.

## 2. Split — list whose rows open a record

```tsx
import {FitPage,ScrollPanel,SplitView} from '../../components/fit';
export function FoliosPage(){
  const [sel,setSel]=useState<string|null>(null);
  const b=sel?s.bookings.find(x=>x.id===sel):undefined;
  return <FitPage title="Folios" sub="Open folios" layout="split" toolbar={<SearchBox …/>}>
    <SplitView label="Folios" ratio="2fr 3fr" onCloseDetail={()=>setSel(null)}
      list={<ScrollPanel label="Folio list" footer={more}>
        <ul className="fol-list">{rows.map(x=><li key={x.id}><button className={x.id===sel?'active':''} onClick={()=>setSel(x.id)}>…</button></li>)}</ul>
      </ScrollPanel>}
      detail={b?<FolioRecord b={b}/>:null}
      empty={<Empty title="Pick a folio" body="Its charges, payments and actions open here."/>}/>
  </FitPage>;
}
```

The record pane scrolls itself (`data-fit-scroll`). Below 900 px the record slides over the list with **Back to list**; Android Back closes it first. Keep every action the full-width record had.

## 3. Tabs — several sections (sub-screens, DEC-196)

```tsx
import {FitPage,FitTabs,useFitTab,ScrollPanel} from '../../components/fit';
const TABS=[{id:'all',label:'All'},{id:'pending',label:'Pending'},{id:'paid',label:'Paid'}] as const;
type Tab=typeof TABS[number]['id'];
export function ExpensesPage(){
  const [tab,setTab]=useFitTab<Tab>('expenses',TABS.map(t=>t.id),'pending');   // replaces the old useState tab, same ids
  return <FitPage title="Expenses" sub="…" actions={…}
      tabs={<FitTabs page="expenses" value={tab} onChange={setTab}
        tabs={TABS.map(t=>({...t,count:counts[t.id],badge:t.id==='pending'&&counts.pending?'warn':undefined}))}/>}>
    {tab==='pending'?<PendingSplit/>:<ScrollPanel label={`${tab} expenses`}>…</ScrollPanel>}
  </FitPage>;
}
```

`subscreens.ts`, inside your block only:

```ts
  // FIT-CASH
  {page:'expenses',id:'all',label:'All'},
  {page:'expenses',id:'pending',label:'Pending',keywords:['awaiting approval']},
  {page:'expenses',id:'paid',label:'Paid'},
```

`go('expenses',{tab:'paid'})` opens a tab; the choice is remembered for the browser session (`sessionStorage hcc-fit-tab:expenses`); switching tabs adds no history entry. A changed default tab goes in your DONE file (old → new).

## 4. Board — columns that scroll on their own

```tsx
import {FitPage,BoardColumns,StatStrip} from '../../components/fit';
export function HousekeepingPage(){
  return <FitPage title="Housekeeping" sub="Room cleaning board" layout="board"
      toolbar={<><FloorFilter/><StatStrip items={stats}/></>}>
    <BoardColumns minColumn={240} columns={STATUSES.map(st=>({id:st,title:st,count:by[st].length,
      body:<ul className="kanban-list">{by[st].map(t=><TaskCard key={t.id} t={t}/>)}</ul>}))}/>
  </FitPage>;
}
```

More columns than fit → the board scrolls sideways; each column body scrolls down. A room grid instead of columns: one `ScrollPanel` holding the grid.

## 5. Steps — wizard / closing / checklist run

```tsx
import {FitPage,StepFlow,ScrollPanel} from '../../components/fit';
export function DayClosingPage(){
  const [step,setStep]=useFitTab('closing',STEP_IDS,firstOpenStep);
  return <FitPage title="Day closing" sub={…} layout="steps"
      actions={<button className="btn primary" disabled={!canClose /* rule unchanged */} onClick={close}>Close day</button>}>
    <StepFlow value={step} onChange={setStep}
      steps={STEPS.map(x=>({id:x.id,label:x.label,status:x.done?'done':x.blocked?'blocked':x.id===step?'current':'waiting',note:x.count?`${x.count} left`:undefined}))}>
      <ScrollPanel title={STEPS.find(x=>x.id===step)!.label} label="Step detail">{bodyOf(step)}</ScrollPanel>
    </StepFlow>
  </FitPage>;
}
```

The committing action sits in the page header (`actions`), always visible, enabled exactly as before. Step ids go in `SUB_SCREENS` when the step is a reachable sub-screen.

## 6. Form — long settings form

```tsx
import {FitPage,FormSections} from '../../components/fit';
export function PrefsPage(){
  const [sec,setSec]=useFitTab('prefs',SECTION_IDS,'text');
  return <FitPage title="Appearance & display" sub="This device only" layout="form">
    <FormSections value={sec} onChange={setSec}
      sections={SECTIONS.map(x=>({id:x.id,label:x.label,dirty:dirty.has(x.id)}))}
      actions={<><button className="btn" onClick={reset}>Reset</button><button className="btn primary" onClick={save}>Save</button></>}>
      <div className="form">{fieldsOf(sec)}</div>
    </FormSections>
  </FitPage>;
}
```

The section list sits left, one section scrolls on the right, the Save bar (`.modal-actions.fit-form-actions`) never scrolls away. Below 900 px the section list becomes one row above the fields.

## Tests and the check

- `tests/stream-fit-<lane>.mjs`, names `[FIT-<LANE>]`: each converted page renders a `fit-page` root (render with the harness in `tests/stream-fit-shell.mjs`: `HccContext` + `FitNavContext` providers with `{desktop:true,page,tabKey,seq,setTabCrumb}`), its `useFitTab` ids equal its `SUB_SCREENS` lines, defaults as you set them.
- A test that read text now in a non-default tab: render with `FitNavContext` `{page, tabKey:'<tab>'}` (or set `sessionStorage hcc-fit-tab:<page>`) — never delete or loosen it.
- Browser check: build, `PORT=<4190+n> node serve.mjs`, then through the lock `node tools/fit-screen-check.mjs --pages <your pages>`.
