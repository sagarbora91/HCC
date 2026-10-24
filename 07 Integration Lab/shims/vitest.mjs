// Minimal vitest-compatible shim so selected upstream *.spec.ts files can run unchanged under Node.
// Supports only the matchers the qualified specs use. This is NOT vitest; results are labelled "upstream spec via shim".
const stack=[];
// Shared across separately bundled suites; run.mjs sets globalThis.__labSuite before importing each bundle.
export const results=(globalThis.__labResults??=[]);
export function describe(name,fn){stack.push(name);try{fn();}finally{stack.pop();}}
export function it(name,fn){
  const full=[...stack,name].join(' › '),suite=globalThis.__labSuite;
  try{const r=fn();if(r&&typeof r.then==='function')throw new Error('async tests are not supported by the shim');results.push({suite,name:full,status:'passed'});}
  catch(e){results.push({suite,name:full,status:'failed',error:String(e&&e.message||e)});}
}
it.each=table=>(name,fn)=>{for(const row of table){const args=Array.isArray(row)?row:[row];let i=0;it(name.replace(/%[sdi]/g,()=>String(args[i++])),()=>fn(...args));}};
export const test=it;
const eq=(a,b)=>{try{return JSON.stringify(a)===JSON.stringify(b);}catch{return a===b;}};
function matchers(actual,negate){
  const check=(ok,msg)=>{if(negate?ok:!ok)throw new Error((negate?'not ':'')+msg);};
  return {
    toBe:x=>check(Object.is(actual,x),`expected ${JSON.stringify(actual)} to be ${JSON.stringify(x)}`),
    toEqual:x=>check(eq(actual,x),`expected ${JSON.stringify(actual)} to equal ${JSON.stringify(x)}`),
    toBeNull:()=>check(actual===null,`expected ${JSON.stringify(actual)} to be null`),
    toBeUndefined:()=>check(actual===undefined,'expected undefined'),
    toBeTruthy:()=>check(!!actual,'expected truthy'),
    toBeFalsy:()=>check(!actual,'expected falsy'),
    toBeCloseTo:(x,digits=2)=>check(Math.abs(actual-x)<Math.pow(10,-digits)/2,`expected ${actual} to be close to ${x}`),
    toContain:x=>check(actual!=null&&actual.includes(x),`expected ${JSON.stringify(actual)} to contain ${JSON.stringify(x)}`),
    toHaveLength:n=>check(actual!=null&&actual.length===n,`expected length ${n}`),
    toThrow:x=>{let threw=false,err;try{actual();}catch(e){threw=true;err=e;}
      let ok=threw;
      if(threw&&typeof x==='function')ok=err instanceof x;
      else if(threw&&typeof x==='string')ok=String(err&&err.message).includes(x);
      else if(threw&&x instanceof RegExp)ok=x.test(String(err&&err.message));
      check(ok,'expected function to throw'+(x?' '+String(x.name||x):''));},
  };
}
export function expect(actual){const m=matchers(actual,false);m.not=matchers(actual,true);return m;}
export const vi={fn:()=>{throw new Error('vi.fn is not supported by the shim');}};
globalThis.describe=describe;globalThis.it=it;globalThis.test=it;globalThis.expect=expect;
