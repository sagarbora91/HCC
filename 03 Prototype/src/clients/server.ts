// Server DTOs as the .NET API sends them (08 Production/src/Hcc.Contracts/*; camelCase JSON). Wave 1 integration: the HTTP
// adapter maps these to the client types in ./types.ts so screens and the mock adapter keep the prototype's shapes
// (room ids = room numbers, type ids = type codes, ids as strings, statuses as the prototype spells them).
// Keep in step with: Inventory/InventoryContracts.cs, Reservations/ReservationContracts.cs, Access/AccessContracts.cs.

/** RoomAvailability (reasons: "Blocked" | "Over capacity" | "Occupied" | "Booked" | "Held" | "GroupHold" | "RESTRICTED"). */
export type SrvRoomAvailability={roomId:number;roomNo:string;roomTypeId:number;roomType:string;capacity:number;available:boolean;
  reasons:string[];heldForBlock?:string|null;nightlyRatePaise:number};
export type SrvStayViolation={code:string;restriction:string;message:string};
export type SrvRoomTypeAvailability={roomTypeId:number;code:string;maxOccupancy:number;availableRooms:number;violations:SrvStayViolation[]};
export type SrvAvailability={businessDate:string;arrival:string;departure:string;nights:number;party:number;types:SrvRoomTypeAvailability[];rooms:SrvRoomAvailability[]};

export type SrvQuoteNight={date:string;ratePaise:number;taxBps:number;taxPaise:number};
export type SrvQuote={quoteId:number;version:string;roomTypeId:number;roomId?:number|null;arrival:string;departure:string;adults:number;children:number;
  ratePlanId:string;nights:SrvQuoteNight[];netPaise:number;taxPaise:number;totalPaise:number;expiresAt:string;violations:SrvStayViolation[]};

/** CreateReservationRequest (server): company = name + GSTIN (W4 G5, stored on the reservation). No e-mail / OTA channel fields yet: those stay in notes. */
export type SrvCompany={name:string;gstin?:string|null};
export type SrvCreateReservation={quoteId:number;quoteVersion:string;guest:{name:string;phone?:string};roomId:number;hold?:boolean;
  restrictionOverride?:string;groupBlockId?:number;source?:string;notes?:string;company?:SrvCompany};
export type SrvReservationResult={reservationId:number;publicNo:string;version:string;status:string;holdUntil?:string|null};

/** ReservationStatuses: Held | Expired | Confirmed | InHouse | Departed | Cancelled | NoShow. */
export type SrvReservationDetails={reservationId:number;publicNo:string;version:string;status:string;guestName:string;phone?:string|null;
  roomId:number;roomNo:string;roomTypeId:number;roomType:string;arrival:string;departure:string;adults:number;children:number;source:string;
  nightlyRatePaise?:number|null;holdUntil?:string|null;groupBlockId?:number|null;restrictionOverride?:string|null;cancelReason?:string|null;notes?:string|null;
  nights:SrvQuoteNight[];netPaise:number;taxPaise:number;totalPaise:number;company?:SrvCompany|null};

export type SrvCapabilityGrant={permissionId:string;mode:string;scope:string;limit?:number|null;limitUnit?:string|null};
export type SrvCapabilities={userId:number;propertyId:number;displayName:string;credentialKind:string;roles:string[];
  permissions:SrvCapabilityGrant[];fieldMasks:Record<string,string>;session:{idleExpiresUtc:string;absoluteExpiresUtc:string;idleTimeoutSeconds:number}};

/** Server availability reason → client AvailabilityReason code. */
export const SERVER_REASON:Record<string,string>={'Over capacity':'OverCapacity',RESTRICTED:'Restricted'};
/** Server reservation status → the prototype's spelling. */
export const SERVER_STATUS:Record<string,string>={InHouse:'In house',NoShow:'No show'};
