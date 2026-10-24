// BL-034 Roadmap: read-only list of functional areas 13–26 (PRD §3) and their current prototype build. No actions on this page.
import React from 'react';
import {Info} from 'lucide-react';
import {HK_ROADMAP} from '../../services/mock';
import {PageTitle,Pill} from '../../components/ui';
export function Roadmap(){
  return <><PageTitle title="Roadmap" sub="Areas beyond the MVP. All are built in the prototype; external integrations are simulated."/>
    <div className="info-note"><Info size={16}/>MVP covers areas 01–12. Areas 13–26 are now built in the prototype (simulated wherever an outside service would be involved) and keep their production phases; none are dropped (PRD §3, DEC-02). This page is read-only.</div>
    <div className="card"><div className="table-wrap"><table className="hk-table"><thead><tr><th>#</th><th>Area</th><th>Phase</th><th>Prototype disposition</th><th>Current build</th></tr></thead>
      <tbody>{HK_ROADMAP.map(r=><tr key={r.no}><td>{r.no}</td><td><strong>{r.area}</strong></td><td><Pill tone={r.phase==='Phase 2'?'blue':'neutral'}>{r.phase}</Pill></td><td>{r.disposition}</td><td>{r.current}</td></tr>)}</tbody></table></div></div></>;
}
