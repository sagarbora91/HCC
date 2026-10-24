// HCC qualification tests for the adapted H04 helper, with a side-by-side run of the unmodified donor function (read-only import).
import {describe,it,expect} from 'vitest';
import {calendarDateInTimeZone,assertTimeZone,InvalidTimeZoneError} from './src/calendar-date';
import {calendarDateInTimeZone as donor} from '../../../06 Reference Repositories/haip/apps/api/src/common/date/property-business-date';
import {seed,execute} from '../../../03 Prototype/src/services/mock/index';

describe('H04 Asia/Kolkata calendar date',()=>{
  it('IST is UTC+5:30: 18:29:59Z is still the 23rd, 18:30:00Z is the 24th',()=>{
    expect(calendarDateInTimeZone(new Date('2026-10-23T18:29:59Z'),'Asia/Kolkata')).toBe('2026-10-23');
    expect(calendarDateInTimeZone(new Date('2026-10-23T18:30:00Z'),'Asia/Kolkata')).toBe('2026-10-24');
  });
  it('year boundary: 31 Mar 18:30Z is 1 Apr in India (fiscal-year start)',()=>{
    expect(calendarDateInTimeZone(new Date('2027-03-31T18:30:00Z'),'Asia/Kolkata')).toBe('2027-04-01');
    expect(calendarDateInTimeZone(new Date('2026-12-31T18:30:00Z'),'Asia/Kolkata')).toBe('2027-01-01');
  });
  it('upstream case: America/New_York and UTC at a UTC-midnight boundary',()=>{
    const t=new Date('2024-06-02T00:30:00.000Z');
    expect(calendarDateInTimeZone(t,'America/New_York')).toBe('2024-06-01');
    expect(calendarDateInTimeZone(t,'UTC')).toBe('2024-06-02');
  });
});

describe('H04 configuration errors (deliberate change from donor)',()=>{
  it('invalid timezone throws InvalidTimeZoneError; donor silently returns the UTC date',()=>{
    const t=new Date('2024-06-02T12:00:00.000Z');
    expect(()=>calendarDateInTimeZone(t,'Not/A_Timezone')).toThrow(InvalidTimeZoneError);
    expect(donor(t,'Not/A_Timezone')).toBe('2024-06-02');
    expect(()=>assertTimeZone('')).toThrow(InvalidTimeZoneError);
    expect(()=>assertTimeZone('Asia/Kolkata')).not.toThrow();
  });
  it('invalid instant is rejected (donor would throw a RangeError from toISOString)',()=>{
    expect(()=>calendarDateInTimeZone(new Date('nope'),'Asia/Kolkata')).toThrow(RangeError);
  });
  it('adapted and donor results agree for valid zones across a day of instants',()=>{
    for(let h=0;h<48;h++){const t=new Date(Date.UTC(2026,9,23,0,30*h));
      expect(calendarDateInTimeZone(t,'Asia/Kolkata')).toBe(donor(t,'Asia/Kolkata'));}
  });
});

describe('H04 calendar date is not the business date (DEC-042)',()=>{
  it('crossing local midnight does not change HCC business date; only day close + next day does',()=>{
    const s=seed();
    expect(calendarDateInTimeZone(new Date('2026-10-24T19:00:00Z'),'Asia/Kolkata')).toBe('2026-10-25');
    expect(s.date).toBe('2026-10-24');
    // A command at that wall-clock time still posts against business date 24 Oct.
    const n=execute(s,'owner',{type:'NIGHT_POST'});
    expect(n.date).toBe('2026-10-24');
  });
});
