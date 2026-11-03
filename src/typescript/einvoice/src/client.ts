import { getDraftPdf } from './application/use-cases/get-draft-pdf.js';
import { listActiveTemplates } from './application/use-cases/list-templates.js';
import { saveDraftInvoices } from './application/use-cases/save-draft.js';
import { previewInvoice } from './application/use-cases/preview.js';
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
import { logCall, refIdLabel, type CallLogContext } from './infrastructure/call-logger.js';
import { createMeInvoiceGateway } from './infrastructure/gateway.js';
import { validateOptions, type MisaEInvoiceOptions } from './infrastructure/options.js';
import { createTransport } from './infrastructure/transport.js';

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

/** Test-only escape hatch (fake server on http://127.0.0.1); not part of the public type. */
type InternalOptions = MisaEInvoiceOptions & { __unsafeAllowAnyBaseUrl?: boolean };

const notImplemented = (): Promise<never> => Promise.reject(new Error('not implemented'));

const signalOf = (opts?: CallOptions): { signal?: AbortSignal } =>
  opts?.signal !== undefined ? { signal: opts.signal } : {};

/** Validates options (throws `Configuration`) and wires transport → gateway → use cases. */
export function createMisaEInvoiceClient(options: MisaEInvoiceOptions): IMisaEInvoiceClient {
  const resolved = validateOptions(options, {
    allowAnyBaseUrl: (options as InternalOptions).__unsafeAllowAnyBaseUrl === true,
  });
  const gateway = createMeInvoiceGateway(createTransport(resolved), resolved);

  const run = <T>(
    endpoint: string,
    opts: CallOptions | undefined,
    ctx: CallLogContext,
    action: () => Promise<T>,
  ): Promise<T> =>
    logCall(
      resolved.logger,
      endpoint,
      opts?.correlationId ?? resolved.correlationId(),
      ctx,
      action,
    );

  return {
    listTemplates: (withCode = true, opts) =>
      run('ListTemplates', opts, {}, () => listActiveTemplates(gateway, withCode, signalOf(opts))),

    preview: (invoice, withCode = true, opts) => {
      const ctx: CallLogContext = {};
      if (invoice?.refId) ctx.refId = invoice.refId;
      if (invoice?.template?.ipTemplateID) ctx.templateId = invoice.template.ipTemplateID;
      return run('Preview', opts, ctx, async () => {
        const result = await previewInvoice(
          {
            gateway,
            validator: resolved.validator,
            templateResolver: resolved.templateResolver,
            refIdGenerator: resolved.refIdGenerator,
          },
          invoice,
          withCode,
          signalOf(opts),
        );
        ctx.refId = result.refId;
        ctx.templateId = result.template.ipTemplateID;
        return result.pdf;
      });
    },

    getDraftPdf: (refId, withCode = true, opts) =>
      run('GetDraftPdfByRefId', opts, refId ? { refId } : {}, () =>
        getDraftPdf(gateway, refId, withCode, signalOf(opts)),
      ),

    saveDraft: (invoices, withCode = true, opts) => {
      const ctx: CallLogContext = {};
      const label = refIdLabel(
        (invoices ?? []).map((i) => i?.refId).filter((r): r is string => !!r),
      );
      if (label !== undefined) ctx.refId = label;
      const tpl = invoices?.[0]?.template?.ipTemplateID;
      if (tpl) ctx.templateId = tpl;
      return run('SaveDraft', opts, ctx, async () => {
        const result = await saveDraftInvoices(
          {
            gateway,
            validator: resolved.validator,
            templateResolver: resolved.templateResolver,
            refIdGenerator: resolved.refIdGenerator,
          },
          invoices,
          withCode,
          signalOf(opts),
        );
        const sent = refIdLabel(result.results.map((r) => r.refId));
        if (sent !== undefined) ctx.refId = sent;
        ctx.templateId = result.template.ipTemplateID;
        return result.results;
      });
    },
    deleteDraft: notImplemented,
    lookupByRefId: notImplemented,
    lookupStandard: notImplemented,
    lookupCalculating: notImplemented,
    issueReplacement: notImplemented,
    issueAdjustment: notImplemented,
  };
}
