// App shell: state, the single run() command path, navigation, header and demo toolbar.
import React,{useState,useRef,useMemo,useCallback,useEffect,useLayoutEffect} from 'react';
import {Search,Hotel,Menu,CalendarDays,AlertCircle,CheckCircle2,X,Clock} from 'lucide-react';
import {useMetrics} from './useMetrics';
import {State,Command,Permission,Room,addDays,can,roomStatus as statusOf,errorTitle,codeOf,assignedScopeRoles,isDayClosed} from '../services/mock';
import {executeWithFault,FaultMode,LATENCY_MS} from '../services/mock/faults';
import {HccContext,Ctx,Modal,RunOptions} from './context';
import {loadStored,saveStored,onStorageError} from './storage';
import {generateScenario,type GenProgress} from './scenarioGen';
import type {Restored} from '../services/mock';
import {navItems} from './nav';
import {HOME_PAGE,parseCatPage,navFilterModules,pagesOfCategory} from './navmap';
import {Breadcrumb,ModulesHome,CategoryPage,SignIn} from './ModulesHome';
import {DemoPill} from './demo/DemoToolbar';
import {FitNavContext,type FitNav} from '../components/fit'; // FIT (DEC-194…196): sub-screen tab + breadcrumb tab crumb for desktop pages
import {useReadOnly,READ_ONLY_HINT} from '../clients/react';
import {ProductionApp,SyncStatus,type ServerBinding} from '../clients/sync/react'; // production build only (W4-0 server sync layer)
import {extraPages,mobilePages,extraModals} from './registry';
import {ModalHost} from './ModalHost';
import {useDisplayMode,DisplayToggle} from './display';
import {SearchPalette} from './SearchPalette';
import {DeskAlerts} from './DeskAlerts'; // LF-ALERTS
import {shortcutFor,shortcutAllowed} from './shortcuts'; // LF-ALERTS
import {NavDrawer} from './NavDrawer';
import {isNative,decideBack,hasStepBack,runStepBack,listenBackButton,styleStatusBar,listenStatusBarRestyle,exitApp,clearExports} from './native';
import {MobileShell} from './mobile/MobileShell';
import {announceTab,type MobileTab} from './mobile/tabs';
import {Empty,ExitAsk,DiscardAsk,dateLabel,clockTime} from '../components/ui';
import {Overview} from '../features/overview/Overview';
import {RoomBoard} from '../features/rooms/RoomBoard';
import {Reservations} from '../features/reservations/Reservations';
import {Housekeeping} from '../features/housekeeping/Housekeeping';
import {Expenses} from '../features/expenses/Expenses';
import {DayClosing} from '../features/closing/DayClosing';
import {Reports} from '../features/reports/Reports';
import {AccessControl} from '../features/admin/AccessControl';
import {HotelSettings} from '../features/admin/HotelSettings';
import {useApplyPrefs,usePrefs,toastMs,resolveStartPage} from './prefs'; // CP-1 personal preferences (this device)
import {TextSizeQuick} from '../features/prefs/parts';

const SESSION_KEY='hcc-session'; // staff picked on this device (per device, not hotel data)
const readSession=(s:State)=>{try{const id=localStorage.getItem(SESSION_KEY);return s.users.find(u=>u.id===id&&u.enabled)?id:null;}catch{return null;}};
const writeSession=(id:string|null)=>{try{if(id)localStorage.setItem(SESSION_KEY,id);else localStorage.removeItem(SESSION_KEY);}catch{/* storage unavailable */}};
const newKey=(type:string)=>type+':'+(globalThis.crypto?.randomUUID?.()??Math.random().toString(36).slice(2));
const sleep=(ms:number)=>new Promise(r=>setTimeout(r,ms));

/** Production build (W4-0): server sign-in, then the same shell over the server state (sync layer). Prototype: demo state. */
export function App(){
  useApplyPrefs(); // CP-1: --hcc-text, data-density, data-motion on <html> from boot (loading and sign-in screens too)
  return process.env.HCC_BUILD==='production'?<ProductionApp render={b=><AppShell initial={{state:b.state,reset:false}} server={b}/>}/>:<PrototypeApp/>;
}

