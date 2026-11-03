/** Failure categories, mirroring the .NET `MeInvoiceErrorCategory` enum (same order). */
export const MeInvoiceErrorCategory = Object.freeze({
  Configuration: 'Configuration',
  Authentication: 'Authentication',
  TemplateState: 'TemplateState',
  DeclarationState: 'DeclarationState',
  Validation: 'Validation',
  BatchOrSize: 'BatchOrSize',
  DuplicateOrUniqueness: 'DuplicateOrUniqueness',
  ResourceNotFound: 'ResourceNotFound',
  MisaThrottled: 'MisaThrottled',
  MisaUnavailable: 'MisaUnavailable',
  Signing: 'Signing',
  Replacement: 'Replacement',
  OtherWorkflow: 'OtherWorkflow',
  MisaUnknown: 'MisaUnknown',
  NotDeletable: 'NotDeletable',
  TransportFailed: 'TransportFailed',
} as const);
export type MeInvoiceErrorCategory =
  (typeof MeInvoiceErrorCategory)[keyof typeof MeInvoiceErrorCategory];

export interface ValidationFailure {
  fieldPath: string;
  message: string;
  ruleId?: string;
}

export interface MeInvoiceExceptionOptions {
  rawErrorCode?: string;
  message?: string;
  field?: string;
  component?: string;
  refId?: string;
  failures?: readonly ValidationFailure[];
  cause?: unknown;
}

export class MeInvoiceException extends Error {
  override readonly name = 'MeInvoiceException';
  readonly category: MeInvoiceErrorCategory;
  readonly rawErrorCode?: string;
  readonly field?: string;
  readonly component?: string;
  readonly refId?: string;
  readonly failures?: readonly ValidationFailure[];

  constructor(category: MeInvoiceErrorCategory, opts: MeInvoiceExceptionOptions = {}) {
    super(
      opts.message ?? opts.rawErrorCode ?? category,
      opts.cause === undefined ? undefined : { cause: opts.cause },
    );
    this.category = category;
    if (opts.rawErrorCode !== undefined) this.rawErrorCode = opts.rawErrorCode;
    if (opts.field !== undefined) this.field = opts.field;
    if (opts.component !== undefined) this.component = opts.component;
    if (opts.refId !== undefined) this.refId = opts.refId;
    if (opts.failures !== undefined) this.failures = opts.failures;
  }
}
