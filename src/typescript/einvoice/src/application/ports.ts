import type { InvoiceDto, TemplateDto, TemplateRefDto } from '../domain/dtos.js';
import type { MeInvoiceErrorCategory, ValidationFailure } from '../domain/errors.js';
import type { TemplateResolution } from './template-resolver.js';

export interface InvoiceValidator {
  validate(invoice: InvoiceDto): ValidationFailure[];
  validateBatch(invoices: InvoiceDto[]): ValidationFailure[];
}

export type TemplateResolver = (
  templates: readonly TemplateDto[],
  ref?: TemplateRefDto,
) => TemplateResolution;

export interface Clock {
  now(): Date;
}

export type RefIdGenerator = () => string;

export interface AccessToken {
  value: string;
  expiresAt: Date;
}

export interface TokenCache {
  get(key: string): Promise<AccessToken | undefined>;
  set(key: string, token: AccessToken): Promise<void>;
  delete(key: string): Promise<void>;
}

export interface Logger {
  debug(message: string, fields?: Record<string, unknown>): void;
  info(message: string, fields?: Record<string, unknown>): void;
  warn(message: string, fields?: Record<string, unknown>): void;
  error(message: string, fields?: Record<string, unknown>): void;
}

export type HttpMethod = 'GET' | 'POST' | 'DELETE';

/**
 * MISA response envelope `{success, data, error, errorCode}`. `data` is already decoded:
 * stringified JSON objects/arrays are parsed, raw arrays kept, other strings (base64 PDF)
 * kept verbatim, null/empty → undefined. `error` / `errorCode` are passed through raw.
 */
export interface Envelope<T> {
  success: boolean;
  data: T | undefined;
  error: unknown;
  errorCode: unknown;
  /** Top-level `ErrorMessage` string (returned by the delete endpoint); absent otherwise. */
  errorMessage?: string;
}

export interface RequestOptions {
  signal?: AbortSignal;
  correlationId?: string;
}

export interface MeInvoiceTransport {
  request<T>(
    method: HttpMethod,
    path: string,
    query: Record<string, string>,
    body?: unknown,
    opts?: RequestOptions,
  ): Promise<Envelope<T>>;
}

/** Per-RefID result of the insert endpoint (port `SaveResult` + `InsertErrorEntryDto`). */
export type InsertOutcome =
  | { refId: string; outcome: 'Success' }
  | {
      refId: string;
      outcome: 'Error';
      errorCategory: MeInvoiceErrorCategory;
      rawErrorCode: string;
      message?: string;
    };

/** Flat shape of MISA's delete response (port `DeleteResponse`). */
export interface DeleteResponse {
  success: boolean;
  errorCode?: string;
  errorMessage?: string;
}

export interface GatewayCallOptions {
  signal?: AbortSignal;
}

/**
 * Domain-level MISA operations (port `IMeInvoiceClient`). The infrastructure adapter
 * owns routes, query strings, wire bodies and envelope interpretation.
 */
export interface MeInvoiceGateway {
  /** All templates (active and inactive), mapped. */
  listTemplates(withCode: boolean, opts?: GatewayCallOptions): Promise<TemplateDto[]>;
  /** PDF bytes of the rendered preview. */
  preview(
    invoice: InvoiceDto,
    refId: string,
    template: TemplateDto,
    withCode: boolean,
    opts?: GatewayCallOptions,
  ): Promise<Uint8Array>;
  /** PDF bytes of a stored draft; unknown RefID → `ResourceNotFound`. */
  viewDraftPdf(refId: string, withCode: boolean, opts?: GatewayCallOptions): Promise<Uint8Array>;
  /**
   * Deletes a draft. MISA-level failures come back as `success: false`; transport and
   * auth problems throw `MeInvoiceException`.
   */
  deleteDraft(
    refId: string,
    invoiceWithCode: boolean,
    opts?: GatewayCallOptions,
  ): Promise<DeleteResponse>;
  /**
   * Inserts the drafts in one request; returns one outcome per item in input order.
   * A RefID MISA does not echo yields `MisaUnknown` / `NoEchoedResult`.
   */
  insertInvoices(
    items: readonly { invoice: InvoiceDto; refId: string; template: TemplateDto }[],
    withCode: boolean,
    opts?: GatewayCallOptions,
  ): Promise<InsertOutcome[]>;
}
