// MA-0 · Phone sheet 'mst-row' (id = "<master>:<rowId>", empty rowId = new row): the shared MasterForm in sheet mode
// (sticky .modal-actions footer). Desktop uses the same modal id through features/masters/register.tsx (MasterRowModal).
import React from 'react';
import {MasterForm} from '../../features/masters/MasterForm';
import {useHcc} from '../../app/context';

export function MasterSheet({id}:{id?:string}){
  const {closeModal}=useHcc();
  const [spec,row]=String(id??'').split(':');
  return <MasterForm specId={spec} rowId={row||undefined} sheet onCancel={closeModal}/>;
}
