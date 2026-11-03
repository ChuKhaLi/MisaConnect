import type { InvoiceDto, SaveResultDto, TemplateDto } from '../../domain/dtos.js';
import { MeInvoiceException } from '../../domain/errors.js';
import type {
  GatewayCallOptions,
  InvoiceValidator,
  MeInvoiceGateway,
  RefIdGenerator,
  TemplateResolver,
} from '../ports.js';
import { templateResolutionError } from '../template-resolver.js';
import { listActiveTemplates } from './list-templates.js';

export interface SaveDraftDeps {
  gateway: MeInvoiceGateway;
  validator: InvoiceValidator;
  templateResolver: TemplateResolver;
  refIdGenerator: RefIdGenerator;
}

export interface SaveDraftResult {
  results: SaveResultDto[];
  template: TemplateDto;
}

/**
 * Port `SaveDraftInvoices.cs`. Deviation (as in preview): the whole batch is validated
 * first and any failure throws `Validation` before any HTTP call. The template is
 * resolved once, from the first invoice's template reference.
 */
export async function saveDraftInvoices(
  deps: SaveDraftDeps,
  invoices: readonly InvoiceDto[],
  withCode: boolean,
  opts?: GatewayCallOptions,
): Promise<SaveDraftResult> {
  const working = invoices.map((inv) => ({
    invoice: inv,
    refId: inv.refId ? inv.refId : deps.refIdGenerator(),
  }));

  const failures = deps.validator.validateBatch(
    working.map((w) => ({ ...w.invoice, refId: w.refId })),
  );
  if (failures.length > 0) {
    throw new MeInvoiceException('Validation', { failures });
  }

  const active = await listActiveTemplates(deps.gateway, withCode, opts);
  const resolution = deps.templateResolver(active, invoices[0]?.template);
  if (resolution.kind !== 'Resolved') throw templateResolutionError(resolution);
  const template = resolution.template;

  const outcomes = await deps.gateway.insertInvoices(working, template, withCode, opts);
  const results = outcomes.map((o): SaveResultDto => {
    if (o.outcome === 'Success') return { refId: o.refId, outcome: 'Success' };
    return {
      refId: o.refId,
      outcome: 'Error',
      errorCategory: o.errorCategory,
      rawErrorCode: o.rawErrorCode,
      ...(o.message !== undefined ? { errorMessage: o.message } : {}),
    };
  });
  return { results, template };
}
