// Modules Home (category tiles), category pages (page tiles), the clickable breadcrumb and the staff-picker sign-in.
import React from 'react';
import {ChevronRight,Hotel,LogIn,ShieldCheck} from 'lucide-react';
import {useHcc} from './context';
import {navItems} from './nav';
import {NAV_CATEGORIES,DASHBOARD,PAGE_BLURB,catPage,crumbsFor,pagesOfCategory,parseCatPage,navFilterModules,type CategoryId} from './navmap';
import {Empty} from '../components/ui';
import {cmpBadge} from '../services/mock';

/** Home › Category › Page, each earlier part clickable. */
export function Breadcrumb({page,label}:{page:string;label:string}){
  const {go}=useHcc();const parts=crumbsFor(page,label);
  return <nav className="crumbs" aria-label="You are here"><ol>{parts.map((p,i)=><li key={i}>
    {i>0&&<ChevronRight size={13} aria-hidden="true"/>}
    {p.page?<button type="button" onClick={()=>go(p.page!)}>{p.label}</button>:<span aria-current={i===parts.length-1?'page':undefined}>{p.label}</span>}
  </li>)}</ol></nav>;
}

/** Page tile → opens the page. Count shows open work where there is some. */
function Tile({icon:Icon,label,blurb,badge,onClick}:{icon:React.ComponentType<{size?:number}>;label:string;blurb:string;badge?:number;onClick:()=>void}){
  return <button type="button" className="tile" onClick={onClick}>
    <span className="tile-ic" aria-hidden="true"><Icon size={26}/></span>
    <span className="tile-text"><strong>{label}</strong><small>{blurb}</small></span>
    {!!badge&&badge>0&&<b className="tile-badge" aria-label={badge+' open'}>{badge}</b>}
  </button>;
}

export function ModulesHome(){
  const {s,uid,go,user,permission,arrivals,taskList}=useHcc();
  const allowed=new Set(navFilterModules(s,navItems).filter(n=>permission(n.p)).map(n=>n.id));
  const inHouse=s.bookings.filter(b=>b.status==='In house').length;
  const openTasks=taskList.filter(t=>t.status!=='Done').length;
  const pendingApprovals=s.approvals.filter(a=>a.status==='Pending').length;
  const pendingExpenses=s.expenses.filter(e=>e.status==='Pending').length;
  const badge:Partial<Record<CategoryId,number>>={front:arrivals.length,hkm:openTasks,guestsvc:inHouse,cash:pendingExpenses,admin:pendingApprovals,accounts:cmpBadge(s,uid)};
  const cats=NAV_CATEGORIES.map(c=>({c,pages:pagesOfCategory(c.id).filter(p=>allowed.has(p))})).filter(x=>x.pages.length);
  const first=user.name.split(' · ')[0];
  return <div className="home">
    <div className="home-head"><h1>Modules</h1><p>Welcome, {first}. Pick a module to get started — or press <kbd>Ctrl K</kbd> to search.</p></div>
    {!allowed.size&&<Empty title="This user has no active access" body="Switch to an enabled user with the demo controls to continue."/>}
    <div className="tiles" role="list">
      {allowed.has(DASHBOARD.id)&&<div role="listitem"><Tile icon={DASHBOARD.icon} label={DASHBOARD.label} blurb={PAGE_BLURB.overview} onClick={()=>go(DASHBOARD.id)}/></div>}
      {cats.map(({c,pages})=><div role="listitem" key={c.id}><Tile icon={c.icon} label={c.label} blurb={c.blurb} badge={badge[c.id]} onClick={()=>pages.length===1?go(pages[0]):go(catPage(c.id))}/></div>)}
    </div>
  </div>;
}

export function CategoryPage({page}:{page:string}){
  const {s,go,permission}=useHcc();
  const id=parseCatPage(page);const cat=NAV_CATEGORIES.find(c=>c.id===id);
  if(!cat)return <Empty title="Module not found" body="Go back to Modules Home."/>;
  const items=navFilterModules(s,navItems).filter(n=>pagesOfCategory(cat.id).includes(n.id)&&permission(n.p));
  return <div className="home">
    <div className="home-head"><h1>{cat.label}</h1><p>{cat.blurb}</p></div>
    {!items.length&&<Empty title="Nothing here for this user" body="Your role has no pages in this module."/>}
    <div className="tiles" role="list">{items.map(n=><div role="listitem" key={n.id}><Tile icon={n.icon} label={n.label} blurb={PAGE_BLURB[n.id]??''} onClick={()=>go(n.id)}/></div>)}</div>
  </div>;
}

/** Staff picker: choose who is using this device. Screen-only (no PIN) — the prototype has demo users, not real authentication. */
export function SignIn({onPick}:{onPick:(id:string)=>void}){
  const {s}=useHcc();
  const users=s.users.filter(u=>u.enabled);
  return <main className="signin">
    <div className="signin-card">
      <div className="signin-brand"><span className="brand-mark"><Hotel size={28}/></span><div><strong>HOTEL CONTROL CENTRE</strong><small>{s.settings.hotel}</small></div></div>
      <h1>Who is using this device?</h1>
      <p className="signin-help"><ShieldCheck size={16} aria-hidden="true"/>Demo sign-in: tap your name. There is no password in the prototype; real sign-in arrives with the production build.</p>
      <ul className="signin-list">{users.map(u=><li key={u.id}><button type="button" className="signin-user" onClick={()=>onPick(u.id)}>
        <span className="avatar" aria-hidden="true">{u.name[0]}</span><span className="signin-name"><strong>{u.name.split(' · ')[0]}</strong><small>{u.role}</small></span><LogIn size={18} aria-hidden="true"/>
      </button></li>)}</ul>
      <p className="signin-foot">DEMO — FICTIONAL DATA · Scenario {s.scenario}</p>
    </div>
  </main>;
}
