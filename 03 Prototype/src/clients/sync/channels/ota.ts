// OTA channels + booking-website queue (W4-11) — sync area part. Server: GET /ota/{channels,messages,pushes}, GET /web-booking/requests[/{id}],
// POST /commands/{SetChannelStatus,SaveChannel,SetChannelStopSell,PushChannelRates,PullChannelMessages,AcceptChannelMessage,RejectChannelMessage,
// ConfirmWebRequest,DeclineWebRequest}. All channel traffic is simulated server-side.
//   slice  channelsX ← channels (+mapping/stop-sell), inbox messages (Queued ones = the simulated outbox), push log, web requests.
//   ids    channel id = String(channelId); message id = String(messageId) (kind 'channelMessage' version); web request id = String(requestId)
//          (kind 'webRequest'); rooms by room number (kind 'room', learned by the rooms area); room types by code (kind 'roomType').
//   Not on any prototype screen, so not loaded: GET /ota/commission (the screen computes it from bookings), /ota/reconciliation,
//   CreateWebDepositLink / MarkWebDepositPaid.
import type {Command} from '../../../services/mock';
import type {CHNState,CHNChannel,CHNMessage,CHNPush,CHNWebRequest} from '../../../domain/streams/channels';
import {HccClientError} from '../../errors';
import type {CommandMapper,LoaderCtx,SliceLoader} from '../types';
import {serverId,utcToClock,paise} from '../map';

type SrvMap={roomTypeId:number;roomType:string;channelCode:string;stopSell:boolean};
type SrvChannel={channelId:number;code:string;name:string;status:string;version:string;commissionBps:number;markupBps:number;lastSync?:string|null;statusNote:string;mapping:SrvMap[]};
type SrvMessage={messageId:number;version:string;channelId:number;kind:string;channelRef:string;guest:string;phone?:string|null;email?:string|null;roomType?:string|null;
  channelRoomCode:string;arrival:string;departure:string;adults:number;children:number;amountPaise:number;receivedAt?:string|null;status:string;reservationId?:number|null;
  room?:string|null;reason?:string|null};
type SrvPush={pushId:number;channelId:number;requestedAt:string;rows:number;closed:number;summary:string};
type SrvWebList={requests:{requestId:number}[];pending:number};
type SrvWebDetail={requestId:number;requestNo:string;status:string;guest:string;phone:string;email?:string|null;notes?:string|null;roomType:string;arrival:string;departure:string;
  adults:number;children:number;netPaise:number;taxPaise:number;totalPaise:number;createdAt:string;reservationId?:number|null;reason?:string|null;decidedBy?:number|null;version:string};

/** A section the user may not read (403) or a module that is switched off stays empty instead of failing the whole slice. */
async function optional<T>(p:Promise<T>,empty:T):Promise<T>{
  try{return await p;}catch(e){if(e instanceof HccClientError&&(e.wireCode==='FORBIDDEN'||e.wireCode==='NOT_FOUND'))return empty;throw e;}
}

export function channelFromServer(c:SrvChannel):CHNChannel&{code:string}{
  const mapping:Record<string,string>={},stopSell:string[]=[];
  for(const m of c.mapping){if(m.channelCode)mapping[m.roomType]=m.channelCode;if(m.stopSell)stopSell.push(m.roomType);}
  return {id:String(c.channelId),code:c.code,name:c.name,status:c.status as CHNChannel['status'],commissionBps:c.commissionBps,markupBps:c.markupBps,mapping,stopSell,
    ...(c.lastSync?{lastSync:utcToClock(c.lastSync)}:{}),...(c.statusNote?{statusNote:c.statusNote}:{})};
}
export function messageFromServer(m:SrvMessage):CHNMessage{
  return {id:String(m.messageId),channel:String(m.channelId),kind:m.kind as CHNMessage['kind'],ref:m.channelRef,guest:m.guest,
    ...(m.phone?{phone:m.phone}:{}),...(m.email?{email:m.email}:{}),roomType:m.roomType??m.channelRoomCode,arrival:m.arrival,departure:m.departure,adults:m.adults,children:m.children,
    amount:paise(m.amountPaise,'amount'),receivedAt:utcToClock(m.receivedAt),status:m.status as CHNMessage['status'],
    ...(m.reservationId?{booking:String(m.reservationId)}:{}),...(m.room?{room:m.room}:{}),...(m.reason?{reason:m.reason}:{})};
}
export function webRequestFromServer(d:SrvWebDetail):CHNWebRequest{
  return {id:String(d.requestId),booking:d.reservationId?String(d.reservationId):'',guest:d.guest,phone:d.phone,email:d.email??'',roomType:d.roomType,arrival:d.arrival,
    departure:d.departure,adults:d.adults,children:d.children,net:paise(d.netPaise,'net'),tax:paise(d.taxPaise,'tax'),total:paise(d.totalPaise,'total'),createdAt:utcToClock(d.createdAt),
    status:d.status==='Confirmed'?'Confirmed':d.status==='Declined'?'Declined':'Pending',...(d.reason?{reason:d.reason}:{}),...(d.decidedBy?{decidedBy:String(d.decidedBy)}:{}),
    ...(d.notes?{notes:d.notes}:{})};
}

