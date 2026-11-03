import type { InvoiceDto, PdfDocumentDto, TemplateDto } from '../../domain/dtos.js';
import { MeInvoiceException } from '../../domain/errors.js';
import type {
  GatewayCallOptions,
  InvoiceValidator,
  MeInvoiceGateway,
  RefIdGenerator,
  TemplateResolver,
} from '../ports.js';
import { templateResolutionError } from '../template-resolver.js';
import { PDF_CONTENT_TYPE } from './get-draft-pdf.js';
import { listActiveTemplates } from './list-templates.js';

export interface PreviewDeps {
  gateway: MeInvoiceGateway;
  validator: InvoiceValidator;
  templateResolver: TemplateResolver;
  refIdGenerator: RefIdGenerator;
}

export interface PreviewResult {
  pdf: PdfDocumentDto;
  refId: string;
  template: TemplateDto;
}

/**
 * Port `PreviewInvoice.cs`. Deviation: RefId assignment and validation run before
 * template resolution so an invalid invoice never reaches MISA (no templates call).
 */
export async function previewInvoice(
  deps: PreviewDeps,
  invoice: InvoiceDto,
  withCode: boolean,
  opts?: GatewayCallOptions,
): Promise<PreviewResult> {
  if (typeof invoice !== 'object' || invoice === null) {
    throw new MeInvoiceException('Validation', {
      rawErrorCode: 'ValidationFailed',
      message: 'Invoice is required.',
      field: 'invoice',
      failures: [{ fieldPath: 'invoice', message: 'Invoice is required.', ruleId: 'FR-006' }],
    });
  }
  const refId = invoice.refId ? invoice.refId : deps.refIdGenerator();
  const working: InvoiceDto = { ...invoice, refId };

  const failures = deps.validator.validate(working);
  if (failures.length > 0) {
    throw new MeInvoiceException('Validation', { failures, refId });
  }

  const active = await listActiveTemplates(deps.gateway, withCode, opts);
  const resolution = deps.templateResolver(active, invoice.template);
  if (resolution.kind !== 'Resolved') throw templateResolutionError(resolution);

  const template = resolution.template;
  const content = await deps.gateway.preview(working, refId, template, withCode, opts);
  return { pdf: { content, contentType: PDF_CONTENT_TYPE }, refId, template };
}
