import type { TemplateDto } from '../../domain/dtos.js';
import type { GatewayCallOptions, MeInvoiceGateway } from '../ports.js';

/** Port `ListActiveTemplates.cs`: all templates from MISA, filtered to `isActive`. */
export async function listActiveTemplates(
  gateway: MeInvoiceGateway,
  withCode: boolean,
  opts?: GatewayCallOptions,
): Promise<TemplateDto[]> {
  const all = await gateway.listTemplates(withCode, opts);
  return all.filter((t) => t.isActive);
}