async function loadChannels(c:LoaderCtx):Promise<CHNState>{
  const [chs,msgs,pushes,web]=await Promise.all([
    c.api.get<SrvChannel[]>('/ota/channels'),
    optional(c.api.get<SrvMessage[]>('/ota/messages',{limit:500}),[] as SrvMessage[]),
    optional(c.api.get<SrvPush[]>('/ota/pushes',{limit:50}),[] as SrvPush[]),
    optional(c.api.get<SrvWebList>('/web-booking/requests'),{requests:[],pending:0} as SrvWebList)]);
  for(const x of chs)c.ids.setVersion('channel',String(x.channelId),x.version);
  const details:SrvWebDetail[]=[];
  const rows=web.requests.slice(0,100);
  for(let i=0;i<rows.length;i+=8){
    const part=await Promise.all(rows.slice(i,i+8).map(r=>optional(c.api.get<SrvWebDetail>('/web-booking/requests/'+r.requestId),null as SrvWebDetail|null)));
    for(const d of part)if(d){c.ids.setVersion('webRequest',String(d.requestId),d.version);details.push(d);}
  }
  const inbox=msgs.filter(m=>m.status!=='Queued'&&m.status!=='Importing'),queued=msgs.filter(m=>m.status==='Queued');
  for(const m of msgs)c.ids.setVersion('channelMessage',String(m.messageId),m.version);
  const push:CHNPush[]=pushes.slice().sort((a,b)=>b.pushId-a.pushId).map(p=>({id:String(p.pushId),at:utcToClock(p.requestedAt),actor:'',channel:String(p.channelId),rows:p.rows,closed:p.closed,summary:p.summary}));
  return {channels:chs.map(channelFromServer),messages:inbox.slice().sort((a,b)=>a.messageId-b.messageId).map(messageFromServer),pushes:push,
    outbox:queued.slice().sort((a,b)=>a.messageId-b.messageId).map(messageFromServer),
    webRequests:details.slice().sort((a,b)=>a.requestId-b.requestId).map(webRequestFromServer)};
}

export const otaLoaders:SliceLoader[]=[{slice:'channelsX',requires:'reservations',load:loadChannels}];

const reasonCheck=(cmd:Command)=>String(cmd.reason??'').trim().length>=3?undefined:'Enter a reason (at least 3 characters).';
function version(c:Parameters<CommandMapper['send']>[0],kind:string,id:string):string{
  const v=c.ids.version(kind,id);
  if(v===undefined)throw new HccClientError('VALIDATION','This record has not been loaded from the server yet. Refresh and try again.');
  return v;
}
const roomTypeServerId=(c:Parameters<CommandMapper['send']>[0],code:string)=>{
  const n=c.ids.server('roomType',code);
  if(n===undefined)throw new HccClientError('VALIDATION',`Room type ${code} is not known on the server yet. Refresh and try again.`);
  return n;
};

export const otaCommands:CommandMapper[]=[
  {type:'CHN_CHANNEL_STATUS',reload:['channelsX'],async send(c){const m=c.cmd;
    await c.api.command('SetChannelStatus',{channelId:serverId(m.channel,'Channel'),action:m.action},{expectedVersion:version(c,'channel',String(m.channel))});}},
  {type:'CHN_CHANNEL_SAVE',reload:['channelsX'],async send(c){const m=c.cmd;
    const cur=(c.state.channelsX?.channels??[]).find(x=>x.id===String(m.channel)) as (CHNChannel&{code?:string})|undefined;
    if(!cur?.code)throw new HccClientError('VALIDATION','Channel not found. Refresh and try again.');
    const mapping=Object.entries((m.mapping??{}) as Record<string,string>).filter(([,code])=>String(code??'').trim())
      .map(([type,code])=>({roomTypeId:roomTypeServerId(c,type),channelCode:String(code).trim().toUpperCase()}));
    await c.api.command('SaveChannel',{channelId:serverId(m.channel,'Channel'),code:cur.code,name:cur.name,commissionBps:m.commissionBps,markupBps:m.markupBps??cur.markupBps,mapping},
      {expectedVersion:version(c,'channel',String(m.channel))});}},
  {type:'CHN_STOP_SELL',reload:['channelsX'],async send(c){const m=c.cmd;
    await c.api.command('SetChannelStopSell',{channelId:m.channel==='ALL'?null:serverId(m.channel,'Channel'),roomTypeId:roomTypeServerId(c,String(m.roomType)),on:!!m.on});}},
  {type:'CHN_PUSH',reload:['channelsX'],async send(c){await c.api.command('PushChannelRates',{channelId:serverId(c.cmd.channel,'Channel')});}},
  {type:'CHN_PULL',reload:['channelsX'],async send(c){await c.api.command('PullChannelMessages',{});}},
  {type:'CHN_ACCEPT',reload:['channelsX','bookings'],async send(c){const m=c.cmd;
    let roomId:number|undefined;
    if(m.room){roomId=c.ids.server('room',String(m.room));
      if(roomId===undefined)throw new HccClientError('VALIDATION',`Room ${m.room} is not known on the server yet. Refresh and try again.`);}
    await c.api.command('AcceptChannelMessage',{messageId:serverId(m.id,'Message'),roomId},{expectedVersion:version(c,'channelMessage',String(m.id))});}},
  {type:'CHN_REJECT',reload:['channelsX'],precheck:reasonCheck,async send(c){const m=c.cmd;
    await c.api.command('RejectChannelMessage',{messageId:serverId(m.id,'Message'),reason:String(m.reason).trim().slice(0,200)},{expectedVersion:version(c,'channelMessage',String(m.id))});}},
  {type:'CHN_WEB_CONFIRM',reload:['channelsX','bookings'],async send(c){await c.api.command('ConfirmWebRequest',{requestId:serverId(c.cmd.id,'Web request')});}},
  {type:'CHN_WEB_DECLINE',reload:['channelsX'],precheck:reasonCheck,async send(c){
    await c.api.command('DeclineWebRequest',{requestId:serverId(c.cmd.id,'Web request'),reason:String(c.cmd.reason).trim().slice(0,200)});}},
];