/** Loads the saved (or default) demo state from IndexedDB first, then shows the app (plan H-05, A3). */
function PrototypeApp(){
  const [initial,setInitial]=useState<Restored|null>(null);
  const [progress,setProgress]=useState<GenProgress|null>(null);
  useEffect(()=>{let live=true;loadStored(p=>{if(live)setProgress(p);}).then(r=>{if(live)setInitial(r);});return()=>{live=false;};},[]);
  if(!initial)return <BootLoading progress={progress}/>;
  return <AppShell initial={initial}/>;
}

/** First-run loading screen: shows generation progress while the FY26 history is built in a worker (audit P-01). */
function BootLoading({progress}:{progress:GenProgress|null}){
  const pct=progress?Math.round(progress.done*100):0;
  return <div className="boot-loading" role="status" aria-live="polite"><Hotel size={28} aria-hidden="true"/><strong>Loading demo data…</strong>
    <span>{progress?`First start on this device: ${progress.step} (${pct}%)`:'Preparing the fictional hotel records on this device.'}</span>
    {progress&&<progress max={100} value={pct} aria-label="Demo data generation progress" style={{width:'min(320px,80vw)',accentColor:'#7A1F2B'}}/>}</div>;
}

function AppShell({initial,server}:{initial:Restored;/** Production only: state, user and command path come from the server sync layer. */server?:ServerBinding}){
  const [sLocal,setS]=useState<State>(initial.state);
  const s=process.env.HCC_BUILD==='production'&&server?server.state:sLocal;
  const stateRef=useRef(s);stateRef.current=s;
  const [uidLocal,setUid]=useState(()=>readSession(initial.state)??'owner');
  const uid=process.env.HCC_BUILD==='production'&&server?server.uid:uidLocal;
  const [signedInLocal,setSignedIn]=useState(()=>!!readSession(initial.state));
  const signedIn=process.env.HCC_BUILD==='production'&&server?true:signedInLocal;
  const prefsApi=usePrefs();const prefsRef=useRef(prefsApi.prefs);prefsRef.current=prefsApi.prefs;
  // CP-1 C1: start page after sign-in = the device preference when this user may open it, else Modules Home.
  const startFor=(st:State,id:string)=>resolveStartPage(st.users.find(u=>u.id===id)?.role??'',prefsRef.current.startPage,navFilterModules(st,navItems,id).filter(n=>can(st,id,n.p)).map(n=>n.id));
  const [page,setPage]=useState(()=>signedIn?startFor(s,uid):HOME_PAGE);
  const [modal,setModal]=useState<Modal>(null);
  const [notice,setNotice]=useState<{text:string;error?:boolean}|null>(initial.reset?{text:initial.reason!,error:false}:null);
  const [error,setError]=useState('');
  const [search,setSearch]=useState(''),[filter,setFilter]=useState('All'),[tab,setTab]=useState('Arrivals');
  const [mobile,setMobile]=useState(false);
  const [fault,setFault]=useState<FaultMode>('none');
  const [busy,setBusy]=useState(false);const busyRef=useRef(false);
  const offline=useReadOnly(); // APK12-5: production only (the prototype build is never read-only)
  const toastTimer=useRef<number|undefined>(undefined);
  const display=useDisplayMode();
  const [tabKey,setTabKey]=useState('');
  const [navSeq,setNavSeq]=useState(0); // FIT: moves on every navigation so a page re-reads its sub-screen (go(page,{tab}), Back / Forward)
  const [tabCrumb,setTabCrumb]=useState<{page:string;label:string}|null>(null); // FIT: selected FitTabs label → breadcrumb
  const [depth,setDepth]=useState(0); // HCC page entries pushed on top of the first one (Android back)
  // T-05: the depth lives in each history entry (idx), so it can never drift from the real stack.
  const histIdx=()=>{try{const st=history.state as {idx?:number}|null;return typeof st?.idx==='number'?st.idx:0;}catch{return 0;}};
  const popSeen=useRef(false);
  const backTimer=useRef<number|undefined>(undefined);
  const cancelBackFallback=()=>{window.clearTimeout(backTimer.current);backTimer.current=undefined;};
  useEffect(()=>()=>window.clearTimeout(backTimer.current),[]);
  const modalRef=useRef<Modal>(null);modalRef.current=modal;
  const modalPushed=useRef(false),dirty=useRef(false),skipPop=useRef(false);
  // V15-2: `dirty` is the open sheet's unsaved input; `pageDirty` is an inline form's (tablet / desktop masters detail pane, no sheet).
  // setDirty() lands on whichever is on screen when it is called; a sheet's input is never confused with the page behind it.
  const pageDirty=useRef(false),pendingNav=useRef<(()=>void)|null>(null);
  const setDirty=useCallback((v:boolean)=>{(modalRef.current?dirty:pageDirty).current=v;},[]);
  useEffect(()=>{if(process.env.HCC_BUILD==='production')return; // production never uses the prototype's demo storage
    onStorageError(()=>setNotice({text:'Browser storage is unavailable. Changes will last only until this page closes.',error:true}));},[]);

  const user=s.users.find(u=>u.id===uid)??s.users[0];
  const m=useMetrics(s,uid);
  const allowedIds=navFilterModules(s,navItems,uid).filter(n=>can(s,uid,n.p)).map(n=>n.id);
  // V11-7: a module page left over from another role (Back after a role switch) with nothing for this user shows the role's home instead
  const catOk=(()=>{const c=parseCatPage(page);return !c||pagesOfCategory(c).some(p=>allowedIds.includes(p));})();
  const current=page===HOME_PAGE||(parseCatPage(page)&&catOk)?page:page==='more'&&display.isMobile?'more':allowedIds.includes(page)?page:HOME_PAGE;

  const toast=useCallback((text:string,isError=false)=>{setNotice({text,error:isError});window.clearTimeout(toastTimer.current);const ms=toastMs(prefsRef.current.toastSeconds);if(ms!==null)toastTimer.current=window.setTimeout(()=>setNotice(null),ms);},[]); // CP-1 E1: duration from prefs (6.5 s default)
  function persist(next:State){setS(next);stateRef.current=next;if(!saveStored(next))setNotice({text:'Browser storage is unavailable. Changes will last only until this page closes.',error:true});}
  const open=(x:{type:string;id?:string;room?:string})=>{
    if(offline&&x.type==='new'){toast(READ_ONLY_HINT,true);return;} // APK12-5: no new booking while the server is unreachable
    cancelBackFallback();setError('');dirty.current=false;
    if(!modalPushed.current){try{history.pushState({hcc:'modal',page,idx:histIdx()},'');modalPushed.current=true;}catch{/* history unavailable */}}
    setModal({...x,key:newKey(x.type)});};
  // V14-1: unsaved sheet text asks through the app's own themed DiscardAsk dialog (never the browser's confirm()); Android Back while it is open = Keep editing.
  const [discardAsk,setDiscardAsk]=useState(false);
  // Discard = close the sheet, or (V15-2) finish the navigation the dirty inline form held back; Keep editing = stay, forget that navigation.
  const discardSheet=()=>{setDiscardAsk(false);dirty.current=false;pageDirty.current=false;setModal(null);setError('');const nav=pendingNav.current;pendingNav.current=null;nav?.();};
  const keepEditing=()=>{setDiscardAsk(false);pendingNav.current=null;};
  const closeModal=()=>{if(dirty.current){setDiscardAsk(true);return;}discardSheet();};
  useEffect(()=>{if(!modal)setDiscardAsk(false);},[modal]);
  const booking=(id:string)=>open({type:'booking',id});
  function go(p:string,opts:{tab?:string;replace?:boolean}={}){
    cancelBackFallback();
    if(pageDirty.current&&!modalRef.current&&p!==page){pendingNav.current=()=>go(p,opts);setDiscardAsk(true);return;} // V15-2: an unsaved inline form asks first
    const histTab=display.isMobile?undefined:opts.tab; // FIT §11.2: the history tab is desktop only (the phone keeps its own tab ids and Back)
    if(p!==page&&!opts.replace){try{const idx=histIdx()+1;history.pushState({hcc:'page',page:p,idx,tab:histTab},'');setDepth(idx);}catch{/* history unavailable */}}
    else if(p!==page){try{history.replaceState({hcc:'page',page:p,idx:histIdx(),tab:histTab},'');}catch{/* ignore */}}
    if(display.isMobile&&p!==page)window.setTimeout(()=>window.scrollTo(0,0),0);
    setTabKey(opts.tab??'');setNavSeq(n=>n+1);setPage(p);setSearch('');setFilter('All');setMobile(false);setError('');}
  // Android back / browser back (MB-02, AC-M03): closes the sheet first, then returns to the previous page.
  useEffect(()=>{try{history.replaceState({hcc:'page',page,idx:0},'');}catch{/* ignore */}},[]);// eslint-disable-line
  useEffect(()=>{if(!modal&&modalPushed.current){modalPushed.current=false;skipPop.current=true;try{history.back();}catch{/* ignore */}}},[modal]);
  useEffect(()=>{const onPop=(e:PopStateEvent)=>{
      popSeen.current=true;cancelBackFallback();
      if(skipPop.current){skipPop.current=false;return;}
      if(modalRef.current){
        if(dirty.current){try{history.pushState({hcc:'modal',page,idx:histIdx()},'');}catch{/* ignore */}setDiscardAsk(true);return;}
        modalPushed.current=false;dirty.current=false;setModal(null);setError('');return;}
      if(pageDirty.current){ // V15-2: Back on a page with an unsaved inline form: stay on it, ask, and go back only on Discard
        try{history.pushState({hcc:'page',page,idx:histIdx()+1},'');}catch{/* ignore */}
        pendingNav.current=()=>{try{history.back();}catch{go(HOME_PAGE,{replace:true});}};setDiscardAsk(true);return;}
      const st=e.state as {hcc?:string;page?:string;tab?:string}|null;
      if(st?.hcc==='page'&&st.page){setPage(st.page);setTabKey(display.isMobile?'':(st.tab??''));setNavSeq(n=>n+1);setDepth(histIdx());} // phone: setTabKey('') as before (§11.2)
    };window.addEventListener('popstate',onPop);return ()=>window.removeEventListener('popstate',onPop);},[page,display.isMobile]);
  const back=()=>{if(depth>0){try{history.back();return;}catch{/* ignore */}}go(HOME_PAGE,{replace:true});};
  // Android hardware back button (APK only, plan D1): sheet → wizard step → previous page → landing page → "Exit?" confirmation.
  const [exitAsk,setExitAsk]=useState(false);
  const [searchOpen,setSearchOpen]=useState(false),[menuOpen,setMenuOpen]=useState(false);
  useEffect(()=>{const on=(e:KeyboardEvent)=>{if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='k'){e.preventDefault();setSearchOpen(v=>!v);}};window.addEventListener('keydown',on);return ()=>window.removeEventListener('keydown',on);},[]);
  const nativeBack=useRef<()=>void>(()=>{});
  nativeBack.current=()=>{
    cancelBackFallback();
    if(discardAsk){keepEditing();return;}
    if(exitAsk){setExitAsk(false);return;}
    if(searchOpen){setSearchOpen(false);return;}
    if(menuOpen){setMenuOpen(false);return;}
    const act=decideBack({sheetOpen:!!modalRef.current,stepBack:hasStepBack(),depth,atLanding:page===HOME_PAGE});
    if(act==='close-sheet')closeModal();
    else if(act==='step-back')runStepBack();
    else if(act==='history-back'){
      // Fallback (T-05): if history has nowhere to go, no popstate arrives — treat it as the landing page instead of doing nothing.
      popSeen.current=false;try{history.back();}catch{/* ignore */}
      backTimer.current=window.setTimeout(()=>{if(!popSeen.current){setDepth(0);try{history.replaceState({hcc:'page',page:HOME_PAGE,idx:0},'');}catch{/* history unavailable */}if(page===HOME_PAGE)setExitAsk(true);else go(HOME_PAGE,{replace:true});}},400);}
    else if(act==='go-landing')go(HOME_PAGE,{replace:true});
    else setExitAsk(true);
  };
  useEffect(()=>{if(!isNative())return;styleStatusBar();const offRestyle=listenStatusBarRestyle();const offBack=listenBackButton(()=>nativeBack.current());return ()=>{offRestyle();offBack();};},[]); // V15-1: colours again after a rotation
  function onTab(t:MobileTab|'more'){if(t==='more'){go('more',{tab:'more'});return;}announceTab(t.key);go(t.page,{tab:t.key});
    if(t.focus)window.setTimeout(()=>{const h=Array.from(document.querySelectorAll('.kanban-col>h3')).find(x=>x.textContent?.includes(t.focus!));h?.scrollIntoView({block:'start',behavior:'smooth'});},120);}

  /** The only path for mutations: idempotency key, fault simulation, typed errors, one toast. */
  async function run(cmd:Command,msg?:string,opts:RunOptions={}):Promise<boolean>{
    if(busyRef.current)return false; // a response is still pending — ignore repeated clicks
    if(process.env.HCC_BUILD==='production'&&server){
      // W4-0: mapped → server command(s) + reload of the affected slices; unmapped / read-only → refused. Never a local change.
      busyRef.current=true;setBusy(true);
      try{
        const r=await server.run(cmd);
        if(!r.ok){setError(r.text);toast(r.text,true);return false;}
        setError('');dirty.current=false;if(opts.close)setModal(null);opts.after?.();
        // Partial success (e.g. checkout done, tax invoice refused): the follow-up problem stays visible on the screen the action led to.
        if(r.warning){setError(r.warning);toast(r.warning,true);}else toast(msg||'Updated successfully');
        return true;
      }finally{busyRef.current=false;setBusy(false);}
    }
    const base=stateRef.current;
    const command={...cmd,key:cmd.key??`${cmd.type}:${cmd.id??''}:${cmd.action??''}:${cmd.index??''}:${base.sequence}`};
    const mode=fault;if(mode!=='none')setFault('none'); // a simulated fault applies to one action only
    if(mode==='latency'){busyRef.current=true;setBusy(true);await sleep(LATENCY_MS);busyRef.current=false;setBusy(false);}
    try{
      const duplicate=base.processed.includes(command.key);
      const {state:next,error:faultError}=executeWithFault(stateRef.current,uid,command,mode);
      if(next!==stateRef.current)persist(next);
      if(faultError)throw faultError;
      setError('');dirty.current=false; // AC-04: a completed action never leaves the sheet dirty (no "Discard?" prompt afterwards)
      if(opts.close)setModal(null);
      opts.after?.();
      toast(duplicate?'Already recorded earlier — no duplicate was created.':(msg||'Updated successfully'));
      return true;
    }catch(e){
      const code=codeOf(e),text=`${errorTitle[code]}: ${(e as Error).message}`;
      setError(text);toast(text,true);return false;
    }
  }
  function switchUser(id:string){
    const u=s.users.find(x=>x.id===id);setUid(id);setModal(null);setError('');setSearch('');setFilter('All');
    if(u){writeSession(id);go(startFor(s,id),{replace:true});}
  }
  function signIn(id:string){setUid(id);setSignedIn(true);writeSession(id);{const p=startFor(s,id);setPage(p);try{history.replaceState({hcc:'page',page:p,idx:histIdx()},'');}catch{/* history unavailable */}}setModal(null);setError('');setSearch('');setFilter('All');}
  function signOut(){void clearExports();if(process.env.HCC_BUILD==='production'&&server)server.signOut();cancelBackFallback();writeSession(null);setSignedIn(false);setMenuOpen(false);setModal(null);setSearchOpen(false);setError('');setPage(HOME_PAGE);setDepth(0);}
  // FY26 is built in the scenario worker so a scenario switch or reset does not freeze the page (audit P-01).
  async function loadScenario(id:string){cancelBackFallback();if(busyRef.current)return;
    busyRef.current=true;setBusy(true);
    try{const next=await generateScenario(id);persist(next);setModal(null);setUid('owner');writeSession('owner');setPage(HOME_PAGE);toast(`Scenario ${id} loaded. All fictional records restored from the fixture.`);}
    catch(e){toast((e as Error).message,true);}
    finally{busyRef.current=false;setBusy(false);}}
  const resetScenario=()=>loadScenario(s.scenario);

  const permission=(p:Permission|Permission[])=>can(s,uid,p);
  const act=(p:Permission,fn:()=>void,label:string,style='btn')=>permission(p)?<button className={style} disabled={busy||offline} title={offline?READ_ONLY_HINT:undefined} onClick={fn}>{label}</button>:null;
  const pending=s.approvals.filter(a=>a.status==='Pending').length+s.expenses.filter(e=>e.status==='Pending').length;
  const arrivals=s.bookings.filter(b=>b.status==='Confirmed'&&b.arrival<=s.date),departures=s.bookings.filter(b=>b.status==='In house'&&b.departure<=s.date);
  // Completed tasks older than two days are summarised (historyX) rather than listed on the boards.
  const taskList=s.tasks.filter(t=>(!assignedScopeRoles.includes(user.role)||t.assignee===uid)&&!(t.status==='Done'&&t.date&&t.date<addDays(s.date,-2)));
  const roomStatus=(r:Room)=>statusOf(s,r);

  const ctx:Ctx={s,uid,user,m,page:current,go,modal,open,closeModal,booking,error,busy,run,toast,permission,act,search,setSearch,filter,setFilter,tab,setTab,arrivals,departures,pending,taskList,roomStatus,switchUser,fault,setFault,loadScenario,resetScenario,display,setDirty,openSearch:()=>{cancelBackFallback();setSearchOpen(true);},openMenu:()=>{cancelBackFallback();setMenuOpen(true);},signOut,prefs:prefsApi};
  const pages:Record<string,()=>React.ReactNode>={overview:()=><Overview/>,rooms:()=><RoomBoard/>,reservations:()=><Reservations/>,folios:()=><Reservations folio/>,housekeeping:()=><Housekeeping/>,expenses:()=><Expenses/>,closing:()=><DayClosing/>,reports:()=><Reports/>,access:()=><AccessControl/>,settings:()=><HotelSettings/>};
  for(const [id,Page] of Object.entries(extraPages))pages[id]=()=><Page/>;
  // LF-ALERTS · A8 function keys (contract §9.3): desktop layout only; inert with a dialog open, without the permission, or when the target is not available
  useEffect(()=>{
    if(!signedIn||display.isMobile)return;
    const on=(e:KeyboardEvent)=>{
      const sc=shortcutFor(e);if(!sc)return;
      if(modalRef.current||searchOpen||menuOpen)return;
      if(!shortcutAllowed(sc,permission))return;
      const hasModal=sc.modal?!!extraModals[sc.modal]||sc.modal==='new'||sc.modal==='shortcuts':false;
      if(sc.page&&!allowedIds.includes(sc.page))return;
      if(sc.modal&&!hasModal&&!sc.optional)return;
      e.preventDefault();
      if(sc.page&&sc.page!==current)go(sc.page);
      if(sc.modal&&hasModal)open({type:sc.modal});
    };
    window.addEventListener('keydown',on);return ()=>window.removeEventListener('keydown',on);
  });
  // LF-ALERTS end
  useEffect(()=>{if(!signedIn)return;const l=navItems.find(n=>n.id===current)?.label;document.title=(l?l+' · ':'')+'Hotel Control Centre';},[current,signedIn]);
  const closed=isDayClosed(s);

  const MobilePage=display.isMobile?mobilePages[current]:undefined;
  const pageBody=current===HOME_PAGE?<ModulesHome/>:parseCatPage(current)?<CategoryPage page={current}/>:MobilePage?<MobilePage/>:pages[current]?.()??(current==='more'?null:<Empty title="This user has no active access" body="Switch to an enabled user with the demo controls to continue."/>);
  const toastEl=notice&&<div className={'toast '+(notice.error?'error':'')+(display.isMobile?' m-toast':'')} role="status" aria-live="polite">{notice.error?<AlertCircle size={19}/>:<CheckCircle2 size={19}/>}<span>{notice.text}</span><button aria-label="Dismiss message" onClick={()=>setNotice(null)}><X size={16}/></button></div>;
  const exitEl=<>{discardAsk&&<DiscardAsk onKeep={keepEditing} onDiscard={discardSheet}/>}{exitAsk?<ExitAsk onStay={()=>setExitAsk(false)} onExit={exitApp}/>:null}</>;
  if(!signedIn)return <HccContext.Provider value={ctx}><SignIn onPick={signIn}/></HccContext.Provider>;
  if(display.isMobile)return <HccContext.Provider value={ctx}>
    <MobileShell allowedIds={allowedIds} canGoBack={depth>0} onBack={back} tabKey={tabKey} onTab={onTab}>{pageBody}</MobileShell>
    <DeskAlerts phone/>{/* LF-ALERTS */}
    {process.env.HCC_BUILD==='production'&&<SyncStatus/>}{toastEl}<ModalHost/>{menuOpen&&<NavDrawer onClose={()=>setMenuOpen(false)}/>}{searchOpen&&<SearchPalette onClose={()=>setSearchOpen(false)}/>}{exitEl}
  </HccContext.Provider>;
  const fitNav:FitNav={desktop:true,page:current,tabKey,seq:navSeq,setTabCrumb};
  const crumbTab=tabCrumb&&tabCrumb.page===current?tabCrumb.label:undefined;
  return <HccContext.Provider value={ctx}><FitNavContext.Provider value={fitNav}>
    <div className="app-shell fit-shell">
      <a className="skip-link" href="#main-content" onClick={e=>{e.preventDefault();document.getElementById('main-content')?.focus();}}>Skip to main content</a>
      <main id="main-content" tabIndex={-1}>
        <FitTopbar>
          <div className="topbar-left"><button className="icon-btn mobile-menu" aria-label="Open menu" onClick={()=>{cancelBackFallback();setMenuOpen(true);}}><Menu size={22}/></button><Breadcrumb page={current} label={navItems.find(n=>n.id===current)?.label||'Access unavailable'} tab={crumbTab}/></div>
          <div className="topbar-right">
            <button type="button" className="search-btn" onClick={()=>{cancelBackFallback();setSearchOpen(true);}} aria-label="Search pages and actions (Control+K)"><Search size={16}/><span className="fit-search-text">Search</span><kbd>Ctrl K</kbd></button>
            <span className="today" title={`Simulated business date ${dateLabel(s.date)} · ${closed?'Closed':'Open'}`}><CalendarDays size={15}/><span className="fit-date-text">Business date </span>{dateLabel(s.date)} · <b className={closed?'day-closed':'day-open'}>{closed?'Closed':'Open'}</b></span>
            {process.env.HCC_BUILD==='production'?null:<><span className="today fit-clock" title="Simulated hotel clock (Asia/Kolkata)"><Clock size={15}/>{clockTime(s.clock)} IST</span>
            <DemoPill/></>}
            <button type="button" className="current-user current-user-btn" aria-haspopup="dialog" title="Your profile: change my password / PIN, display mode, sign out, about this build"
              onClick={()=>open({type:'user-menu'})}>{user.name.split(' · ')[0]} · {user.role}</button>
            <TextSizeQuick/>
            <DisplayToggle display={display} compact/>
          </div>
        </FitTopbar>
        <DeskAlerts/>{/* LF-ALERTS · wake-up and reminder banner (contract §9.1) */}
        {/* F4-T3 · LAN health banner */}
        {/* F4-T3 end */}
        {process.env.HCC_BUILD==='production'?<SyncStatus/>:null}
        <FitContent>{pageBody}</FitContent>
      </main>
    </div>
    {toastEl}
    <ModalHost/>{menuOpen&&<NavDrawer onClose={()=>setMenuOpen(false)}/>}{searchOpen&&<SearchPalette onClose={()=>setSearchOpen(false)}/>}{exitEl}
  </FitNavContext.Provider></HccContext.Provider>;
}

