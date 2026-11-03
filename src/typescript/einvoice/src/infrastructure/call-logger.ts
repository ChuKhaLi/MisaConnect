import type { Logger } from '../application/ports.js';
import { MeInvoiceErrorCategory, MeInvoiceException } from '../domain/errors.js';
import { scrub } from './logging.js';

/** Per-call identifiers; the action may fill them in once known (e.g. a generated RefId). */
export interface CallLogContext {
  refId?: string;
  templateId?: string;
}

/** Port `MeInvoiceCallLogger` batch label: one id as-is, more as `"{first}+N-more"`. */
export function refIdLabel(refIds: readonly string[]): string | undefined {
  const [first] = refIds;
  if (first === undefined) return undefined;
  return refIds.length === 1 ? first : `${first}+${refIds.length - 1}-more`;
}

/**
 * Runs `action` and emits one `info` line `MeInvoice call completed` with
 * `{ endpoint, outcome, errorCategory?, durationMs, refId?, templateId?, correlationId }`.
 * Deliberately never logs the customer tax code (spec §9.2), buyer data, amounts or
 * error messages. Only plain primitives are passed to the logger.
 */
export async function logCall<T>(
  logger: Logger,
  endpoint: string,
  correlationId: string,
  ctx: CallLogContext,
  action: () => Promise<T>,
): Promise<T> {
  const started = performance.now();
  const emit = (outcome: 'Success' | 'Error', errorCategory?: MeInvoiceErrorCategory): void => {
    const fields: Record<string, unknown> = { endpoint, outcome };
    if (errorCategory !== undefined) fields['errorCategory'] = errorCategory;
    fields['durationMs'] = Math.round(performance.now() - started);
    if (ctx.refId !== undefined) fields['refId'] = ctx.refId;
    if (ctx.templateId !== undefined) fields['templateId'] = ctx.templateId;
    fields['correlationId'] = correlationId;
    logger.info('MeInvoice call completed', scrub(fields));
  };
  try {
    const result = await action();
    emit('Success');
    return result;
  } catch (err) {
    if (err instanceof MeInvoiceException) {
      if (ctx.refId === undefined && err.refId !== undefined) ctx.refId = err.refId;
      emit('Error', err.category);
    } else {
      emit('Error', MeInvoiceErrorCategory.MisaUnknown);
    }
    throw err;
  }
}
