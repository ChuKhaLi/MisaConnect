import type { Logger } from '../application/ports.js';
import { MeInvoiceErrorCategory, MeInvoiceException } from '../domain/errors.js';
import { scrub } from './logging.js';

/** Per-call identifiers; the action may fill them in once known (e.g. a generated RefId). */
export interface CallLogContext {
  refId?: string;
  templateId?: string;
  /**
   * Set by an outcome-returning action whose result is a failure status (it does not throw);
   * the call is then logged as `Error` with this category.
   */
  failureCategory?: MeInvoiceErrorCategory;
  /** Operation-specific plain fields (e.g. amendments: eInvoiceStatus, orgRefId), logged after templateId. */
  extra?: Record<string, unknown>;
}

const FAILURE_CATEGORY_BY_STATUS: Readonly<Record<string, MeInvoiceErrorCategory>> = {
  AuthFailed: MeInvoiceErrorCategory.Authentication,
  Configuration: MeInvoiceErrorCategory.Configuration,
  MisaThrottled: MeInvoiceErrorCategory.MisaThrottled,
  MisaUnavailable: MeInvoiceErrorCategory.MisaUnavailable,
  TransportFailed: MeInvoiceErrorCategory.TransportFailed,
  MisaUnknown: MeInvoiceErrorCategory.MisaUnknown,
  Validation: MeInvoiceErrorCategory.Validation,
};

/**
 * Rule for outcome-returning operations: a status is a failure (category returned) when it
 * is a transport / auth / config / MISA / validation problem. Business results — `Found`,
 * `NotFound`, `Deleted`, `NotDeletable` — are not, so they stay logged as `Success`.
 */
export function failureCategoryOfStatus(status: string): MeInvoiceErrorCategory | undefined {
  return FAILURE_CATEGORY_BY_STATUS[status];
}

/** First failure category among the statuses, in order; `undefined` when none failed. */
export function firstFailureCategory(
  statuses: Iterable<string>,
): MeInvoiceErrorCategory | undefined {
  for (const status of statuses) {
    const category = failureCategoryOfStatus(status);
    if (category !== undefined) return category;
  }
  return undefined;
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
    if (ctx.extra !== undefined) Object.assign(fields, ctx.extra);
    fields['correlationId'] = correlationId;
    logger.info('MeInvoice call completed', scrub(fields));
  };
  try {
    const result = await action();
    if (ctx.failureCategory !== undefined) emit('Error', ctx.failureCategory);
    else emit('Success');
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
