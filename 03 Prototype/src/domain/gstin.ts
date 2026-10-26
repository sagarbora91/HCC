// GSTIN format + check character — the same rule as the server (08 Production Hcc.Domain/Invoices/GstIdentity.cs `Gstin.Problem`).
// 15 characters = state code (2 digits) + PAN (5 letters, 4 digits, 1 letter) + entity number (1–9, A–Z) + 'Z' + check character.
// The check character is the GSTN mod-36 checksum (weights 1, 2 alternating; each product folded as quotient + remainder of 36).
// Only the format and checksum are verified; the GST portal is not queried. Used by the production build only (Master configuration
// brand.gstin and the SETTING_SET mapper); the prototype keeps its fictional placeholder values.
const ALPHABET='0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ';
const SHAPE=/^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$/;
const STATES=new Set(['01','02','03','04','05','06','07','08','09','10','11','12','13','14','15','16','17','18','19','20','21','22','23','24','26','27','29',
  '30','31','32','33','34','35','36','37','38','97']);

export function gstinCheckCharacter(first14:string):string{
  let sum=0;
  for(let i=0;i<14;i++){const v=ALPHABET.indexOf(first14[i]!);if(v<0)return '';const p=v*(i%2===0?1:2);sum+=Math.floor(p/36)+p%36;}
  return ALPHABET[(36-sum%36)%36]!;
}
/** Plain reason the GSTIN is not acceptable, or undefined when valid (wording = the server's). */
export function gstinProblem(value:unknown,whose='hotel'):string|undefined{
  const g=String(value??'').trim().toUpperCase();
  if(!g)return `Enter the ${whose} GSTIN.`;
  if(!SHAPE.test(g))return `The ${whose} GSTIN must be 15 characters: state code, PAN, entity number, Z and a check character.`;
  if(!STATES.has(g.slice(0,2)))return `The ${whose} GSTIN starts with an unknown state code (${g.slice(0,2)}).`;
  if(gstinCheckCharacter(g.slice(0,14))!==g[14])return `The ${whose} GSTIN check character is wrong; check the number for a typing error.`;
  return undefined;
}
