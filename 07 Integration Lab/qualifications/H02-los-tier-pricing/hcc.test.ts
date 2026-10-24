// HCC qualification tests for the adapted H02 helper (original HCC tests; upstream cases re-expressed in paise/bps are marked "upstream case").
import {describe,it,expect} from 'vitest';
import {selectLosTier,selectOccupancyBand,validateOccupancyBands,applyBps,priceNight,RateRuleError,type LosTier,type OccupancyBand} from './src/rate-adjustments';

const tiers:LosTier[]=[{minNights:7,adjBps:-500},{minNights:14,adjBps:-1000},{minNights:21,adjBps:-1500}];
const code=(fn:()=>unknown)=>{try{fn();return 'none';}catch(e){return e instanceof RateRuleError?e.code:'other';}};

describe('H02 LOS tiers',()=>{
  it('upstream case: highest qualifying tier at 6/7/13/14/21/30 nights',()=>{
    expect(selectLosTier(tiers,6)).toBeNull();
    expect(selectLosTier(tiers,7)?.adjBps).toBe(-500);
    expect(selectLosTier(tiers,13)?.adjBps).toBe(-500);
    expect(selectLosTier(tiers,14)?.adjBps).toBe(-1000);
    expect(selectLosTier(tiers,21)?.adjBps).toBe(-1500);
    expect(selectLosTier(tiers,30)?.adjBps).toBe(-1500);
  });
  it('rejects duplicate, zero and fractional tiers and invalid night counts',()=>{
    expect(code(()=>selectLosTier([{minNights:7,adjBps:-500},{minNights:7,adjBps:-900}],8))).toBe('INVALID_RULE');
    expect(code(()=>selectLosTier([{minNights:0,adjBps:-500}],8))).toBe('INVALID_RULE');
    expect(code(()=>selectLosTier([{minNights:7.5,adjBps:-500}],8))).toBe('INVALID_RULE');
    expect(code(()=>selectLosTier([{minNights:7,adjBps:-12.5}],8))).toBe('INVALID_RULE');
    expect(code(()=>selectLosTier(tiers,0))).toBe('INVALID_INPUT');
    expect(code(()=>selectLosTier(tiers,2.5))).toBe('INVALID_INPUT');
  });
});

describe('H02 occupancy bands (half-open)',()=>{
  const bands:OccupancyBand[]=[{fromPct:0,toPct:50,adjBps:-1000},{fromPct:50,toPct:80,adjBps:0},{fromPct:80,toPct:100,adjBps:1500}];
  it('upstream case re-expressed: 40 / 65 / 90 select the three bands; 101 rejected',()=>{
    expect(selectOccupancyBand(bands,40)?.adjBps).toBe(-1000);
    expect(selectOccupancyBand(bands,65)?.adjBps).toBe(0);
    expect(selectOccupancyBand(bands,90)?.adjBps).toBe(1500);
    expect(code(()=>selectOccupancyBand(bands,101))).toBe('INVALID_INPUT');
  });
  it('boundary 50 % matches exactly one band; 0 % and 100 % are covered',()=>{
    expect(selectOccupancyBand(bands,50)?.adjBps).toBe(0);
    expect(selectOccupancyBand(bands,0)?.adjBps).toBe(-1000);
    expect(selectOccupancyBand(bands,100)?.adjBps).toBe(1500);
  });
  it('overlapping or inverted bands are rejected (donor inclusive bands [0,50]+[50,80] would match 50 twice)',()=>{
    expect(code(()=>validateOccupancyBands([{fromPct:0,toPct:51,adjBps:0},{fromPct:50,toPct:80,adjBps:0}]))).toBe('INVALID_RULE');
    expect(code(()=>validateOccupancyBands([{fromPct:60,toPct:60,adjBps:0}]))).toBe('INVALID_RULE');
    expect(code(()=>validateOccupancyBands([{fromPct:-1,toPct:20,adjBps:0}]))).toBe('INVALID_RULE');
  });
  it('a gap returns null (no adjustment), not a neighbouring band',()=>{
    expect(selectOccupancyBand([{fromPct:0,toPct:40,adjBps:-500},{fromPct:60,toPct:100,adjBps:500}],50)).toBeNull();
  });
});

describe('H02 integer-paise arithmetic',()=>{
  it('upstream case: ₹200 −10 % = ₹180, +12 % = ₹224 (exact paise, no float tolerance)',()=>{
    expect(applyBps(20000,-1000)).toBe(18000);
    expect(applyBps(20000,1200)).toBe(22400);
  });
  it('rounds once, half away from zero, symmetric for negative results',()=>{
    expect(applyBps(1,5000)).toBe(2);      // 1.5 → 2
    expect(applyBps(1,-15000)).toBe(-1);   // −0.5 → −1
    expect(applyBps(3333,500)).toBe(3500); // 3499.65 → 3500
  });
  it('LOS and occupancy bps are summed then rounded once (differs from sequential application)',()=>{
    expect(priceNight(3333,{los:{minNights:7,adjBps:-1000},band:{fromPct:80,toPct:100,adjBps:1500}})).toBe(3500);
    const sequential=applyBps(applyBps(3333,-1000),1500);
    expect(sequential).toBe(3450);
  });
  it('fixed discount larger than the base rate is an error, not a silent clamp',()=>{
    expect(code(()=>priceNight(250000,{fixed:[{paise:-300000}]}))).toBe('NEGATIVE_RATE');
    expect(priceNight(250000,{fixed:[{paise:-250000}]})).toBe(0);
  });
  it('rejects fractional paise, negative base and unsafe magnitudes',()=>{
    expect(code(()=>applyBps(100.5,0))).toBe('INVALID_INPUT');
    expect(code(()=>applyBps(-1,0))).toBe('INVALID_INPUT');
    expect(code(()=>applyBps(Number.MAX_SAFE_INTEGER,100))).toBe('INVALID_INPUT');
    expect(code(()=>priceNight(1000,{fixed:[{paise:0.5}]}))).toBe('INVALID_INPUT');
  });
});
