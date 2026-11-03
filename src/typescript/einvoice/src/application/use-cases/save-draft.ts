import type { InvoiceDto, SaveResultDto, TemplateDto } from '../../domain/dtos.js';
import { MeInvoiceException, type ValidationFailure } from '../../domain/errors.js';
import { mapErrorCode } from '../error-mapper.js';
import { MaxInvoicesPerBatch } from '../invoice-validator.js';
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

const BATCH_FIELD = '$batch.count';

/** Port of the .NET batch-size guard; the only failures that throw instead of becoming results. */
function assertBatchSize(count: number): void {
  if (count < 1) {
    throw new MeInvoiceException('Validation', {
      failures: [
        {
          fieldPath: BATCH_FIELD,
          message: `Batch must contain between 1 and ${MaxInvoicesPerBatch} invoices.`,
          ruleId: 'FR-030',
        },
      ],
    });
  }
  if (count > MaxInvoicesPerBatch) {
    // Category via the error mapper so the code stays consistent with MISA's own answer (BatchOrSize).
    const code = mapErrorCode('InvoiceQuantityTooLarge');
    throw new MeInvoiceException(code.category, {
      rawErrorCode: code.rawCode,
      failures: [
        {
          fieldPath: BATCH_FIELD,
          message: `Batch size ${count} exceeds the MISA limit of ${MaxInvoicesPerBatch} invoices per request.`,
          ruleId: 'FR-030',
        },
      ],
    });
  }
}

const indexed = (index: number, failures: readonly ValidationFailure[]): ValidationFailure[] =>
  failures.map((f) => ({ ...f, fieldPath: `invoices[${index}].${f.fieldPath}` }));

/**
 * Port SaveDraftInvoices.cs. Only an empty or oversize batch throws. Templates are fetched
 * once; each invoice is resolved, then validated, and an unresolvable or invalid invoice
 * becomes its own `Error` result (`ValidationFailed` with `invoices[i].`-prefixed failures)
 * while the rest go out in one insert call. Results follow input order.
 */
export async function saveDraftInvoices(
  deps: SaveDraftDeps,
  invoices: readonly InvoiceDto[],
  withCode: boolean,
  opts?: GatewayCallOptions,
): Promise<SaveDraftResult> {
  assertBatchSize(invoices.length);

  const active = await listActiveTemplates(deps.gateway, withCode, opts);
  const results: (SaveResultDto | undefined)[] = new Array<SaveResultDto | undefined>(
    invoices.length,
  );
  const toSubmit: { index: number; invoice: InvoiceDto; refId: string; template: TemplateDto }[] =
    [];
  invoices.forEach((inv, index) => {
    const refId = inv?.refId ? inv.refId : deps.refIdGenerator();
    const resolution = deps.templateResolver(active, inv?.template);
    if (resolution.kind !== 'Resolved') {
      const ex = templateResolutionError(resolution);
      results[index] = {
        refId,
        outcome: 'Error',
        errorCategory: ex.category,
        rawErrorCode: resolution.kind,
        errorMessage: ex.message,
      };
      return;
    }
    const failures = deps.validator.validate({ ...inv, refId });
    if (failures.length > 0) {
      results[index] = {
        refId,
        outcome: 'Error',
        errorCategory: 'Validation',
        rawErrorCode: 'ValidationFailed',
        failures: indexed(index, failures),
      };
      return;
    }
    toSubmit.push({ index, invoice: inv, refId, template: resolution.template });
  });

  if (toSubmit.length > 0) {
    const outcomes = await deps.gateway.insertInvoices(toSubmit, withCode, opts);
    toSubmit.forEach((item, k) => {
      const o = outcomes[k];
      results[item.index] =
        o === undefined
          ? {
              refId: item.refId,
              outcome: 'Error',
              errorCategory: 'MisaUnknown',
              rawErrorCode: 'NoEchoedResult',
            }
          : o.outcome === 'Success'
            ? { refId: item.refId, outcome: 'Success' }
            : {
                refId: item.refId,
                outcome: 'Error',
                errorCategory: o.errorCategory,
                rawErrorCode: o.rawErrorCode,
                ...(o.message !== undefined ? { errorMessage: o.message } : {}),
                ...(o.failures !== undefined ? { failures: indexed(item.index, o.failures) } : {}),
              };
    });
  }

  const first = toSubmit[0]?.template.ipTemplateID;
  return {
    results: results as SaveResultDto[],
    ...(first !== undefined ? { templateId: first } : {}),
  };
}
