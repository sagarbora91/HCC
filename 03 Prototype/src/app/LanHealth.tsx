// F4-T3 qw-alerts · LAN health banner for tablets: shown when the server cannot be reached (the production build keeps changes on the tablet).
// The demo build is always reachable, so it shows nothing.
import React from 'react';
import {WifiOff} from 'lucide-react';
import {useSync} from '../clients/sync/react';
export const LAN_DOWN_TEXT='Server unreachable — changes are kept on this tablet';
function LanProd(){
  const snap=useSync();
  if(!snap.readOnly)return null;
  return <div className="qw-banner" role="alert"><WifiOff size={16}/><span>{LAN_DOWN_TEXT}</span></div>;
}
export function LanHealth(){return process.env.HCC_BUILD==='production'?<LanProd/>:null;}
