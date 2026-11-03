import type { PdfDocumentDto } from '../../domain/dtos.js';
import { MeInvoiceException } from '../../domain/errors.js';
import type { GatewayCallOptions, MeInvoiceGateway } from '../ports.js';

const PDF_MAGIC = [0x25, 0x50, 0x44, 0x46, 0x2d]; // "%PDF-"

export const PDF_CONTENT_TYPE = 'application/pdf';

/** Port `GetDraftPdfByRefId.cs`. */
export async function getDraftPdf(
  gateway: MeInvoiceGateway,
  refId: string,
  withCode: boolean,
  opts?: GatewayCallOptions,
): Promise<PdfDocumentDto> {
  if (typeof refId !== 'string' || refId.trim() === '') {
    throw new MeInvoiceException('Validation', {
      rawErrorCode: 'RefIdRequired',
      message: 'RefId must be a non-empty string.',
    });
  }
  const content = await gateway.viewDraftPdf(refId, withCode, opts);
  if (content.length < PDF_MAGIC.length || PDF_MAGIC.some((b, i) => content[i] !== b)) {
    throw new MeInvoiceException('MisaUnknown', {
      rawErrorCode: 'InvalidPdfContent',
      message: 'Response did not contain a PDF document.',
      refId,
    });
  }
  return { content, contentType: PDF_CONTENT_TYPE };
}
