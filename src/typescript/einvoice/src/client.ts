import { createMisaEInvoiceClientInternal } from './client-internal.js';
import type {
  AdjustmentRequestDto,
  AmendmentResultDto,
  InvoiceDto,
  LookupBatchOutcomeDto,
  PagedLookupRequestDto,
  PagedLookupResultDto,
  PdfDocumentDto,
  ReplacementRequestDto,
  SaveResultDto,
  TemplateDto,
} from './domain/dtos.js';
import type { DeleteDraftOutcome } from './domain/statuses.js';
import type { MisaEInvoiceOptions } from './infrastructure/options.js';

/** Optional last argument of every client method. */
export interface CallOptions {
  /** Aborts the call; rejects with the native `AbortError`, never retried or wrapped. */
  signal?: AbortSignal;
  /** Overrides the `correlationId` option for this call's log line. */
  correlationId?: string;
}

/** Port of the .NET `IMisaEInvoiceClient` facade. */
export interface IMisaEInvoiceClient {
  listTemplates(withCode?: boolean, opts?: CallOptions): Promise<TemplateDto[]>;
  preview(invoice: InvoiceDto, withCode?: boolean, opts?: CallOptions): Promise<PdfDocumentDto>;
  saveDraft(
    invoices: readonly InvoiceDto[],
    withCode?: boolean,
    opts?: CallOptions,
  ): Promise<SaveResultDto[]>;
  getDraftPdf(refId: string, withCode?: boolean, opts?: CallOptions): Promise<PdfDocumentDto>;
  deleteDraft(
    refId: string,
    invoiceWithCode: boolean,
    opts?: CallOptions,
  ): Promise<DeleteDraftOutcome>;
  lookupByRefId(
    refIds: readonly string[],
    withCode: boolean,
    opts?: CallOptions,
  ): Promise<LookupBatchOutcomeDto>;
  lookupStandard(
    request: PagedLookupRequestDto,
    withCode: boolean,
    opts?: CallOptions,
  ): Promise<PagedLookupResultDto>;
  lookupCalculating(
    request: PagedLookupRequestDto,
    withCode: boolean,
    opts?: CallOptions,
  ): Promise<PagedLookupResultDto>;
  issueReplacement(request: ReplacementRequestDto, opts?: CallOptions): Promise<AmendmentResultDto>;
  issueAdjustment(request: AdjustmentRequestDto, opts?: CallOptions): Promise<AmendmentResultDto>;
}

/**
 * Validates options (throws `Configuration`) and builds the client. The base URL is always
 * pinned to https and the MISA host of the chosen environment.
 */
export function createMisaEInvoiceClient(options: MisaEInvoiceOptions): IMisaEInvoiceClient {
  return createMisaEInvoiceClientInternal(options, { allowAnyBaseUrl: false });
}