/** FIT §3 top bar: one 56 px row. When the right-hand chips do not fit, they shrink in priority order (data-fit-squeeze 1 → 3):
 *  clock hidden → the words "Business date" hidden → the word "Search" hidden; icons always stay. Measured, so text size changes count.
 *  One ResizeObserver for the bar's life (bar, left and right halves); a content change re-measures through the effect below. */
function FitTopbar({children}:{children:React.ReactNode}){
  const ref=useRef<HTMLElement>(null);const fitRef=useRef<()=>void>(()=>{});
  useLayoutEffect(()=>{const el=ref.current;if(!el)return;
    // Too tight = the bar overflows, or a breadcrumb crumb is cut (the crumbs shrink first, so their ellipsis is the early signal).
    const tight=()=>el.scrollWidth>el.clientWidth+1||Array.from(el.querySelectorAll<HTMLElement>('.crumbs li>button,.crumbs li>span')).some(c=>c.scrollWidth>c.clientWidth+1);
    const fit=()=>{let lvl=0;el.dataset.fitSqueeze='0';
      while(lvl<3&&tight()){lvl++;el.dataset.fitSqueeze=String(lvl);}};
    fitRef.current=fit;fit();
    if(typeof ResizeObserver==='undefined')return;
    const ro=new ResizeObserver(()=>fit());ro.observe(el);
    const right=el.querySelector('.topbar-right'),left=el.querySelector('.topbar-left');if(right)ro.observe(right);if(left)ro.observe(left);
    return ()=>ro.disconnect();},[]);
  useLayoutEffect(()=>{fitRef.current();}); // new crumbs / date / user text: re-measure (cheap; no observer churn)
  return <header className="topbar fit-topbar" ref={ref}>{children}</header>;
}

