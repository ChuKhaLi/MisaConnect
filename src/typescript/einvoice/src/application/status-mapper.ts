import { InvoiceStatus } from '../domain/statuses.js';

/** Port of InvoiceStatusMapper.Map (FR-048): eInvoiceStatus 3/4 override, otherwise publishStatus decides. */
export function mapInvoiceStatus(eInvoiceStatus?: number, publishStatus?: number): InvoiceStatus {
  if (eInvoiceStatus === 3) return InvoiceStatus.Replaced;
  if (eInvoiceStatus === 4) return InvoiceStatus.Adjusted;
  switch (publishStatus) {
    case 0:
      return InvoiceStatus.Draft;
    case 4:
      return InvoiceStatus.Signed;
    default:
      return InvoiceStatus.Issued;
  }
}
