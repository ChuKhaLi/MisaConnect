import { mapInvoiceStatus } from '../../application/status-mapper.js';
import type { InvoiceSnapshotDto } from '../../domain/dtos.js';
import { parseDateTime } from './datetime.js';

/** Incoming lookup row (`/webapp/getlist`, `/webapp/paging`, `/webapp/paging/calculating`). */
export interface WireLookupRow {
  RefID?: string | null;
  InvoiceTemplateID?: string | null;
  InvSeries?: string | null;
  InvDate?: string | null;
  InvNo?: string | null;
  AccountObjectTaxCode?: string | null;
  AccountObjectName?: string | null;
  TotalSaleAmount?: number | null;
  TotalVATAmount?: number | null;
  TotalAmount?: number | null;
  TotalSaleAmountOC?: number | null;
  TotalVATAmountOC?: number | null;
  TotalAmountOC?: number | null;
  EInvoiceStatus?: string | null;
  PublishStatus?: string | null;
  OrgRefID?: string | null;
  CreatedDate?: string | null;
  ModifiedDate?: string | null;
}

const INT_PATTERN = /^\s*[+-]?\d+\s*$/;

function tryParseInt(v: string | null | undefined): number | undefined {
  if (v == null || !INT_PATTERN.test(v)) return undefined;
  const n = Number(v);
  return Number.isSafeInteger(n) && n >= -2147483648 && n <= 2147483647 ? n : undefined;
}

/** `yyyy-MM-dd` only; returned as an unspecified-kind ISO date-time (midnight), like .NET's DateTime?. */
function tryParseDate(v: string | null | undefined): string | undefined {
  if (v == null || !/^\d{4}-\d{2}-\d{2}$/.test(v)) return undefined;
  const p = parseDateTime(v);
  return p ? `${p.date}T00:00:00` : undefined;
}

/** Honours an explicit offset/`Z`; otherwise assumes Vietnam +07:00 (R-LU-20). */
function tryParseLocalDateTime(v: string | null | undefined): string | undefined {
  if (v == null || v === '') return undefined;
  const p = parseDateTime(v);
  if (!p) return undefined;
  const frac = p.fraction.slice(0, 7).replace(/0+$/, '');
  return `${p.date}T${p.time}${frac !== '' ? `.${frac}` : ''}${p.offset ?? '+07:00'}`;
}

function num(v: number | null | undefined): number | undefined {
  return v == null ? undefined : v;
}

function str(v: string | null | undefined): string | undefined {
  return v == null ? undefined : v;
}

/** Port of `InvoiceLookupMapper.FromWire` + `LookupDtoMapper.ToDto(InvoiceSnapshot)`. */
export function fromWireSnapshot(w: WireLookupRow): InvoiceSnapshotDto {
  const rawEInvoiceStatus = tryParseInt(w.EInvoiceStatus);
  const rawPublishStatus = tryParseInt(w.PublishStatus);
  const out: InvoiceSnapshotDto = {
    refId: w.RefID ?? '',
    status: mapInvoiceStatus(rawEInvoiceStatus, rawPublishStatus),
  };
  const set = <K extends keyof InvoiceSnapshotDto>(k: K, v: InvoiceSnapshotDto[K] | undefined) => {
    if (v !== undefined) out[k] = v as InvoiceSnapshotDto[K];
  };
  set('invoiceTemplateID', str(w.InvoiceTemplateID));
  set('invSeries', str(w.InvSeries));
  set('invDate', tryParseDate(w.InvDate));
  set('invNo', str(w.InvNo));
  set('accountObjectTaxCode', str(w.AccountObjectTaxCode));
  set('accountObjectName', str(w.AccountObjectName));
  set('totalSaleAmount', num(w.TotalSaleAmount));
  set('totalVATAmount', num(w.TotalVATAmount));
  set('totalAmount', num(w.TotalAmount));
  set('totalSaleAmountOC', num(w.TotalSaleAmountOC));
  set('totalVATAmountOC', num(w.TotalVATAmountOC));
  set('totalAmountOC', num(w.TotalAmountOC));
  set('rawEInvoiceStatus', rawEInvoiceStatus);
  set('rawPublishStatus', rawPublishStatus);
  set('orgRefID', w.OrgRefID ? w.OrgRefID : undefined);
  set('createdDate', tryParseLocalDateTime(w.CreatedDate));
  set('modifiedDate', tryParseLocalDateTime(w.ModifiedDate));
  return out;
}
