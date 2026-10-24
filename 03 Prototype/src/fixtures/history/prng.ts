// Deterministic pseudo-random numbers for the FY history generator (plan H-03). No global state, no Math.random().
export type Rng={
  /** Uniform float in [0, 1). */
  next():number;
  /** Uniform integer in [a, b] (inclusive). */
  int(a:number,b:number):number;
  /** True with probability p. */
  chance(p:number):boolean;
  pick<T>(xs:readonly T[]):T;
  /** Pick by relative weight; weights need not sum to 1. */
  weighted<T>(items:readonly (readonly [T,number])[]):T;
};
/** mulberry32 — small, fast, good enough for fixture data. */
export function createRng(seed:number):Rng{
  let a=seed>>>0;
  const next=()=>{a=(a+0x6D2B79F5)>>>0;let t=a;t=Math.imul(t^(t>>>15),t|1);t^=t+Math.imul(t^(t>>>7),t|61);return((t^(t>>>14))>>>0)/4294967296;};
  const int=(lo:number,hi:number)=>lo+Math.floor(next()*(hi-lo+1));
  return {
    next,int,
    chance:p=>next()<p,
    pick:xs=>xs[Math.floor(next()*xs.length)],
    weighted:items=>{
      const total=items.reduce((n,[,w])=>n+w,0);let r=next()*total;
      for(const [v,w] of items){r-=w;if(r<0)return v;}
      return items[items.length-1][0];
    },
  };
}
