import type { MeInvoiceErrorCategory, ValidationFailure } from './errors.js';
import type { InvoiceStatus, LookupBatchStatus, LookupStatus, SaveOutcomeDto } from './statuses.js';

/** Decimal amount. Strings must match `^-?\d+(\.\d+)?$` and are sent verbatim as number literals. */
export type Money = number | string;
/** `yyyy-MM-dd`, or a `Date` formatted from its local calendar fields. */
export type DateOnlyInput = string | Date;

export interface TemplateDto {
  ipTemplateID: string;
  invSeries: string;
  templateName: string;
  invTemplateNo?: string;
  templateType: number;
  usesTaxAuthorityCode: boolean;
  isActive: boolean;
  isMoreVATRate: boolean;
}

/** An e-ticket template ("mẫu vé") from MISA `/webapp/templates/ticket` (API document §8.2). */
export interface TicketTemplateDto {
  ticketTemplateID: string;
  templateName: string;
  invSeries: string;
  /** MISA's `Inactive`, inverted. Rows are returned active and inactive alike. */
  isActive: boolean;
  /** MISA's raw ticket type (no documented enum). */
  ticketType: number;
  templateType: number;
  serviceName?: string;
  isInheritFromOldTemplate: boolean;
  isTicketCode: boolean;
}

/** Paging query for `listTicketTemplates`. Defaults: `withCode` true, `start` 0, `length` 20, `fromCashRegister` false. */
export interface TicketTemplateListRequestDto {
  withCode?: boolean;
  /** Offset, >= 0. */
  start?: number;
  /** Page size, 1–100 (MISA's maximum). */
  length?: number;
  /** MISA `invoiceCalcu`: ticket templates from a cash register (MTT). */
  fromCashRegister?: boolean;
}

export interface TemplateRefDto {
  ipTemplateID: string;
  invSeries: string;
}

export interface BuyerInfoDto {
  name: string;
  taxCode?: string;
  address?: string;
  contactName?: string;
  email?: string;
  receiverName?: string;
  mobile?: string;
  bankAccount?: string;
  bankName?: string;
}

export interface InvoiceLineDto {
  inventoryItemType: number;
  sortOrder: number;
  description: string;
  unitName: string;
  quantity: Money;
  unitPrice: Money;
  amountOC: Money;
  amount: Money;
  amountWithoutVATOC: Money;
  amountWithoutVAT: Money;
  vatRateName?: string;
  vatAmountOC?: Money;
  vatAmount?: Money;
  sortOrderView?: number;
  discountRate?: Money;
  discountAmountOC?: Money;
  discountAmount?: Money;
}

export interface InvoiceTotalsDto {
  totalSaleAmountOC: Money;
  totalSaleAmount: Money;
  totalDiscountAmountOC: Money;
  totalDiscountAmount: Money;
  totalAmountWithoutVATOC: Money;
  totalAmountWithoutVAT: Money;
  totalVATAmountOC: Money;
  totalVATAmount: Money;
  totalAmountOC: Money;
  totalAmount: Money;
  totalAmountInWords: string;
}

export interface InvoiceDto {
  refId?: string;
  template?: TemplateRefDto;
  invDate: DateOnlyInput;
  /** ISO-8601 date-time with offset. */
  createdDate: string;
  /** ISO-8601 date-time with offset. */
  modifiedDate: string;
  currency: string;
  exchangeRate: Money;
  paymentMethod: string;
  buyerType: number;
  buyer: BuyerInfoDto;
  lines: readonly InvoiceLineDto[];
  totals: InvoiceTotalsDto;
  customFields?: Record<number, string>;
}

export type ValidationFailureDto = ValidationFailure;

export interface SaveResultDto {
  refId: string;
  outcome: SaveOutcomeDto;
  errorCategory?: MeInvoiceErrorCategory;
  rawErrorCode?: string;
  errorMessage?: string;
  failures?: readonly ValidationFailureDto[];
}

export interface PdfDocumentDto {
  content: Uint8Array;
  contentType: string;
}

export interface PagedLookupRequestDto {
  start: number;
  length: number;
  sort?: string;
  fromDate: DateOnlyInput;
  toDate: DateOnlyInput;
  publishStatus?: number;
}

export interface InvoiceSnapshotDto {
  refId: string;
  invoiceTemplateID?: string;
  invSeries?: string;
  /** ISO-8601 date-time. */
  invDate?: string;
  invNo?: string;
  accountObjectTaxCode?: string;
  accountObjectName?: string;
  totalSaleAmount?: number;
  totalVATAmount?: number;
  totalAmount?: number;
  totalSaleAmountOC?: number;
  totalVATAmountOC?: number;
  totalAmountOC?: number;
  rawEInvoiceStatus?: number;
  rawPublishStatus?: number;
  status: InvoiceStatus;
  orgRefID?: string;
  createdDate?: string;
  modifiedDate?: string;
}

export interface LookupOutcomeDto {
  refId: string;
  status: LookupStatus;
  snapshot?: InvoiceSnapshotDto;
  errorCode?: string;
  message?: string;
}

export interface LookupBatchOutcomeDto {
  status: LookupBatchStatus;
  outcomes?: readonly LookupOutcomeDto[];
  reason?: string;
}

export interface PagedLookupResultDto {
  items: readonly InvoiceSnapshotDto[];
  start: number;
  length: number;
  returnedCount: number;
}

export interface OriginalInvoiceReferenceDto {
  orgRefID: string;
  orgInvNo: string;
  orgInvTemplateNo: string;
  orgInvSeries: string;
  orgInvDate: DateOnlyInput;
}

export interface ReplacementRequestDto {
  invoice: InvoiceDto;
  originalRef: OriginalInvoiceReferenceDto;
  changeReason: string;
  invoiceWithCode?: boolean;
}

export interface AdjustmentRequestDto {
  invoice: InvoiceDto;
  originalRef: OriginalInvoiceReferenceDto;
  changeReason: string;
  invoiceWithCode?: boolean;
}

export interface AmendmentResultDto {
  refId: string;
  outcome: SaveOutcomeDto;
  errorCategory?: MeInvoiceErrorCategory;
  rawErrorCode?: string;
  errorMessage?: string;
  failures?: readonly ValidationFailureDto[];
  orgRefId?: string;
}
