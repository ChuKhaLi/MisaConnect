import type { PagedLookupRequestDto, PagedLookupResultDto } from '../../domain/dtos.js';
import { MeInvoiceException } from '../../domain/errors.js';
import { validatePagedLookup } from '../invoice-validator.js';
import type { GatewayCallOptions, MeInvoiceGateway, PagedLookupKind } from '../ports.js';

/**
 * Port LookupStandard.cs / LookupCalculating.cs: validates the request (first violation only,
 * `Validation` before any HTTP), then posts to the standard or calculating paging route.
 */
export async function lookupPaged(
  gateway: MeInvoiceGateway,
  kind: PagedLookupKind,
  request: PagedLookupRequestDto,
  withCode: boolean,
  opts?: GatewayCallOptions,
): Promise<PagedLookupResultDto> {
  if (typeof request !== 'object' || request === null) {
    throw new MeInvoiceException('Validation', {
      rawErrorCode: 'ValidationFailed',
      message: 'Lookup request is required.',
      field: 'request',
      failures: [
        { fieldPath: 'request', message: 'Lookup request is required.', ruleId: 'FR-047' },
      ],
    });
  }
  const [failure] = validatePagedLookup(request);
  if (failure !== undefined) {
    throw new MeInvoiceException('Validation', {
      rawErrorCode: 'ValidationFailed',
      message: failure.message,
      field: failure.fieldPath,
      failures: [failure],
    });
  }
  const items = await gateway.lookupPaged(kind, request, withCode, opts);
  return {
    items,
    start: request.start,
    length: request.length,
    returnedCount: items.length,
  };
}
