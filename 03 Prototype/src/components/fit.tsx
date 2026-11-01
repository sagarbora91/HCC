// FIT · fit-to-screen components for the desktop / tablet layout (DEC-194…196, APR-025; contract 02 Planning/agents/FIT-LAYOUT-CONTRACT.md §4, §11).
// Names and props are fixed by the contract; module lanes use these and never create their own variants. Styles: src/styles/z-fit.css.
// Fonts are never set here: every heading, label and value reuses the classes the old markup used (page-title, card, section-title,
// stat-label, money-stat …) so computed font sizes stay identical (DEC-195); only the page h1 drops to 22 px, in z-fit.css.
import React,{createContext,useContext,useEffect,useLayoutEffect,useRef,useState,useCallback} from 'react';
import {ArrowLeft,Check,CircleDot,Circle,Ban,MoreHorizontal} from 'lucide-react';
import {useStepBack} from '../app/native';

/** What the desktop shell (App.tsx) tells the pages: that this is the desktop layout, the open page, the tab asked for by go(page,{tab}), a
 *  counter that moves on every navigation (so the same page re-reads its tab), and where the selected tab's label goes (breadcrumb tab
 *  crumb). The phone shell does not provide it; the defaults (desktop false) make every hook below a plain local state there and keep
 *  stand-alone dialogs (ModalFrame) in their phone look. */
export type FitNav={desktop:boolean;page:string;tabKey:string;seq:number;setTabCrumb:(c:{page:string;label:string}|null)=>void};
export const FitNavContext=createContext<FitNav>({desktop:false,page:'',tabKey:'',seq:0,setTabCrumb:()=>{}});
export const useFitNav=()=>useContext(FitNavContext);

/** sessionStorage key of a page's selected sub-screen (lane fit-check's tool sets it before opening a page). */
export const fitTabKey=(page:string)=>'hcc-fit-tab:'+page;
const readTab=(page:string)=>{try{return sessionStorage.getItem(fitTabKey(page));}catch{return null;}};
const writeTab=(page:string,t:string)=>{try{sessionStorage.setItem(fitTabKey(page),t);}catch{/* storage unavailable */}};

/** Selected sub-screen of `page`: go(page,{tab}) first, then the one remembered in this browser session, else `fallback`.
 *  Unknown ids fall back. Switching writes sessionStorage and adds no history entry (Android Back leaves the page, as today). */
