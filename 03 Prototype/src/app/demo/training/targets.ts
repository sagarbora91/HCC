// Training driver: target resolution. A target string is "<kind>:<arg>"; see plan §7.4 / skill reference/driver-interface.md.
const norm=(t:string|null|undefined)=>(t??'').replace(/\s+/g,' ').trim().toLowerCase();
export const visible=(el:Element|null|undefined):el is HTMLElement=>{
  if(!el)return false;const h=el as HTMLElement;
  if(!h.getClientRects().length)return false;
  const r=h.getBoundingClientRect();if(r.width<2||r.height<2)return false;
  const cs=getComputedStyle(h);return cs.visibility!=='hidden'&&cs.display!=='none';
};
const DEMO_POP='#fit-demo-pop,.fit-demo,.demo-toolbar,.tr-coach';
/** The topmost open dialog / sheet / palette / drawer, ignoring the demo popover. */
export function topDialog():HTMLElement|null{
  const all=Array.from(document.querySelectorAll<HTMLElement>('[role="dialog"],[role="alertdialog"],dialog[open]')).filter(d=>visible(d)&&!d.matches(DEMO_POP));
  return all.length?all[all.length-1]:null;
}
const inDemo=(el:Element)=>!!el.closest(DEMO_POP);
const nameOf=(el:Element)=>norm(el.getAttribute('aria-label')||el.textContent);
const BTN='button,[role="button"],a,[role="tab"],[role="menuitem"],[role="radio"],[role="option"],summary,[role="switch"],[role="checkbox"]';
function pick(list:HTMLElement[],want:string,nameFn:(e:HTMLElement)=>string):HTMLElement|null{
  const w=norm(want);const vis=list.filter(e=>visible(e)&&!inDemo(e));
  return vis.find(e=>nameFn(e)===w)??vis.find(e=>nameFn(e).startsWith(w))??vis.find(e=>nameFn(e).includes(w))??null;
}
function scoped<T>(f:(root:ParentNode)=>T|null):T|null{const d=topDialog();return (d?f(d):null)??f(document);}
function byBtn(text:string){return scoped(root=>pick(Array.from(root.querySelectorAll<HTMLElement>(BTN)),text,nameOf));}
function fieldOf(label:string):HTMLElement|null{
  const w=norm(label);
  return scoped(root=>{
    const ctl='input:not([type=hidden]),select,textarea';
    const cands=Array.from(root.querySelectorAll<HTMLElement>(ctl)).filter(e=>visible(e)&&!inDemo(e));
    const byAria=cands.find(e=>norm(e.getAttribute('aria-label'))===w)??cands.find(e=>norm(e.getAttribute('aria-label')).includes(w));
    if(byAria)return byAria;
    for(const l of Array.from(root.querySelectorAll('label')).filter(l=>norm(l.textContent).startsWith(w)||norm(l.textContent).includes(w))){
      const c=(l as HTMLLabelElement).control??l.querySelector<HTMLElement>(ctl);if(c&&visible(c))return c as HTMLElement;
    }
    const byPh=cands.find(e=>norm(e.getAttribute('placeholder')).includes(w));if(byPh)return byPh;
    // aria-labelledby / preceding text label
    for(const e of cands){const id=e.getAttribute('aria-labelledby');if(id){const t=norm(document.getElementById(id)?.textContent);if(t.includes(w))return e;}}
    return null;});
}
function rowOf(text:string):HTMLElement|null{
  const w=norm(text);
  return scoped(root=>{
    const rows=Array.from(root.querySelectorAll<HTMLElement>('tr,[role="row"],li,[role="listitem"],article,.card,.row')).filter(e=>visible(e)&&!inDemo(e)&&norm(e.textContent).includes(w));
    const leaf=rows.filter(e=>!rows.some(o=>o!==e&&e.contains(o)));
    return leaf[0]??null;});
}
/** Resolve a step target to the element to outline (or click). Null = not on screen. */
export function resolveTarget(target:string):HTMLElement|null{
  if(!target)return null;
  const i=target.indexOf(':');if(i<0)return null;
  const kind=target.slice(0,i),arg=target.slice(i+1);
  switch(kind){
    case 'sel':{const l=Array.from(document.querySelectorAll<HTMLElement>(arg)).filter(visible);return l[0]??null;}
    case 'tr':{const l=Array.from(document.querySelectorAll<HTMLElement>(`[data-tr="${arg}"]`)).filter(visible);return l[0]??null;}
    case 'aria':return scoped(root=>pick(Array.from(root.querySelectorAll<HTMLElement>('[aria-label]')),arg,e=>norm(e.getAttribute('aria-label'))));
    case 'btn':return byBtn(arg);
    case 'tab':return scoped(root=>pick(Array.from(root.querySelectorAll<HTMLElement>('[role="tab"]')),arg,nameOf))??byBtn(arg);
    case 'field':return fieldOf(arg);
    case 'row':return rowOf(arg);
    case 'modalbtn':{const d=topDialog();return d?pick(Array.from(d.querySelectorAll<HTMLElement>(BTN)),arg,nameOf):null;}
    case 'dialog':return topDialog();
    case 'menutop':return pick(Array.from(document.querySelectorAll<HTMLElement>('.tile,.nav-cat,.drawer button,nav button')),arg,e=>norm(e.querySelector('strong')?.textContent||e.textContent));
    case 'menuleaf':{const leaf=arg.split('>').pop()!.trim();return pick(Array.from(document.querySelectorAll<HTMLElement>('.tile,.drawer button,nav button')),leaf,e=>norm(e.querySelector('strong')?.textContent||e.textContent));}
    case 'text':{ // first smallest visible element whose own text contains arg
      const w=norm(arg);const all=Array.from(document.querySelectorAll<HTMLElement>('main *,[role="dialog"] *')).filter(e=>visible(e)&&!inDemo(e)&&norm(e.textContent).includes(w));
      return all.filter(e=>!all.some(o=>o!==e&&e.contains(o)))[0]??null;}
    default:return null;
  }
}
export {norm};
