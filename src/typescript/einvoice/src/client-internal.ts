import { deleteDraftInvoice } from './application/use-cases/delete-draft.js';
import { getDraftPdf } from './application/use-cases/get-draft-pdf.js';
import { listActiveTemplates } from './application/use-cases/list-templates.js';
import { lookupByRefIds } from './application/use-cases/lookup-by-ref-ids.js';
import { lookupPaged } from './application/use-cases/lookup-paged.js';
import { issueAmendment } from './application/use-cases/issue-amendment.js';
import { saveDraftInvoices } from './application/use-cases/save-draft.js';
import { previewInvoice } from './application/use-cases/preview.js';
import type {
  AdjustmentRequestDto,
  AmendmentResultDto,
  ReplacementRequestDto,
} from './domain/dtos.js';
import {
  failureCategoryOfStatus,
  firstFailureCategory,
  logCall,
  refIdLabel,
  type CallLogContext,
} from './infrastructure/call-logger.js';
import { createMeInvoiceGateway } from './infrastructure/gateway.js';
import { validateOptions, type MisaEInvoiceOptions } from './infrastructure/options.js';
import { createTransport } from './infrastructure/transport.js';
import type { CallOptions, IMisaEInvoiceClient } from './client.js';

const signalOf = (opts?: CallOptions): { signal?: AbortSignal } =>
  opts?.signal !== undefined ? { signal: opts.signal } : {};

/** Internal-only switches; never reachable from the public factory or its options object. */
export interface InternalClientOptions {
  /** Skips the https + MISA-host pinning (tests against a local fake server only). */
  allowAnyBaseUrl: boolean;
}

/**
 * Builds the client. Not exported from the package entry point: only `createMisaEInvoiceClient`
 * (which always pins the base URL) and in-repo tests call it.
 */
export function createMisaEInvoiceClientInternal(
  options: MisaEInvoiceOptions,
  internal: InternalClientOptions,
): IMisaEInvoiceClient {
  const resolved = validateOptions(options, { allowAnyBaseUrl: internal.allowAnyBaseUrl });
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
      opts?.signal,
    );

  const amend = (
    endpoint: 'IssueReplacement' | 'IssueAdjustment',
    kind: 'replacement' | 'adjustment',
    request: ReplacementRequestDto | AdjustmentRequestDto,
    opts: CallOptions | undefined,
  ): Promise<AmendmentResultDto> => {
    // Port MeInvoiceCallLogger.WrapAmendment: eInvoiceStatus + orgRefId always, changeReason
    // only with delete.includeRawErrorMessage.
    const extra: Record<string, unknown> = {
      eInvoiceStatus: kind === 'replacement' ? '3' : '4',
    };
    const orgRefId = request?.originalRef?.orgRefID;
    if (orgRefId) extra['orgRefId'] = orgRefId;
    if (resolved.delete.includeRawErrorMessage) extra['changeReason'] = request?.changeReason ?? '';
    const ctx: CallLogContext = { extra };
    if (request?.invoice?.refId) ctx.refId = request.invoice.refId;
    if (request?.invoice?.template?.ipTemplateID) {
      ctx.templateId = request.invoice.template.ipTemplateID;
    }
    return run(endpoint, opts, ctx, async () => {
      const { result, templateId } = await issueAmendment(
        {
          gateway,
          validator: resolved.validator,
          templateResolver: resolved.templateResolver,
          refIdGenerator: resolved.refIdGenerator,
        },
        kind,
        request,
        signalOf(opts),
      );
      ctx.refId = result.refId;
      if (templateId !== undefined) ctx.templateId = templateId;
      if (result.outcome === 'Error' && result.errorCategory !== undefined) {
        ctx.failureCategory = result.errorCategory;
      }
      return result;
    });
  };

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
        if (result.templateId !== undefined) ctx.templateId = result.templateId;
        return result.results;
      });
    },

    deleteDraft: (refId, invoiceWithCode, opts) => {
      const ctx: CallLogContext = refId ? { refId } : {};
      return run('DeleteDraft', opts, ctx, async () => {
        const outcome = await deleteDraftInvoice(
          {
            gateway,
            logger: resolved.logger,
            includeRawErrorMessage: resolved.delete.includeRawErrorMessage,
          },
          refId,
          invoiceWithCode,
          signalOf(opts),
        );
        const failure = failureCategoryOfStatus(outcome.status);
        if (failure !== undefined) ctx.failureCategory = failure;
        return outcome;
      });
    },

    lookupByRefId: (refIds, withCode, opts) => {
      const ctx: CallLogContext = {};
      const label = refIdLabel(Array.isArray(refIds) ? refIds.filter((r) => !!r) : []);
      if (label !== undefined) ctx.refId = label;
      return run('LookupByRefId', opts, ctx, async () => {
        const result = await lookupByRefIds(
          { gateway, includeRawErrorMessage: resolved.delete.includeRawErrorMessage },
          refIds,
          withCode,
          signalOf(opts),
        );
        // Failure rule: batch Validation, else the first per-RefID transport/auth/config/MISA failure.
        const failure =
          result.status === 'Validation'
            ? 'Validation'
            : firstFailureCategory((result.outcomes ?? []).map((o) => o.status));
        if (failure !== undefined) ctx.failureCategory = failure;
        return result;
      });
    },

    lookupStandard: (request, withCode, opts) =>
      run('LookupStandard', opts, {}, () =>
        lookupPaged(gateway, 'standard', request, withCode, signalOf(opts)),
      ),

    lookupCalculating: (request, withCode, opts) =>
      run('LookupCalculating', opts, {}, () =>
        lookupPaged(gateway, 'calculating', request, withCode, signalOf(opts)),
      ),

    issueReplacement: (request, opts) => amend('IssueReplacement', 'replacement', request, opts),
    issueAdjustment: (request, opts) => amend('IssueAdjustment', 'adjustment', request, opts),
  };
}
