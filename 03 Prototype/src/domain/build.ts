// Build flavour for pure domain code. esbuild replaces `process.env.HCC_BUILD` at build time (prototype / production builds and the sync
// test harness); realms that bundle domain code without that define and without `process` (tests/stream-compat.mjs) read false.
export function isProductionBuild():boolean{
  try{return process.env.HCC_BUILD==='production';}catch{return false;}
}
