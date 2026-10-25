// MB-02 Mobile shell (SCR-M00): app bar, role tabs + More, safe areas. Same context, run(), registry and modals as desktop.
import React from 'react';
import {Search,Hotel,ArrowLeft,Menu,FlaskConical,CalendarDays} from 'lucide-react';
import {useHcc} from '../context';
import {navItems} from '../nav';
import {HOME_PAGE,parseCatPage,categoryOf} from '../navmap';
import {Breadcrumb} from '../ModulesHome';
import {tabsFor,MobileTab} from './tabs';
import {dateLabel} from '../../components/ui';
import {isDayClosed} from '../../services/mock';
export function MobileShell({allowedIds,children,canGoBack,onBack,tabKey,onTab}:{allowedIds:string[];children:React.ReactNode;canGoBack:boolean;onBack:()=>void;tabKey:string;onTab:(t:MobileTab|'more')=>void}){
  const {s,user,page,open,pending,taskList,arrivals,display,openSearch,openMenu}=useHcc();
  const tabs=tabsFor(user.role,allowedIds);
  const tabPages=tabs.map(t=>t.page);
  const showBack=canGoBack&&page!=='more'&&!tabPages.includes(page)&&page!==HOME_PAGE;
  const cid=parseCatPage(page);
  const title=page===HOME_PAGE?'Modules':cid?categoryOf(cid).label:page==='more'?'More':navItems.find(n=>n.id===page)?.label||'Hotel Control Centre';
  const closed=isDayClosed(s);
  const badge=(t:MobileTab)=>t.badge==='approvals'?pending:t.badge==='tasks'?taskList.filter(x=>x.status!=='Done').length:t.badge==='arrivals'?arrivals.length:0;
  const active=(t:MobileTab)=>tabKey?tabKey===t.key:page===t.page;
  return <div className={'m-shell'+(display.framed?' framed':'')}>
    <header className="m-appbar">
      {showBack?<button className="m-icon" aria-label="Back" onClick={onBack}><ArrowLeft size={22}/></button>:<span className="m-brand" aria-hidden><Hotel size={20}/></span>}
      <div className="m-titles"><h1 className="m-title">{title}</h1>
        <span className={'m-date '+(closed?'closed':'open')} title="Simulated business date"><CalendarDays size={12}/>Business date {dateLabel(s.date).replace(/ \d{4}$/,'')} · <b>{closed?'Closed':'Open'}</b></span></div>
      <button className="m-search" aria-label="Search pages and actions" onClick={openSearch}><Search size={22}/></button>
      {process.env.HCC_BUILD==='production'?null:<button className="m-demo-pill" onClick={()=>open({type:'demo'})} aria-label="Open demo controls"><FlaskConical size={14}/>DEMO</button>}
      <button className="m-avatar" aria-label={`User menu: ${user.name}`} onClick={()=>open({type:'user-menu'})}>{user.name[0]}</button>
    </header>
    <main className="m-content" id="m-content">{page!==HOME_PAGE&&<div className="m-crumbs"><Breadcrumb page={page} label={navItems.find(n=>n.id===page)?.label||''}/></div>}{children}</main>
    <nav className="m-tabbar" aria-label="Main tabs">
      {tabs.map(t=>{const n=badge(t);return <button key={t.key} className={active(t)?'on':''} aria-current={active(t)?'page':undefined} onClick={()=>onTab(t)}><span className="m-tab-icon"><t.icon size={22}/>{n>0&&<b aria-label={`${n} open`}>{n}</b>}</span><span>{t.label}</span></button>;})}
      <button onClick={openMenu} aria-haspopup="dialog"><span className="m-tab-icon"><Menu size={22}/></span><span>Menu</span></button>
    </nav>
  </div>;
}