export function useFitTab<T extends string>(page:string,ids:readonly T[],fallback:T):[T,(t:T)=>void]{
  const nav=useFitNav();
  const pick=():T=>{
    const asked=nav.page===page?nav.tabKey:'';
    if(asked&&(ids as readonly string[]).includes(asked)){writeTab(page,asked);return asked as T;} // remembered, so a later plain go(page) keeps it
    const kept=readTab(page);
    return kept&&(ids as readonly string[]).includes(kept)?kept as T:fallback;
  };
  const [tab,setTab]=useState<T>(pick);
  // Every navigation to this page (go with or without a tab, Back / Forward) re-reads: the asked tab, else the remembered one.
  const first=useRef(true);
  useEffect(()=>{if(first.current){first.current=false;return;}if(nav.page===page)setTab(pick());},[nav.seq]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(()=>{if(!(ids as readonly string[]).includes(tab))setTab(fallback);},[ids.join('|')]); // eslint-disable-line react-hooks/exhaustive-deps
  const choose=useCallback((t:T)=>{writeTab(page,t);setTab(t);},[page]);
  return [tab,choose];
}

/** Inside a FitPage body: a FitTabs bar there is an inner (nested) bar — it never takes the breadcrumb tab crumb (contract §11.5). */
const InnerTabsContext=createContext(false);

/** Page root for every desktop page. Header row: h1 (22 px) + one-line sub (ellipsis, full text in title attr) + actions (right). */
export function FitPage(p:{
  title:string;sub?:string;
  actions?:React.ReactNode;          // primary buttons — always visible
  toolbar?:React.ReactNode;          // ONE row under the header: search, filters, date range, StatStrip; wraps to at most 2 rows below 900 px
  tabs?:React.ReactNode;             // a <FitTabs/> bar, rendered between toolbar and body
  layout?:'fill'|'split'|'board'|'steps'|'form'; // body layout, default 'fill'
  children:React.ReactNode;
}):React.JSX.Element{
  const nav=useFitNav();const layout=p.layout??'fill';
  return <section className={'fit-page fit-'+layout} data-fit-page={nav.page||undefined} aria-labelledby="fit-page-h1">
    <header className="page-title fit-page-head"><div className="fit-page-heading"><h1 id="fit-page-h1">{p.title}</h1>{p.sub&&<p className="fit-page-sub" title={p.sub}>{p.sub}</p>}</div>
      {p.actions&&<HeaderActions>{p.actions}</HeaderActions>}</header>
    {p.toolbar&&<div className="fit-page-toolbar">{p.toolbar}</div>}
    {p.tabs}
    <div className="fit-page-body"><InnerTabsContext.Provider value={true}>{p.children}</InnerTabsContext.Provider></div>
  </section>;
}

/** Top-level items of a node, fragments opened (the header's actions are usually passed as <>…</>). */
function flatItems(n:React.ReactNode):React.ReactNode[]{
  return React.Children.toArray(n).flatMap(x=>React.isValidElement(x)&&x.type===React.Fragment?flatItems((x.props as {children?:React.ReactNode}).children):[x]);
}
/** FitPage header actions (contract §11.9): one row that never wraps. The first item (the page's primary action) always stays; when the row
 *  does not fit, the trailing items move, in order, into a "More" menu under a More button. Re-measured when the header's width changes. */
function HeaderActions({children}:{children:React.ReactNode}){
  const items=flatItems(children);const total=items.length;
  const [n,setN]=useState(total);const [open,setOpen]=useState(false);const [,bump]=useState(0);
  const row=useRef<HTMLDivElement>(null);const full=useRef(true);const lastTotal=useRef(total);const lastW=useRef(-1);
  if(lastTotal.current!==total){lastTotal.current=total;full.current=true;}
  // Every render: start again from all items when asked (new items, new width), then drop one item at a time while the row overflows.
  useLayoutEffect(()=>{const el=row.current;if(!el)return;
    if(full.current){if(n!==total){setN(total);return;}full.current=false;}
    if(n>1&&el.scrollWidth>el.clientWidth+1)setN(n-1);
    else if(n>total)setN(total);
  });
  useEffect(()=>{const head=row.current?.parentElement;if(!head||typeof ResizeObserver==='undefined')return;
    const ro=new ResizeObserver(()=>{const w=Math.round(head.clientWidth);if(w===lastW.current)return;lastW.current=w;full.current=true;bump(x=>x+1);});
    ro.observe(head);return ()=>ro.disconnect();},[]);
  useEffect(()=>{if(!open)return;
    const off=(e:Event)=>{if(row.current&&!row.current.contains(e.target as Node))setOpen(false);};
    const esc=(e:KeyboardEvent)=>{if(e.key==='Escape')setOpen(false);};
    document.addEventListener('mousedown',off);document.addEventListener('keydown',esc);
    return ()=>{document.removeEventListener('mousedown',off);document.removeEventListener('keydown',esc);};},[open]);
  const shown=Math.min(n,total);const rest=items.slice(shown);
  if(!rest.length&&open)window.setTimeout(()=>setOpen(false),0);
  return <div className="fit-page-actions" ref={row}>
    {items.slice(0,shown)}
    {rest.length>0&&<div className="fit-more">
      <button type="button" className="btn fit-more-btn" aria-haspopup="true" aria-expanded={open} onClick={()=>setOpen(o=>!o)}><MoreHorizontal size={16}/>More</button>
      {open&&<div className="fit-more-pop" role="group" aria-label="More actions" onClickCapture={()=>window.setTimeout(()=>setOpen(false),0)}>{rest}</div>}
    </div>}
  </div>;
}

/** Card that takes all remaining height; header (title, count, actions) fixed; body scrolls; table heads sticky; ShowMore sits in its footer. */
export function ScrollPanel(p:{title?:React.ReactNode;actions?:React.ReactNode;footer?:React.ReactNode;label:string;children:React.ReactNode;grow?:number}):React.JSX.Element{
  const id=React.useId();const titled=p.title!==undefined; // the scroll body carries the label (it is the focusable part); the card points at its title
  return <section className="card fit-panel" aria-labelledby={titled?id:undefined} style={p.grow!==undefined?{flexGrow:p.grow}:undefined}>
    {(titled||p.actions)&&<div className="section-title fit-panel-head">{titled?<h2 id={id}>{p.title}</h2>:<span/>}{p.actions&&<div className="inline fit-panel-actions">{p.actions}</div>}</div>}
    <div className="fit-panel-body" data-fit-scroll="" tabIndex={0} aria-label={p.label}>{p.children}</div>
    {p.footer&&<div className="fit-panel-foot">{p.footer}</div>}
  </section>;
}

const NARROW='(max-width: 899.98px)';
function useNarrow(){
  const q=()=>typeof window!=='undefined'&&typeof window.matchMedia==='function'&&window.matchMedia(NARROW).matches;
  const [n,setN]=useState(q);
  useEffect(()=>{if(typeof window==='undefined'||typeof window.matchMedia!=='function')return;const m=window.matchMedia(NARROW);const on=()=>setN(m.matches);on();
    m.addEventListener?.('change',on);return ()=>m.removeEventListener?.('change',on);},[]);
  return n;
}

/** List + detail side by side. Below 900 px width the detail opens over the list as a slide-over with a Back button (never stacked below).
 *  Android Back closes the slide-over first (the app's step-back chain, app/native.ts). */
export function SplitView(p:{list:React.ReactNode;detail:React.ReactNode|null;empty:React.ReactNode;onCloseDetail:()=>void;ratio?:`${number}fr ${number}fr`;label:string}):React.JSX.Element{
  const narrow=useNarrow();const open=p.detail!==null&&p.detail!==undefined&&p.detail!==false;
  const close=useRef(p.onCloseDetail);close.current=p.onCloseDetail;
  useStepBack(narrow&&open,()=>close.current());
  const ratio=p.ratio??'2fr 3fr';const [a,b]=ratio.split(' ');
  // A record that opens takes the focus to its pane instead of a live region re-reading the whole record on every change.
  const pane=useRef<HTMLDivElement>(null);const was=useRef(open);
  useEffect(()=>{if(open&&!was.current)pane.current?.focus({preventScroll:true});was.current=open;},[open]);
  return <div className={'fit-split'+(open?' has-detail':'')} aria-label={p.label} role="group" style={{['--fit-split-cols' as string]:`minmax(0,${a}) minmax(0,${b})`}}>
    <div className="fit-split-list">{p.list}</div>
    <div className="fit-split-detail">
      {open&&<button type="button" className="btn fit-split-back" onClick={()=>close.current()}><ArrowLeft size={16}/>Back to list</button>}
      <div className="fit-split-pane" ref={pane} data-fit-scroll="" tabIndex={0} aria-label={p.label+' — record'}>{open?p.detail:p.empty}</div>
    </div>
  </div>;
}

/** Sub-screens (DEC-196). Tab bar ≥ 48 px on touch; scrolls sideways if it overflows; role=tablist, arrow keys, Home/End.
 *  Selected tab: remembered per page in sessionStorage `hcc-fit-tab:<page>`; `go(page,{tab})` opens a given tab (existing ctx.go option).
 *  Switching tabs uses no history entry (Android Back leaves the page, as today). */
export function FitTabs<T extends string>(p:{page:string;tabs:{id:T;label:string;count?:number;badge?:'warn'|'danger'}[];value:T;onChange:(t:T)=>void}):React.JSX.Element{
  const nav=useFitNav();const bar=useRef<HTMLDivElement>(null);
  const label=p.tabs.find(t=>t.id===p.value)?.label??'';
  // Only the page's own bar (the FitPage "tabs" slot) names the breadcrumb tab crumb; a nested bar inside the body does not (§11.5).
  const inner=useContext(InnerTabsContext);const setCrumb=nav.setTabCrumb;
  useEffect(()=>{if(!inner)setCrumb(label?{page:p.page,label}:null);},[p.page,label,setCrumb,inner]);
  useEffect(()=>()=>{if(!inner)setCrumb(null);},[setCrumb,inner]);
  useEffect(()=>{bar.current?.querySelector<HTMLElement>('[aria-selected="true"]')?.scrollIntoView?.({block:'nearest',inline:'nearest'});},[p.value]);
  const onKey=(e:React.KeyboardEvent)=>{
    const i=p.tabs.findIndex(t=>t.id===p.value);let j=-1;
    if(e.key==='ArrowRight')j=(i+1)%p.tabs.length;else if(e.key==='ArrowLeft')j=(i-1+p.tabs.length)%p.tabs.length;
    else if(e.key==='Home')j=0;else if(e.key==='End')j=p.tabs.length-1;
    if(j<0)return;e.preventDefault();p.onChange(p.tabs[j].id);
    window.setTimeout(()=>bar.current?.querySelector<HTMLElement>(`[data-tab="${p.tabs[j].id}"]`)?.focus(),0);
  };
  return <div className="fit-tabs-bar tabs" role="tablist" aria-label="Sections of this page" ref={bar} onKeyDown={onKey}>
    {p.tabs.map(t=><button key={t.id} type="button" role="tab" id={`fit-tab-${p.page}-${t.id}`} data-tab={t.id} aria-selected={t.id===p.value} tabIndex={t.id===p.value?0:-1}
      className={t.id===p.value?'active':''} onClick={()=>p.onChange(t.id)}>{t.label}
      {t.count!==undefined&&<span className={'fit-tab-count'+(t.badge?' '+t.badge:'')}>{t.count}</span>}</button>)}
  </div>;
}

/** One-row KPI strip for the toolbar. Same Stat font sizes; less padding; scrolls sideways if too many (never wraps). */
export function StatStrip(p:{items:{title:string;value:string;sub?:string;tone?:'ok'|'warn'|'danger'}[]}):React.JSX.Element{
  return <div className="fit-stats" role="list" aria-label="Key figures">{p.items.map((x,i)=><div role="listitem" key={i} className={'stat fit-stat'+(x.tone?' '+x.tone:'')}>
    <div className="stat-label">{x.title}</div><strong className="money-stat">{x.value}</strong>{x.sub&&<small>{x.sub}</small>}</div>)}</div>;
}

/** Columns that each scroll on their own (Housekeeping, Maintenance board, POS tickets). */
export function BoardColumns(p:{columns:{id:string;title:string;count?:number;body:React.ReactNode}[];minColumn?:number}):React.JSX.Element{
  return <div className="fit-board" style={{['--fit-col' as string]:(p.minColumn??240)+'px'}}>
    {p.columns.map(c=><section key={c.id} className="card fit-board-col" aria-label={c.title}>
      <div className="section-title fit-panel-head"><h2>{c.title}</h2>{c.count!==undefined&&<span className="muted">{c.count}</span>}</div>
      <div className="fit-panel-body" data-fit-scroll="" tabIndex={0} aria-label={c.title}>{c.body}</div></section>)}
  </div>;
}

const STEP_ICON={done:Check,current:CircleDot,waiting:Circle,blocked:Ban} as const;
const STEP_WORD={done:'done',current:'current step',waiting:'waiting',blocked:'blocked'} as const;
/** Wizard / checklist: step rail on the left (status icon + label + count), one step body on the right (Day closing, handover runs). */
export function StepFlow(p:{steps:{id:string;label:string;status:'done'|'current'|'waiting'|'blocked';note?:string}[];value:string;onChange:(id:string)=>void;children:React.ReactNode}):React.JSX.Element{
  return <div className="fit-steps">
    <ol className="fit-step-rail" aria-label="Steps">{p.steps.map((x,i)=>{const Icon=STEP_ICON[x.status];return <li key={x.id}>
      <button type="button" className={'fit-step '+x.status+(x.id===p.value?' on':'')} aria-current={x.id===p.value?'step':undefined} onClick={()=>p.onChange(x.id)}>
        <span className="fit-step-ic" aria-hidden="true"><Icon size={16}/></span>
        <span className="fit-step-text"><strong>{i+1}. {x.label}</strong><small>{x.note?x.note+' · ':''}{STEP_WORD[x.status]}</small></span></button></li>;})}</ol>
    <div className="fit-step-body">{p.children}</div>
  </div>;
}

/** Long settings forms: section list on the left, one section on the right, Save bar sticky at the bottom. */
export function FormSections(p:{sections:{id:string;label:string;dirty?:boolean}[];value:string;onChange:(id:string)=>void;actions:React.ReactNode;children:React.ReactNode}):React.JSX.Element{
  return <div className="fit-form">
    <nav className="fit-form-nav" aria-label="Form sections"><ul>{p.sections.map(x=><li key={x.id}>
      <button type="button" className={x.id===p.value?'on':''} aria-current={x.id===p.value?'true':undefined} onClick={()=>p.onChange(x.id)}>
        {x.label}{x.dirty&&<span className="fit-dirty" aria-label="unsaved changes">•</span>}</button></li>)}</ul></nav>
    <div className="fit-form-main">
      <div className="fit-form-body" data-fit-scroll="" tabIndex={0} aria-label={(p.sections.find(x=>x.id===p.value)?.label??'Section')+' — fields'}>{p.children}</div>
      <div className="modal-actions fit-form-actions">{p.actions}</div>
    </div>
  </div>;
}

/** One-line note with a "More" button that opens the full text in place (contract §11.4). Touch-reachable: never a title-only tooltip.
 *  Use it for footnotes in a ScrollPanel footer and for collapsed info notes. "lines" is the collapsed height (1 by default). */
export function FitNote(p:{text:React.ReactNode;lines?:number}):React.JSX.Element{
  const [open,setOpen]=useState(false);const [long,setLong]=useState(true);const t=useRef<HTMLParagraphElement>(null);
  // "More" only when the text is cut (measured after layout; text size and width changes re-measure).
  useEffect(()=>{const el=t.current;if(!el||open)return;const m=()=>setLong(el.scrollWidth>el.clientWidth+1||el.scrollHeight>el.clientHeight+1);m();
    if(typeof ResizeObserver==='undefined')return;const ro=new ResizeObserver(m);ro.observe(el);return ()=>ro.disconnect();},[open,p.text]);
  const lines=p.lines??1;
  return <div className={'fit-note footnote'+(open?' open':'')}>
    <p className="fit-note-text" ref={t} style={lines>1&&!open?{whiteSpace:'normal',display:'-webkit-box',WebkitLineClamp:lines,WebkitBoxOrient:'vertical'}:undefined}>{p.text}</p>
    {(long||open)&&<button type="button" className="text-btn fit-note-more" aria-expanded={open} onClick={()=>setOpen(o=>!o)}>{open?'Less':'More'}</button>}
  </div>;
}

/** A dialog's frame. Two uses (contract §11.1):
 *  - inside ModalHost (no "dialog" prop): ModalHost already gives the scrolling .modal-body, so this only lays out a heading, the content
 *    and a sticky .modal-actions bar — plain wrappers, no second scroll box or tab stop;
 *  - a stand-alone dialog rendered outside ModalHost ("dialog" prop: Day pack, server Day pack, guest-ID dialog): it renders the overlay and
 *    the .modal shell itself. On the desktop layout the shell is ".modal fit-modal" with the scrolling .modal-body; on the phone layout (no
 *    desktop shell) the markup is exactly the dialog's old one, so its phone look is unchanged. In this use "heading" and "actions" are
 *    rendered as given (pass the full elements, e.g. the dialog's own <div className="modal-actions no-print">). */
export type FitDialog={label:string;onClose:()=>void;className?:string;close?:React.ReactNode;tight?:boolean;
  attrs?:React.HTMLAttributes<HTMLDivElement>&{ref?:React.Ref<HTMLDivElement>}};
export function ModalFrame(p:{heading:React.ReactNode;actions:React.ReactNode;children:React.ReactNode;dialog?:FitDialog}):React.JSX.Element{
  const {desktop}=useFitNav();const d=p.dialog;
  if(!d)return <div className="fit-modal-frame">
    {p.heading!=null&&p.heading!==false&&<div className="modal-heading">{p.heading}</div>}
    <div className="fit-modal-frame-body">{p.children}</div>
    <div className="modal-actions">{p.actions}</div>
  </div>;
  const cls='modal'+(d.className?' '+d.className:'')+(desktop?' fit-modal'+(d.tight?' fit-modal-tight':''):'');
  return <div className={'modal-overlay'+(desktop?' fit-modal-overlay':'')} onMouseDown={e=>{if(e.target===e.currentTarget)d.onClose();}}>
    <div {...d.attrs} className={cls} role="dialog" aria-modal="true" aria-label={d.label}>
      {d.close}
      {desktop?<div className="modal-body" data-fit-scroll="" tabIndex={0} aria-label="Dialog content">{p.heading}{p.children}{p.actions}</div>
        :<>{p.heading}{p.children}{p.actions}</>}
    </div></div>;
}
