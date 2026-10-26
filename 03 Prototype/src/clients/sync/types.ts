// Server sync layer (Wave 4, W4-0): the contract every area module (`sync/<area>.ts`) implements.
// Production build only — the prototype bundle never contains this folder (sync/package.json "sideEffects": false plus the
// compile-time HCC_BUILD branches; tests/sync-foundation.mjs checks the bundle).
//
// An area declares
//   • loaders  — server GETs → one prototype state slice each (`s.bookings`, `s.posX`, …). The production state is built
//                ONLY from loaders (over an empty skeleton, see empty.ts); there is no demo data in production.
//   • commands — prototype `cmd.type` → one or more server commands (`POST /commands/<Name>` with Idempotency-Key and,
//                where the prototype command carries a version, expectedVersion) + the slices to reload afterwards.
// Anything not mapped is refused by ctx.run with "Not available on the server yet: <command>" — never a local change.
//
// Worked example (the real one is sync/reservations.ts):
//
//   export const housekeeping:SyncArea={id:'housekeeping',
//     loaders:[{slice:'tasks',requires:'housekeeping',
//       async load(c){const r=await c.api.get<SrvTaskPage>('/housekeeping/tasks',{pageSize:200});return r.items.map(mapTask);}}],
//     commands:[{type:'TASK',reload:['tasks','rooms'],
//       precheck:(cmd)=>cmd.action?undefined:'Choose an action.',
//       async send(c){await c.api.command('TransitionHousekeepingTask',{taskId:serverId(c.cmd.id,'Task'),action:c.cmd.action},
//         {expectedVersion:c.expectedVersion('task',c.cmd.id)});}}]};
//
// Rules (README.md has the checklist): plain object literals only (no top-level calls), money stays integer paise,
// dates YYYY-MM-DD, prototype ids = String(server id) except rooms (= room number) and room types (= type code),
// masked fields stay masked, never write to the prototype's demo storage.
import type {State,Command,Permission} from '../../services/mock';
import type {HttpTransport} from '../http';
import type {HccClients} from '../types';
import type {IdDirectory} from './map';

/** A top-level key of the prototype State (array slice, module slice `…X`, or a scalar such as `date`/`clock`). */
export type SliceKey=Extract<keyof State,string>;
type Query=Record<string,string|number|boolean|undefined>;

/** Server calls available to loaders and command mappers (shared transport: session, connection state, wire checks). */
export interface SyncApi{
  /** GET /api/v1<path>. Never retried automatically; UNAVAILABLE switches the app to read-only. */
  get<T>(path:string,query?:Query):Promise<T>;
  /** POST /api/v1/commands/<name>. The Idempotency-Key is derived from the user action (see CommandCtx.key): the first call of a
   *  mapper uses the action key, later calls `<key>:2`, `<key>:3`, … (or `<key>:<step>` when `step` is given) — identical on retry. */
  command<T>(name:string,body:Record<string,unknown>,opts?:{expectedVersion?:string;step?:string}):Promise<T>;
  /** The raw transport, for the typed clients (clients/http.ts). */
  readonly transport:HttpTransport;
  /** The shared typed HTTP clients (reservations, system, me) on the same transport. */
  readonly clients:HccClients;
}

/** What the signed-in user holds, from GET /me/capabilities (see capabilities.ts). */
export type SyncMe={userId:number;uid:string;displayName:string;role:import('../../services/mock').Role;roles:string[];
  /** Server PERM ids held (any mode). */permIds:string[];
  /** Prototype permissions derived through PERMISSION_MAP. */permissions:Permission[];
  grants:{permissionId:string;mode:string;scope:string;limit?:number|null;limitUnit?:string|null}[];
  /** Field → mask kind (RBAC §9). Masked values arrive masked from the server; never unmask on the client. */fieldMasks:Record<string,string>};

export interface LoaderCtx{
  api:SyncApi;
  /** Value of a slice as currently assembled (this batch's fresh value when it was already loaded in this batch). */
  current<K extends SliceKey>(slice:K):State[K];
  /** True when the signed-in user holds the prototype permission (same rule as can()). */
  can(p:Permission|readonly Permission[]):boolean;
  me:SyncMe;
  /** Server id ↔ prototype id directory + record versions (persisted with the read cache). */
  ids:IdDirectory;
}

export type SliceLoader={
  slice:SliceKey;
  /** 'own' (default): exactly one area owns a slice and returns its whole value. 'patch': runs after the owner in the same
   *  batch and returns an updated value (e.g. the money area filling `lines`/`receipts` into `s.bookings`). */
  kind?:'own'|'patch';
  /** Skip (slice stays as the skeleton) unless the user holds this prototype permission — avoids 403 noise. */
  requires?:Permission|readonly Permission[];
  /** Slices that must be loaded first in the same batch (their owners run before this loader). */
  needs?:SliceKey[];
  load(c:LoaderCtx):Promise<unknown>;
};

export interface CommandCtx<C extends Command=Command>{
  cmd:C;
  /** The assembled production state the screen acted on. */
  state:State;
  api:SyncApi;
  ids:IdDirectory;
  me:SyncMe;
  /** Stable Idempotency-Key of this user action (dialog key, or derived from the command; same on every retry). */
  key:string;
  /** The server version recorded for (kind, prototype id) at load time — only when the prototype command carries `version`
   *  (the prototype's own stale check); otherwise undefined. Pass it as `expectedVersion`. */
  expectedVersion(kind:string,protoId:string):string|undefined;
}
export type CommandOutcome={/** Extra slices to reload (on top of the mapper's `reload`). */reload?:SliceKey[];/** Returned to the caller. */result?:unknown;
  /** Partial success (W4 D1): the main command succeeded but a follow-up step did not (e.g. checkout done, invoice refused). The screen
   *  shows this text instead of its success message; the reload still runs so the screen shows what the server now holds. */warning?:string};

export type CommandMapper={
  /** Prototype command type (`cmd.type`). One mapper per type across all areas (a test enforces it). */
  type:string;
  /** Slices reloaded after a successful send (the server is the truth; nothing is changed locally). */
  reload:SliceKey[];
  /** Client-side refusal before any network call: return the message (shown as "Check the details: …"), or undefined. */
  precheck?(cmd:Command,s:State,me:SyncMe):string|undefined;
  send(c:CommandCtx):Promise<CommandOutcome|void>;
  /** Screens that run the flow through a typed client instead of ctx.run (Wave 1 booking dialogs: useReservations().commit)
   *  report the result here so the area can remember new ids; `reload` runs afterwards as for send(). */
  track?(result:unknown,ids:IdDirectory):void;
};

export type SyncArea={
  /** Short area id (file name), e.g. 'reservations'. */
  id:string;
  loaders:SliceLoader[];
  commands:CommandMapper[];
  /** Gaps the area knows about (prototype commands deliberately not mapped, with the reason) — listed in tests/docs. */
  unmapped?:Record<string,string>;
};
