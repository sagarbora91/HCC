// F12 MEAS · record types (MEAS-CONTRACT §2). Declared by the setup commit: lanes import, never redeclare.
// Design rule: NO existing persisted shape changes. Extra fields on existing records (task timestamps, customer preferences, offline
// reasons, asset life, exit reasons, root causes, booked type) live in side tables keyed by the existing id, so SCHEMA stays 15.
// Money = integer paise. Dates 'YYYY-MM-DD'; timestamps ISO strings from the simulated clock (s.clock), never real time.

/** Capture kinds that can be switched off item by item; the keys are the `meas.<pack>.<item>` setting suffixes (contract §1.2). */
export type MeasPack='daily'|'revenue'|'guest'|'hk'|'people'|'study';

// ── Desk captures (meas-cap-desk) ──
export type TurnawayReason='full'|'price'|'dates'|'room-type'|'policy'|'other';
export type MeasTurnaway={id:string;at:string;by:string;reason:TurnawayReason;arrival:string;nights:number;roomType:string;rooms:number;quotedRate?:number;channel:string;leadDays:number;note?:string;convertedBooking?:string};
export type MeasWalked={id:string;at:string;by:string;booking:string;night:string;reason:string;costPaise?:number;approvedBy?:string};
/** Per booking: the room type booked (kept when the assigned type differs) and upsell flags. */
export type MeasBookingExtra={bookedType?:string;upgrade?:'paid'|'free';upgradeApprover?:string};
export type MeasAllowanceReason={code:string;label:string;active:boolean};
/** Customer preference profile (3.4) and loyalty tag (3.7), keyed by customer id. */
export type MeasPreference={floor?:string;roomKind?:string;meal?:string;pillow?:string;billToCompany?:string;doNotDisturb?:boolean;blacklist?:{reason:string;approvedBy:string;at:string};loyaltyTag?:string;note?:string};

// ── Housekeeping / engineering captures (meas-cap-hk) ──
export type MeasTaskTimes={startedAt?:string;finishedAt?:string;inspectedAt?:string;inspectResult?:'pass'|'fail';rejectReason?:string;attendant?:string};
export type MeasOfflineReason='OOO'|'OOS'|'house-use'|'renovation';
export type MeasOffline={reason:MeasOfflineReason;expectedReturn?:string;workOrder?:string;note?:string};
export type MeasMeter={id:string;name:string;kind:'electricity'|'water'|'lpg'|'diesel'|'dg-hours';unit:string;active:boolean};
export type MeasMeterReading={id:string;meter:string;date:string;value:number;by:string;photoRef?:string};
export type MeasAssetLife={expectedLifeMonths?:number;conditionGrade?:'A'|'B'|'C'|'D';replacementCostPaise?:number};
export type MeasDiscard={id:string;item:string;qty:number;reason:string;date:string;by:string};

// ── Guest captures (meas-cap-guest) ──
export type MeasSurvey={id:string;at:string;booking?:string;roomType?:string;channel?:string;overall:number;cleanliness:number;staff:number;value:number;breakfast?:number;recommend:number;comment?:string;consent:boolean;via:'qr'|'desk'|'link';enteredBy?:string};
export type MeasReviewMonth={id:string;month:string;platform:'google'|'agoda'|'makemytrip'|'booking'|'other';count:number;avgScore:number;score12m?:number;responded:number;themes?:string};
export type MeasRootCause={code:string;label:string;active:boolean};

// ── People captures (meas-cap-people) ──
export type MeasTraining={id:string;staff:string;course:string;date:string;hours:number;certificateNo?:string;validTo?:string;evidenceRef?:string;mandatory?:boolean};
export type MeasExit={reason:string;exitDate:string};
/** Anonymous: no staff id stored, department only. */
export type MeasPulse={id:string;quarter:string;dept:string;workload:number;respect:number;tools:number;payFairness:number;recommend:number};

// ── Revenue captures (meas-forecast) ──
export type MeasEventKind='wedding-season'|'exam'|'fair'|'corporate-visit'|'holiday'|'other';
export type MeasEvent={id:string;name:string;from:string;to:string;kind:MeasEventKind;impactPct:number;note?:string};
export type MeasOtbSnapshot={id:string;takenOn:string;night:string;rooms:number;revenuePaise:number};
export type MeasCompetitor={id:string;name:string;active:boolean};
export type MeasRateShop={id:string;date:string;stayDate:string;competitor:string;roomType:string;ratePaise:number;withBreakfast:boolean;source:string;by:string};
export type MeasRestrictionCounter={night:string;kind:'MIN_STAY'|'CTA';refused:number;reshaped:number};

/** State slice s.measX. Absent = nothing captured yet (old saves). Switch / target values are NOT here: they live in configX.values (meas.*). */
export type MeasState={
  turnaway?:MeasTurnaway[];walked?:MeasWalked[];allowanceReasons?:MeasAllowanceReason[];
  bookingExtra?:Record<string,MeasBookingExtra>;preference?:Record<string,MeasPreference>;
  taskTimes?:Record<string,MeasTaskTimes>;offline?:Record<string,MeasOffline>;meters?:MeasMeter[];readings?:MeasMeterReading[];assetLife?:Record<string,MeasAssetLife>;discards?:MeasDiscard[];
  surveys?:MeasSurvey[];reviews?:MeasReviewMonth[];rootCauseList?:MeasRootCause[];rootCause?:Record<string,string>;
  training?:MeasTraining[];exit?:Record<string,MeasExit>;pulse?:MeasPulse[];
  events?:MeasEvent[];otb?:MeasOtbSnapshot[];competitors?:MeasCompetitor[];rateshop?:MeasRateShop[];restrictions?:MeasRestrictionCounter[];
};