/** FIT §3 / §11.3 content area: fills the height, never scrolls. A converted page's root (`fit-page`) is its single child. A page not
 *  converted yet gets data-fit-scroll="legacy" on its own root element (no wrapper, so React never remounts the page) and scrolls there
 *  (z-fit.css); a page that renders several root elements (a fragment) marks .content itself. The check tool reports both as not-converted. */
function FitContent({children}:{children:React.ReactNode}){
  const ref=useRef<HTMLDivElement>(null);
  useLayoutEffect(()=>{const el=ref.current;if(!el)return;
    const mark=()=>{
      const kids=Array.from(el.children);
      for(const k of kids)if(k.getAttribute('data-fit-scroll')==='legacy'&&(kids.length!==1||k.classList.contains('fit-page')))k.removeAttribute('data-fit-scroll');
      if(kids.length===1&&!kids[0].classList.contains('fit-page'))kids[0].setAttribute('data-fit-scroll','legacy');
      if(kids.length>1)el.setAttribute('data-fit-scroll','legacy');else el.removeAttribute('data-fit-scroll');
    };
    mark();
    if(typeof MutationObserver==='undefined')return;
    const mo=new MutationObserver(mark);mo.observe(el,{childList:true});
    return ()=>mo.disconnect();},[]);
  return <div className="content" ref={ref}>{children}</div>;
}
