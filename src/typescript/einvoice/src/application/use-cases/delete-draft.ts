import { MeInvoiceException } from '../../domain/errors.js';
import type { DeleteDraftOutcome, DeleteDraftStatus } from '../../domain/statuses.js';
import { disambiguateInvalidTransaction } from '../delete-disambiguator.js';
import { mapErrorCode } from '../error-mapper.js';
import type { GatewayCallOptions, Logger, MeInvoiceGateway } from '../ports.js';

export interface DeleteDraftDeps {
  gateway: MeInvoiceGateway;
  logger: Logger;
  /** `delete.includeRawErrorMessage` (spec §9.1). */
  includeRawErrorMessage: boolean;
  /** The call's correlation id, attached to the ARCH-DEL-001 warning. */
  correlationId?: string;
}

const MAX_REF_ID_LENGTH = 64;
const EXCERPT_LENGTH = 80;

const STATUS_BY_CATEGORY: Readonly<Record<string, DeleteDraftStatus>> = {
  Authentication: 'AuthFailed',
  Configuration: 'Configuration',
  MisaThrottled: 'MisaThrottled',
  MisaUnavailable: 'MisaUnavailable',
  TransportFailed: 'TransportFailed',
  ResourceNotFound: 'NotFound',
  NotDeletable: 'NotDeletable',
};

function outcome(
  status: DeleteDraftStatus,
  refId: string,
  rawErrorCode: string | undefined,
  message: string | undefined,
  field: string | undefined,
  includeMessage: boolean,
): DeleteDraftOutcome {
  return {
    status,
    ...(rawErrorCode !== undefined && rawErrorCode !== '' ? { rawErrorCode } : {}),
    ...(includeMessage && message !== undefined ? { message } : {}),
    ...(field !== undefined ? { field } : {}),
    refId,
  };
}

/**
 * Port DeleteDraftInvoice.cs (plus the facade's RefId validation). Never throws for
 * MISA or transport errors — they become a `DeleteDraftOutcome`; an invalid RefId throws
 * `Validation` and a caller abort rejects with the native `AbortError`.
 */
export async function deleteDraftInvoice(
  deps: DeleteDraftDeps,
  refId: string,
  invoiceWithCode: boolean,
  opts?: GatewayCallOptions,
): Promise<DeleteDraftOutcome> {
  if (typeof refId !== 'string' || refId === '' || refId.length > MAX_REF_ID_LENGTH) {
    throw new MeInvoiceException('Validation', {
      rawErrorCode: 'RefIdInvalid',
      message:
        typeof refId === 'string' && refId.length > MAX_REF_ID_LENGTH
          ? `RefId value length ${refId.length} exceeds the maximum of ${MAX_REF_ID_LENGTH}.`
          : 'RefId value must be non-empty.',
    });
  }
  const includeMessage = deps.includeRawErrorMessage;

  let response;
  try {
    response = await deps.gateway.deleteDraft(refId, invoiceWithCode, opts);
  } catch (err) {
    if (!(err instanceof MeInvoiceException)) throw err;
    return outcome(
      STATUS_BY_CATEGORY[err.category] ?? 'MisaUnknown',
      refId,
      err.rawErrorCode,
      err.message,
      err.field,
      includeMessage,
    );
  }

  if (response.success) return { status: 'Deleted', refId };

  const rawCode = response.errorCode ?? '';
  if (rawCode.toLowerCase() === 'invalidtransactionid') {
    const kind = disambiguateInvalidTransaction(response.errorMessage) ?? 'NotFound';
    if (kind === 'NotDeletable') {
      return outcome(
        'NotDeletable',
        refId,
        rawCode,
        response.errorMessage,
        undefined,
        includeMessage,
      );
    }
    emitDisambiguationWarningIfNeeded(
      deps.logger,
      response.errorMessage,
      includeMessage,
      deps.correlationId,
    );
    return outcome('NotFound', refId, rawCode, response.errorMessage, undefined, includeMessage);
  }

  const mapped = mapErrorCode(response.errorCode, response.errorMessage);
  return outcome(
    STATUS_BY_CATEGORY[mapped.category] ?? 'MisaUnknown',
    refId,
    mapped.rawCode,
    response.errorMessage,
    mapped.field,
    includeMessage,
  );
}

/** Drift detection: the default NotFound branch was taken without the canonical phrase. */
function emitDisambiguationWarningIfNeeded(
  logger: Logger,
  errorMessage: string | undefined,
  includeMessage: boolean,
  correlationId: string | undefined,
): void {
  if (
    errorMessage !== undefined &&
    errorMessage.trim() !== '' &&
    errorMessage.toLowerCase().includes('không tồn tại')
  ) {
    return;
  }
  const excerpt =
    errorMessage !== undefined && errorMessage !== '' && includeMessage
      ? errorMessage.slice(0, EXCERPT_LENGTH)
      : undefined;
  logger.warn(
    `MeInvoice delete disambiguation default-branch: eventId=ARCH-DEL-001, errorCode=InvalidTransactionID, errorMessageExcerpt=${excerpt ?? ''}`,
    {
      eventId: 'ARCH-DEL-001',
      errorCode: 'InvalidTransactionID',
      ...(excerpt !== undefined ? { errorMessageExcerpt: excerpt } : {}),
      ...(correlationId !== undefined ? { correlationId } : {}),
    },
  );
}
