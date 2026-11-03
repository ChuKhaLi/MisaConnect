import type { MeInvoiceGateway, MeInvoiceTransport } from '../application/ports.js';
import { mapErrorCode } from '../application/error-mapper.js';
import type { TemplateDto } from '../domain/dtos.js';
import { MeInvoiceException } from '../domain/errors.js';
import { extractErrorCode, extractErrorMessage, throwFromEnvelope } from './envelope.js';
import type { ResolvedOptions } from './options.js';
import { wireTaxCode } from './transport.js';
import { toWireInvoice } from './wire/invoice.js';
import { fromWireTemplate, type WireTemplate } from './wire/templates.js';

/** Port `MeInvoiceHttpRoutes`. */
export const Routes = Object.freeze({
  Templates: 'webapp/templates',
  Preview: 'webapp/preview',
  ViewRefId: 'webapp/viewrefid',
});

const flag = (b: boolean): string => (b ? 'true' : 'false');

/** Base64 envelope `data` → bytes; anything else is a malformed MISA response. */
function decodeBase64(data: unknown, refId?: string): Uint8Array {
  if (typeof data === 'string') {
    try {
      const bin = atob(data);
      const bytes = new Uint8Array(bin.length);
      for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
      return bytes;
    } catch {
      // fall through
    }
  }
  throw new MeInvoiceException('MisaUnknown', {
    rawErrorCode: 'InvalidResponse',
    message: 'MISA returned data that is not base64 content.',
    ...(refId !== undefined ? { refId } : {}),
  });
}

/** Port of `MeInvoiceClient` (templates, preview, viewrefid). */
export function createMeInvoiceGateway(
  transport: MeInvoiceTransport,
  resolved: ResolvedOptions,
): MeInvoiceGateway {
  return {
    async listTemplates(withCode, opts) {
      const body = {
        TaxCode: wireTaxCode(resolved.taxCode, resolved.appId),
        UserName: resolved.userName,
        Password: resolved.password,
      };
      const envelope = await transport.request<unknown>(
        'POST',
        Routes.Templates,
        { invoiceWithCode: flag(withCode), isTicket: 'false' },
        body,
        opts,
      );
      if (!envelope.success) throwFromEnvelope(envelope);
      const data = envelope.data;
      if (data === undefined) return [];
      if (!Array.isArray(data)) {
        throw new MeInvoiceException('MisaUnknown', {
          rawErrorCode: 'InvalidResponse',
          message: 'MISA returned a templates payload that is not an array.',
        });
      }
      return (data as WireTemplate[]).map((w): TemplateDto => fromWireTemplate(w, withCode));
    },

    async preview(invoice, refId, template, withCode, opts) {
      const envelope = await transport.request<unknown>(
        'POST',
        Routes.Preview,
        { invoiceWithCode: flag(withCode) },
        toWireInvoice(invoice, refId, template),
        opts,
      );
      if (!envelope.success || envelope.data === undefined) throwFromEnvelope(envelope, refId);
      return decodeBase64(envelope.data, refId);
    },

    async viewDraftPdf(refId, withCode, opts) {
      const envelope = await transport.request<unknown>(
        'GET',
        Routes.ViewRefId,
        { invoiceWithCode: flag(withCode), refid: refId },
        undefined,
        opts,
      );
      if (!envelope.success || envelope.data === undefined) {
        // MISA answers success:true with empty data for an unknown RefID; treat both as not-found.
        const msg = extractErrorMessage(envelope);
        const code = mapErrorCode(extractErrorCode(envelope) ?? 'RefIdNotFound', msg);
        const message = code.detail ?? msg;
        throw new MeInvoiceException(code.category, {
          rawErrorCode: code.rawCode,
          ...(message !== undefined ? { message } : {}),
          refId,
        });
      }
      return decodeBase64(envelope.data, refId);
    },
  };
}
