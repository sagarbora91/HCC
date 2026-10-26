// CP-1 · Text size quick control + "Appearance & display" link for the phone user-menu sheet (app/mobile/sheets.tsx UserMenuSheet).
import React from 'react';
import {SlidersHorizontal,Type} from 'lucide-react';
import {useHcc} from '../../app/context';
import {usePrefs,textScaleLabel} from '../../app/prefs';
import {Seg,textScaleOptions,PREFS_PAGE} from './parts';

export function PrefsQuickSheet(){
  const {prefs,save}=usePrefs();const {closeModal,go}=useHcc();
  return <>
    <div><span className="m-field-label"><Type size={15}/> Text size · {textScaleLabel(prefs.textScale)} (this device only)</span>
      <Seg label="Text size" value={prefs.textScale} options={textScaleOptions} onChange={v=>save({textScale:v})} className="pf-seg-text"/></div>
    <button type="button" className="btn full mo-btn" onClick={()=>{closeModal();go(PREFS_PAGE);}}><SlidersHorizontal size={18}/>Appearance &amp; display</button>
  </>;
}
