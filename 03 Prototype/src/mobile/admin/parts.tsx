// MB-08 small phone building blocks (ma- classes in styles/mobile-admin.css).
import React from 'react';
/** Status tag: hue AND text label, ≥ 13 px, AA contrast. tone: ok | warn | info | err | neutral */
export function MaTag({tone='neutral',children}:{tone?:string;children:React.ReactNode}){return <span className={'ma-tag '+tone}>{children}</span>;}
export const maTone=(status:string)=>['Enabled','Active','Approved','Executed','Posted','On','done','Allowed','Succeeded'].includes(status)?'ok'
  :['Pending','Expired','blocked','Approval required'].includes(status)?'warn':['Invalidated','Disabled','Rejected','Denied','Failed'].includes(status)?'err':['current','Future'].includes(status)?'info':'neutral';
export function MaIntro({children}:{children:React.ReactNode}){return <p className="ma-intro">{children}</p>;}
export function MaH({children,aside}:{children:React.ReactNode;aside?:React.ReactNode}){return <div className="ma-h"><h2>{children}</h2>{aside}</div>;}
/** A button that, when blocked, is disabled AND shows the reason as visible text below it. */
export function MaGuard({block,onClick,children,className='btn',busy}:{block?:string;onClick:()=>void;children:React.ReactNode;className?:string;busy?:boolean}){
  return <div className="ma-guard"><button type="button" className={className} disabled={!!block||busy} onClick={onClick}>{children}</button>{block&&<small className="ma-why">{block}</small>}</div>;
}
/** Plain key/value rows for cards. */
export function MaRows({rows}:{rows:[React.ReactNode,React.ReactNode][]}){return <dl className="ma-rows">{rows.map(([k,v],i)=><div key={i}><dt>{k}</dt><dd>{v}</dd></div>)}</dl>;}
