// Render the actual report component for print QA, using fictional history only.
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../src/app/context';
import {ReportPrintModal} from '../src/features/reports/Reports';
import {loadScenario} from '../src/services/mock';
import {printablePage} from '../src/app/native';
export function reportHtml(from:string,to:string,css:string){
  const s=loadScenario('FY26');
  const context={s,permission:()=>true,closeModal:()=>{},display:{isMobile:false}} as any;
  const fragment=renderToStaticMarkup(<HccContext.Provider value={context}><ReportPrintModal id="daily" room={`${from}|${to}`}/></HccContext.Provider>);
  return printablePage('HCC print regression',fragment,css);
}
