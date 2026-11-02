// F4-T3 qw-alerts · Stock page banner: items below their reorder level (same selector the owner digest reads).
import React from 'react';
import {AlertTriangle} from 'lucide-react';
import {qwReorderAlerts,qwReorderLine} from '../../services/mock';
import {useHcc} from '../../app/context';
export function ReorderBanner(){
  const {s}=useHcc();const a=qwReorderAlerts(s);
  if(!a.length)return null;
  return <div className="qw-banner" role="status"><AlertTriangle size={16}/><span><strong>Reorder:</strong> {qwReorderLine(a)}</span></div>;
}
