import type { TicketTemplateDto, TicketTemplateListRequestDto } from '../../domain/dtos.js';
import { MeInvoiceException } from '../../domain/errors.js';
import { validateTicketTemplateList } from '../invoice-validator.js';
import type { GatewayCallOptions, MeInvoiceGateway } from '../ports.js';

/**
 * Port `ListTicketTemplates.cs`: fills defaults, validates (first violation only, `Validation`
 * before any HTTP), then lists one page. No active-only filter: the endpoint is paged.
 */
export async function listTicketTemplates(
  gateway: MeInvoiceGateway,
  request: TicketTemplateListRequestDto | undefined,
  opts?: GatewayCallOptions,
): Promise<TicketTemplateDto[]> {
  if (request !== undefined && (typeof request !== 'object' || request === null)) {
    throw new MeInvoiceException('Validation', {
      rawErrorCode: 'ValidationFailed',
      message: 'Ticket template request must be an object.',
      field: 'request',
      failures: [
        {
          fieldPath: 'request',
          message: 'Ticket template request must be an object.',
          ruleId: 'FR-047',
        },
      ],
    });
  }
  const resolved: Required<TicketTemplateListRequestDto> = {
    withCode: request?.withCode ?? true,
    start: request?.start ?? 0,
    length: request?.length ?? 20,
    fromCashRegister: request?.fromCashRegister ?? false,
  };
  const [failure] = validateTicketTemplateList(resolved);
  if (failure !== undefined) {
    throw new MeInvoiceException('Validation', {
      rawErrorCode: 'ValidationFailed',
      message: failure.message,
      field: failure.fieldPath,
      failures: [failure],
    });
  }
  return gateway.listTicketTemplates(resolved, opts);
}
