// Hamburger menu: one slide-in drawer on desktop and phone (replaces the long sidebar and the phone "More" list).
// Categories collapse; only the one holding the current page starts open. Everything comes from navmap.ts, filtered by role.
import React,{useEffect,useRef,useState} from 'react';
import {X,LayoutGrid,Search,ChevronDown,FlaskConical,LogOut,Hotel,Smartphone} from 'lucide-react';
import {useHcc} from './context';
import {navItems} from './nav';
import {NAV_CATEGORIES,DASHBOARD,HOME_PAGE,PAGE_CATEGORY,catPage,pagesOfCategory,parseCatPage,type CategoryId} from './navmap';

export function NavDrawer({onClose}:{onClose:()=>void}){
  const {s,user,page,go,permission,open,openSearch,signOut,taskList}=useHcc();
  const allowed=new Set(navItems.filter(n=>permission(n.p)).map(n=>n.id));
  const here:CategoryId|undefined=parseCatPage(page)??PAGE_CATEGORY[page];
  const [openCat,setOpenCat]=useState<CategoryId|undefined>(here);
  const ref=useRef<HTMLElement>(null);
  useEffect(()=>{ref.current?.querySelector<HTMLElement>('button')?.focus();},[]);
  const nav=(p:string)=>{onClose();go(p);};
  const onKey=(e:React.KeyboardEvent)=>{if(e.key==='Escape'){e.preventDefault();onClose();}};
  const cats=NAV_CATEGORIES.map(c=>({c,items:navItems.filter(n=>pagesOfCategory(c.id).includes(n.id)&&allowed.has(n.id))})).filter(x=>x.items.length);
  return <div className="drawer-overlay" role="presentation" onMouseDown={e=>{if(e.target===e.currentTarget)onClose();}}>
    <aside className="drawer" role="dialog" aria-modal="true" aria-label="Menu" ref={ref} onKeyDown={onKey}>
      <div className="drawer-head"><span className="brand-mark"><Hotel size={22}/></span><div><strong>HOTEL CONTROL CENTRE</strong><small>{s.settings.hotel}</small></div>
        <button type="button" className="drawer-close" aria-label="Close menu" onClick={onClose}><X size={22}/></button></div>
      <button type="button" className="drawer-search" onClick={()=>{onClose();openSearch();}}><Search size={18}/>Search pages and actions<kbd>Ctrl K</kbd></button>
      <nav aria-label="Modules" className="drawer-nav">
        <button type="button" className={'drawer-item'+(page===HOME_PAGE?' on':'')} aria-current={page===HOME_PAGE?'page':undefined} onClick={()=>nav(HOME_PAGE)}><LayoutGrid size={19}/>Modules Home</button>
        {allowed.has(DASHBOARD.id)&&<button type="button" className={'drawer-item'+(page===DASHBOARD.id?' on':'')} aria-current={page===DASHBOARD.id?'page':undefined} onClick={()=>nav(DASHBOARD.id)}><DASHBOARD.icon size={19}/>{DASHBOARD.label}</button>}
        {cats.map(({c,items})=>{const isOpen=openCat===c.id;return <div key={c.id} className="drawer-cat">
          <button type="button" className={'drawer-item drawer-cat-btn'+(here===c.id?' here':'')} aria-expanded={isOpen} onClick={()=>setOpenCat(isOpen?undefined:c.id)}>
            <c.icon size={19}/><span>{c.label}</span><ChevronDown size={16} className={isOpen?'flip':''} aria-hidden="true"/></button>
          {isOpen&&<ul className="drawer-pages">
            {items.map(n=><li key={n.id}><button type="button" data-page={n.id} className={'drawer-sub'+(page===n.id?' on':'')} aria-current={page===n.id?'page':undefined} onClick={()=>nav(n.id)}>
              <n.icon size={17}/><span>{n.label}</span>{n.id==='housekeeping'&&<b>{taskList.filter(t=>t.status!=='Done').length}</b>}</button></li>)}
          </ul>}
        </div>;})}
      </nav>
      <div className="drawer-foot">
        <details className="drawer-tools"><summary>Demo &amp; display</summary>
        <div className="drawer-person"><span className="avatar" aria-hidden="true">{user.name[0]}</span><div><strong>{user.name.split(' · ')[0]}</strong><small>{user.role}</small></div></div>
        <button type="button" className="drawer-item" onClick={()=>{onClose();open({type:'demo'});}}><FlaskConical size={18}/>Demo controls</button>
        <button type="button" className="drawer-item" onClick={()=>{onClose();open({type:'user-menu'});}}><Smartphone size={18}/>Display mode</button>
        </details>
        <button type="button" className="drawer-item" onClick={()=>{onClose();signOut();}}><LogOut size={18}/>Sign out</button>
      </div>
    </aside>
  </div>;
}
