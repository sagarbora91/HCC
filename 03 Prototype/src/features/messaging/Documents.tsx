// Module 25 · OCR / document capture (simulated). Desktop page + phone page.
import React,{useState} from 'react';
import {Camera,ScanLine} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Pill,Empty,clockTime,shortDate} from '../../components/ui';
import {FitPage,ScrollPanel,SplitView} from '../../components/fit';
import {ListCard} from '../../app/mobile/components';
import {msgDocs,msgLowFields,MSG_DOC_KIND_LABEL,MSGDoc} from '../../services/mock';
import {msgSampleDocs} from '../../fixtures/ext/messaging';
import {MsgDocArt,MsgDocReview,MsgSimulated,MsgSimulatedNote,MsgKindLabel} from './shared';

const tone=(st:MSGDoc['status'])=>st==='Applied'?'green':st==='Extracted'?'blue':'neutral';
/** The ID the next MSG_DOC_EXTRACT will allocate (sequence-based, deterministic). */
const nextDocId=(s:ReturnType<typeof useHcc>['s'])=>'DOC-'+(s.sequence+1);
function History({docs,onOpen}:{docs:MSGDoc[];onOpen:(d:MSGDoc)=>void}){
  if(!docs.length)return <Empty title="No captures yet" body="Extract a sample document to start."/>;
  return <div className="table-wrap"><table><thead><tr><th>Capture</th><th>Kind</th><th>Status</th><th>Applied to</th><th>Action</th></tr></thead><tbody>
    {docs.map(d=><tr key={d.id}><td><strong>{d.title}</strong><small>{d.id} · {shortDate(d.at.slice(0,10))} {clockTime(d.at)}</small></td><td>{MSG_DOC_KIND_LABEL[d.kind]}{d.partySeq?<small>Accompanying guest {d.partySeq}</small>:null}</td>
      <td><Pill tone={tone(d.status)}>{d.status}</Pill>{d.fields.some(f=>'original' in f&&f.original!==undefined)&&<small>Corrected by staff</small>}{d.reason&&<small>{d.reason}</small>}</td>
      <td>{d.appliedTo||'—'}</td><td>{d.status==='Extracted'?<button className="text-btn" onClick={()=>onOpen(d)}>Review</button>:<span className="muted">—</span>}</td></tr>)}
  </tbody></table></div>;
}

// FIT (DEC-194…196, contract §6 `documents` = split): the sample documents and the capture history are the left pane (one panel), the review of
// the capture being checked is the record pane (below 900 px it slides over the list with Back). Same three steps, same buttons and texts.
export function MsgDocumentsPage(){
  const {s,run,busy,permission}=useHcc();
  const docs=msgDocs(s);
  const [sel,setSel]=useState<string>('');
  const [closed,setClosed]=useState<string>(''); // narrow screens: the review the person went Back from (stays out of the way until another capture is chosen)
  const active=docs.find(d=>d.id===sel&&d.status==='Extracted')||docs.find(d=>d.status==='Extracted');
  async function extract(sample:string){const id=nextDocId(s);if(await run({type:'MSG_DOC_EXTRACT',sample},'Fields extracted (simulated)')){setSel(id);setClosed('');}}
  const choose=(d:MSGDoc)=>{setSel(d.id);setClosed('');};
  // The left pane is ONE panel (samples, then the capture history): two stacked panels would each be under the 160 px minimum on a 640 px tablet.
  const left=<ScrollPanel label="Sample documents and capture history"
      footer={<MsgSimulatedNote>No camera, no upload and no real OCR — “Extract” returns fixture fields with confidence scores. No image or full ID number is stored.</MsgSimulatedNote>}>
    <div className="section-title"><h2>1 · Pick a sample document</h2><Pill>Fictional samples</Pill></div>
    <div className="msg-samples">{msgSampleDocs.map(d=><article key={d.id} className="msg-sample">
        <MsgDocArt sample={d.id}/>
        <div className="msg-sample-foot"><div><MsgKindLabel kind={d.kind}/><small>{d.hint} · {msgLowFields(d.fields).length} low-confidence</small></div>
          {permission('checkin')&&<button className="btn" disabled={busy} onClick={()=>extract(d.id)}><ScanLine size={16}/> Extract</button>}</div>
      </article>)}</div>
    <div className="section-title"><h2>Capture history</h2><span className="muted">Metadata only</span></div>
    <History docs={docs} onOpen={choose}/>
  </ScrollPanel>;
  const review=active&&closed!==active.id?<ScrollPanel title="2 · Review and confirm" label="Review and confirm" actions={<span className="muted">{active.id} · {active.title}</span>}>
      <MsgDocReview key={active.id} doc={active} onDone={()=>setSel('')}/></ScrollPanel>:null;
  return <FitPage title="Document capture" sub="Pick a fictional sample, extract fields, review and confirm into a guest profile or an expense draft." layout="split">
    <SplitView label="Document capture" ratio="1fr 1fr" emptyPane="show" onCloseDetail={()=>active&&setClosed(active.id)}
      list={left} detail={review}
      empty={<section className="card"><div className="section-title"><h2>2 · Review and confirm</h2></div><Empty title="Nothing to review" body="Extract a sample on the left; its fields appear here for checking."/></section>}/>
  </FitPage>;
}

export function MsgDocumentsMobile(){
  const {s,run,busy,open,toast,permission}=useHcc();
  const docs=msgDocs(s);const waiting=docs.filter(d=>d.status==='Extracted');
  async function extract(sample:string){const id=nextDocId(s);if(await run({type:'MSG_DOC_EXTRACT',sample},'Fields extracted (simulated)'))open({type:'msg-doc',id});}
  return <div className="msg-m">
    <MsgSimulated>No camera or real OCR in this prototype. Pick a fictional sample.</MsgSimulated>
    <button type="button" className="msg-camera" onClick={()=>toast('Camera capture is simulated — pick a sample document below')}>
      <Camera size={28} aria-hidden/><span><strong>Take photo</strong><small>Placeholder · camera not used in the prototype</small></span></button>
    {!!waiting.length&&<><h2 className="msg-m-h">Waiting for review</h2>{waiting.map(d=><ListCard key={d.id} title={d.title} sub={`${d.id} · ${MSG_DOC_KIND_LABEL[d.kind]}`} right={<Pill tone="blue">Review</Pill>} onClick={()=>open({type:'msg-doc',id:d.id})}/>)}</>}
    <h2 className="msg-m-h">Sample documents</h2>
    {msgSampleDocs.map(d=><ListCard key={d.id} title={d.title} sub={d.hint} meta={`${MSG_DOC_KIND_LABEL[d.kind]} · ${msgLowFields(d.fields).length} field(s) to check`}
      onClick={permission('checkin')&&!busy?()=>extract(d.id):undefined} right={<span className="msg-m-extract"><ScanLine size={18} aria-hidden/>Extract</span>}/>)}
    <h2 className="msg-m-h">History</h2>
    {!docs.length?<Empty title="No captures yet"/>:docs.filter(d=>d.status!=='Extracted').map(d=><ListCard key={d.id} title={d.title} sub={`${d.id} · ${d.appliedTo?'→ '+d.appliedTo:d.status}`} right={<Pill tone={tone(d.status)}>{d.status}</Pill>}/>)}
  </div>;
}
