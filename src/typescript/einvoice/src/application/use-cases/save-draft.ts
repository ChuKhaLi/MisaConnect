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
      rawErrorCode: 'ValidationFailed',
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

/** Prefixes `invoices[i].`; the invoice root (`$` or empty) becomes `invoices[i]`. */
const indexed = (index: number, failures: readonly ValidationFailure[]): ValidationFailure[] =>
  failures.map((f) => ({
    ...f,
    fieldPath:
      f.fieldPath === '$' || f.fieldPath === ''
        ? `invoices[${index}]`
        : `invoices[${index}].${f.fieldPath}`,
  }));

/**
 * Port SaveDraftInvoices.cs. Only an empty or oversize batch throws. Each invoice is validated
 * first (deviation: .NET resolves the template first), so a batch with no valid invoice makes no
 * HTTP call; templates are then fetched once and each valid invoice resolved. An invalid or
 * unresolvable invoice becomes its own `Error` result (`ValidationFailed` with
 * `invoices[i].`-prefixed failures) while the rest go out in one insert call, in input order.
 */
export async function saveDraftInvoices(
  deps: SaveDraftDeps,
  invoices: readonly InvoiceDto[],
  withCode: boolean,
  opts?: GatewayCallOptions,
): Promise<SaveDraftResult> {
  if (!Array.isArray(invoices)) {
    throw new MeInvoiceException('Validation', {
      rawErrorCode: 'ValidationFailed',
      message: 'Invoices must be an array.',
      field: 'invoices',
      failures: [
        { fieldPath: 'invoices', message: 'Invoices must be an array.', ruleId: 'FR-006' },
      ],
    });
  }
  assertBatchSize(invoices.length);

  const results: (SaveResultDto | undefined)[] = new Array<SaveResultDto | undefined>(
    invoices.length,
  );
  const valid: { index: number; invoice: InvoiceDto; refId: string }[] = [];
  invoices.forEach((inv, index) => {
    const isObject = typeof inv === 'object' && inv !== null;
    const refId =
      isObject && typeof inv.refId === 'string' && inv.refId !== ''
        ? inv.refId
        : deps.refIdGenerator();
    const failures: readonly ValidationFailure[] = isObject
      ? deps.validator.validate({ ...inv, refId })
      : [{ fieldPath: '$', message: 'Invoice is null.', ruleId: 'FR-006' }];
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
    valid.push({ index, invoice: inv, refId });
  });

  const toSubmit: { index: number; invoice: InvoiceDto; refId: string; template: TemplateDto }[] =
    [];
  if (valid.length > 0) {
    const active = await listActiveTemplates(deps.gateway, withCode, opts);
    for (const v of valid) {
      const resolution = deps.templateResolver(active, v.invoice.template);
      if (resolution.kind === 'Resolved') {
        toSubmit.push({ ...v, template: resolution.template });
        continue;
      }
      const ex = templateResolutionError(resolution);
      results[v.index] = {
        refId: v.refId,
        outcome: 'Error',
        errorCategory: ex.category,
        rawErrorCode: resolution.kind,
        errorMessage: ex.message,
      };
    }
  }

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
