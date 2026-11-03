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
  /** Id of the first resolved template, if any (for logging). */
  templateId?: string;
}

/**
 * Port SaveDraftInvoices.cs. Deviation: the whole batch is validated first and any
 * failure throws Validation before any HTTP call. Templates are fetched once and each
 * invoice is resolved against them; unresolvable invoices become their own Error result
 * while the rest go out in one insert call.
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
  const results: (SaveResultDto | undefined)[] = new Array<SaveResultDto | undefined>(
    working.length,
  );
  const toSubmit: { index: number; invoice: InvoiceDto; refId: string; template: TemplateDto }[] =
    [];
  working.forEach((w, index) => {
    const resolution = deps.templateResolver(active, w.invoice.template);
    if (resolution.kind === 'Resolved') {
      toSubmit.push({ index, ...w, template: resolution.template });
      return;
    }
    const ex = templateResolutionError(resolution);
    results[index] = {
      refId: w.refId,
      outcome: 'Error',
      errorCategory: ex.category,
      rawErrorCode: resolution.kind,
      errorMessage: ex.message,
    };
  });

  if (toSubmit.length > 0) {
    const outcomes = await deps.gateway.insertInvoices(toSubmit, withCode, opts);
    toSubmit.forEach((item, k) => {
      const o = outcomes[k];
      results[item.index] =
        o === undefined || o.outcome === 'Success'
          ? { refId: item.refId, outcome: 'Success' }
          : {
              refId: item.refId,
              outcome: 'Error',
              errorCategory: o.errorCategory,
              rawErrorCode: o.rawErrorCode,
              ...(o.message !== undefined ? { errorMessage: o.message } : {}),
            };
    });
  }

  const first = toSubmit[0]?.template.ipTemplateID;
  return {
    results: results as SaveResultDto[],
    ...(first !== undefined ? { templateId: first } : {}),
  };
}
