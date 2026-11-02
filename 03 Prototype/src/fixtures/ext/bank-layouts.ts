// ACC-4 (contract §2.1, owner default A23): the seed of the bank statement layout master (mst.bank_statement_layout). The owner's real bank
// layout becomes a new row later (Owner master, no code change). Same row as deployment/import-templates/bank-statement-layouts.csv and
// server migration 4285.
import type {BankStatementLayout} from '../../domain/streams/accounting-bank';
export const ACC_BANK_LAYOUT_SEED:BankStatementLayout[]=[
  {id:'generic',name:'Generic',bankLedger:'1030',delimiter:',',headerRows:1,dateFormat:'DD/MM/YYYY',
    columns:{date:'date',narration:'narration',ref:'ref',debit:'debit',credit:'credit',balance:'balance'},active:true,
    settlementPattern:'(UPI|CARD|POS|MERCHANT)[^A-Z]*(SETTL|STL)'},
];
