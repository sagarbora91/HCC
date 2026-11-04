// AF-NETSET (audit item 23) · the Owner's Internet switches of the production sync layer.
//
//   server   POST /commands/SetNetSetting {setting, value, reason, expectedVersion} (PERM-NET-001, Owner): written through Master configuration
//            (EGRESS-CONTRACT §1.1: dated, audited with the reason, Configuration history). Keys: net.internetEnabled, net.messaging, net.ota,
//            net.ocr, net.payments, net.email, net.updateCheck (booleans) and net.window ({from,to,days} object, or '' = no window).
//   commands CFG_NET_SET {setting, value, reason} → SetNetSetting; reloads the configuration slices so the panel re-reads the value.
//   The command never writes the firewall state (the server's outbox worker does, EGRESS-CONTRACT §4). NET_TICK stays unmapped (worker tick).
import {HccClientError} from '../errors';
import type {SyncArea} from './types';

export const NET_SETTING_KEYS=['net.internetEnabled','net.messaging','net.ota','net.ocr','net.payments','net.email','net.updateCheck','net.window'] as const;
const configReload=['configX','settings','configHistory','approvals'] as const;

/** Body of SetNetSetting from the prototype command; refuses before any request. */
export function netSettingBody(cmd:Record<string,unknown>,version:string|undefined):{setting:string;value:unknown;reason:string;expectedVersion:string|null}{
  const setting=String(cmd.setting??'');
  if(!(NET_SETTING_KEYS as readonly string[]).includes(setting))throw new HccClientError('VALIDATION','Unknown internet setting.');
  const reason=String(cmd.reason??'').trim();
  if(reason.length<3||reason.length>120)throw new HccClientError('VALIDATION','Give a reason for changing an internet switch (3–120 characters).');
  let value=cmd.value;
  if(setting==='net.window')value=value===undefined||value===null?'':value;
  else if(typeof value!=='boolean')throw new HccClientError('VALIDATION','Choose on or off.');
  return {setting,value,reason,expectedVersion:version??null};
}

export const egress:SyncArea={
  id:'egress',
  loaders:[],
  commands:[
    {type:'CFG_NET_SET',reload:[...configReload],
      precheck:cmd=>{try{netSettingBody(cmd,undefined);return undefined;}catch(e){return e instanceof HccClientError?e.message:'Check the internet setting.';}},
      async send(c){await c.api.command('SetNetSetting',netSettingBody(c.cmd,c.ids.version('setting',String(c.cmd.setting))));}},
  ],
  unmapped:{NET_TICK:'the outbox worker tick: the hotel server replays held work on its own (EGRESS-CONTRACT §1.2)'},
};
