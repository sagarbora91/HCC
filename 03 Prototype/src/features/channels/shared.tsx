// Module 21–22 · shared UI bits for channels and booking site.
import React from 'react';
import {FlaskConical} from 'lucide-react';
import {chnStatusTone} from '../../services/mock';
import {shortDate} from '../../components/ui';

export function ChnPill({status,children}:{status:string;children?:React.ReactNode}){return <span className={'chn-pill '+chnStatusTone(status)}>{children??status}</span>;}
export function ChnSimulated({text='No data leaves this device. Channels and guests are fictional.'}:{text?:string}){
  return <div className="chn-sim" role="note"><FlaskConical size={16} aria-hidden/><span><b>Simulated.</b> {text.replace(/^Simulated[ —.-]*/,'')}</span></div>;
}
export const stay=(a:string,d:string)=>`${shortDate(a)} → ${shortDate(d)}`;
export const party=(adults:number,children:number)=>`${adults} adult${adults===1?'':'s'}${children?` + ${children} child${children===1?'':'ren'}`:''}`;
export const firstName=(name?:string)=>name?.split(' · ')[0]??'—';
