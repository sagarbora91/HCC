// Shared till (DEC-052): open restaurant / room-service checks block DAY_CLOSE. Tests that close a BASE day settle the fixture's
// open checks (POS-F03 table T2, POS-F04 room 201) by UPI first: no drawer, folio or ledger-cash figure changes.
export function tillClear(M,s,uid='cashier'){
  for(const o of M.posOpenOrders(s)){
    if(M.posUnsent(o).length)s=M.execute(s,uid,{type:'POS_KOT_SEND',id:o.id});
    s=M.execute(s,uid,{type:'POS_SETTLE',id:o.id,mode:'outlet',method:'UPI',reference:'DEMO-UPI-CLOSE-'+o.id});
  }
  return s;
}
