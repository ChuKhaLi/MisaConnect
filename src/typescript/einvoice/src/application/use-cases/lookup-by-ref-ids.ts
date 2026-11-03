import type { LookupBatchOutcomeDto, LookupOutcomeDto } from '../../domain/dtos.js';
import { MeInvoiceException, type MeInvoiceErrorCategory } from '../../domain/errors.js';
import type { LookupStatus } from '../../domain/statuses.js';
import { mapErrorCode } from '../error-mapper.js';
import type { GatewayCallOptions, MeInvoiceGateway } from '../ports.js';

export interface LookupByRefIdsDeps {
  gateway: MeInvoiceGateway;
  /** Lookups reuse `delete.includeRawErrorMessage` (as in .NET `IDeleteOptionsAccessor`). */
  includeRawErrorMessage: boolean;
}

/** Port `LookupByRefIds.MaxBatchSize`. */
export const MAX_LOOKUP_BATCH_SIZE = 50;
const MAX_REF_ID_LENGTH = 64;

const STATUS_BY_CATEGORY: Readonly<Partial<Record<MeInvoiceErrorCategory, LookupStatus>>> = {
  Authentication: 'AuthFailed',
  Configuration: 'Configuration',
  MisaThrottled: 'MisaThrottled',
  MisaUnavailable: 'MisaUnavailable',
  TransportFailed: 'TransportFailed',
  ResourceNotFound: 'NotFound',
  Validation: 'Validation',
};

const statusOf = (category: MeInvoiceErrorCategory): LookupStatus =>
  STATUS_BY_CATEGORY[category] ?? 'MisaUnknown';

/** A failure in one chunk is terminal (copied to every remaining chunk) for these statuses (R-LU-10). */
const isTerminal = (status: LookupStatus): boolean =>
  status === 'AuthFailed' || status === 'Configuration';

interface ChunkFailure {
  status: LookupStatus;
  rawErrorCode: string;
  message: string | undefined;
}

/**
 * Port LookupByRefIds.cs plus the facade's RefId checks. An empty / non-array list returns a
 * `Validation` batch outcome without HTTP; an individual empty or over-long RefId throws
 * `Validation` (`RefIdInvalid`, as the .NET facade does). Chunks of 50 are sent sequentially;
 * outcomes keep input order; a terminal failure is copied to all remaining chunks without
 * further requests. Caller aborts reject with the native `AbortError`.
 */
export async function lookupByRefIds(
  deps: LookupByRefIdsDeps,
  refIds: readonly string[],
  withCode: boolean,
  opts?: GatewayCallOptions,
): Promise<LookupBatchOutcomeDto> {
  if (!Array.isArray(refIds)) {
    return { status: 'Validation', reason: 'RefIds list cannot be null.' };
  }
  if (refIds.length === 0) {
    return { status: 'Validation', reason: 'RefIds list cannot be empty.' };
  }
  for (const id of refIds) {
    if (typeof id !== 'string' || id === '' || id.length > MAX_REF_ID_LENGTH) {
      throw new MeInvoiceException('Validation', {
        rawErrorCode: 'RefIdInvalid',
        message:
          typeof id === 'string' && id.length > MAX_REF_ID_LENGTH
            ? `RefId value length ${id.length} exceeds the maximum of ${MAX_REF_ID_LENGTH}.`
            : 'RefId value must be non-empty.',
      });
    }
  }

  const includeMessage = deps.includeRawErrorMessage;
  const failed = (refId: string, f: ChunkFailure): LookupOutcomeDto => ({
    refId,
    status: f.status,
    ...(f.rawErrorCode !== '' ? { errorCode: f.rawErrorCode } : {}),
    ...(includeMessage && f.message !== undefined ? { message: f.message } : {}),
  });

  const outcomes: LookupOutcomeDto[] = [];
  let terminal: ChunkFailure | undefined;

  for (let offset = 0; offset < refIds.length; offset += MAX_LOOKUP_BATCH_SIZE) {
    const chunk = refIds.slice(offset, offset + MAX_LOOKUP_BATCH_SIZE);

    let failure: ChunkFailure | undefined = terminal;
    if (failure === undefined) {
      try {
        const result = await deps.gateway.lookupByRefIdsChunk(chunk, withCode, opts);
        if (result.errorCode !== undefined && result.errorCode !== '') {
          const mapped = mapErrorCode(result.errorCode, result.errorMessage);
          failure = {
            status: statusOf(mapped.category),
            rawErrorCode: result.errorCode,
            message: result.errorMessage,
          };
        } else {
          const byRefId = new Map(result.snapshots.map((s) => [s.refId, s] as const));
          for (const refId of chunk) {
            const snapshot = byRefId.get(refId);
            outcomes.push(
              snapshot !== undefined
                ? { refId, status: 'Found', snapshot }
                : { refId, status: 'NotFound' },
            );
          }
          continue;
        }
      } catch (err) {
        if (!(err instanceof MeInvoiceException)) throw err;
        failure = {
          status: statusOf(err.category),
          rawErrorCode: err.rawErrorCode ?? '',
          message: err.message,
        };
      }
      if (isTerminal(failure.status)) terminal = failure;
    }
    for (const refId of chunk) outcomes.push(failed(refId, failure));
  }

  return { status: 'Completed', outcomes };
}
