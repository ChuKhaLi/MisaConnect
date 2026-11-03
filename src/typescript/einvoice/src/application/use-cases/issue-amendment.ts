import type {
  AdjustmentRequestDto,
  AmendmentResultDto,
  ReplacementRequestDto,
} from '../../domain/dtos.js';
import { MeInvoiceException } from '../../domain/errors.js';
import { validateAmendment } from '../invoice-validator.js';
import type {
  GatewayCallOptions,
  InvoiceValidator,
  MeInvoiceGateway,
  RefIdGenerator,
  TemplateResolver,
} from '../ports.js';
import { templateResolutionError } from '../template-resolver.js';
import { listActiveTemplates } from './list-templates.js';

export interface IssueAmendmentDeps {
  gateway: MeInvoiceGateway;
  validator: InvoiceValidator;
  templateResolver: TemplateResolver;
  refIdGenerator: RefIdGenerator;
}

export interface IssueAmendmentResult {
  result: AmendmentResultDto;
  /** Resolved template id (for logging), once resolution succeeded. */
  templateId?: string;
}

/**
 * Port `IssueReplacementInvoice.cs` / `IssueAdjustmentInvoice.cs`. Request fields and the
 * invoice are validated before any HTTP call (deviation: .NET validates the invoice after
 * template resolution). Template resolution failures throw, as in .NET; a MISA rejection of
 * the insert is returned as an `Error` result.
 */
export async function issueAmendment(
  deps: IssueAmendmentDeps,
  kind: 'replacement' | 'adjustment',
  request: ReplacementRequestDto | AdjustmentRequestDto,
  opts?: GatewayCallOptions,
): Promise<IssueAmendmentResult> {
  const requestFailures = validateAmendment(request);
  if (requestFailures.length > 0) {
    throw new MeInvoiceException('Validation', {
      rawErrorCode: 'ValidationFailed',
      failures: requestFailures,
    });
  }

  const refId = request.invoice.refId ? request.invoice.refId : deps.refIdGenerator();
  const invoice = { ...request.invoice, refId };
  const invoiceFailures = deps.validator.validate(invoice);
  if (invoiceFailures.length > 0) {
    throw new MeInvoiceException('Validation', {
      rawErrorCode: 'ValidationFailed',
      failures: invoiceFailures,
      refId,
    });
  }

  const withCode = request.invoiceWithCode ?? true;
  const active = await listActiveTemplates(deps.gateway, withCode, opts);
  const resolution = deps.templateResolver(active, invoice.template);
  if (resolution.kind !== 'Resolved') throw templateResolutionError(resolution);

  const template = resolution.template;
  const outcome = await deps.gateway.insertAmendment(
    kind,
    { ...request, invoice },
    refId,
    template,
    withCode,
    opts,
  );
  const orgRefId = request.originalRef.orgRefID;
  const result: AmendmentResultDto =
    outcome.outcome === 'Success'
      ? { refId, outcome: 'Success', orgRefId }
      : {
          refId,
          outcome: 'Error',
          errorCategory: outcome.errorCategory,
          rawErrorCode: outcome.rawErrorCode,
          ...(outcome.message !== undefined ? { errorMessage: outcome.message } : {}),
          orgRefId,
        };
  return { result, templateId: template.ipTemplateID };
}
