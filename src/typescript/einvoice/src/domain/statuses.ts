export const DeleteDraftStatus = Object.freeze({
  Deleted: 'Deleted',
  NotFound: 'NotFound',
  NotDeletable: 'NotDeletable',
  AuthFailed: 'AuthFailed',
  Configuration: 'Configuration',
  MisaThrottled: 'MisaThrottled',
  MisaUnavailable: 'MisaUnavailable',
  TransportFailed: 'TransportFailed',
  MisaUnknown: 'MisaUnknown',
} as const);
export type DeleteDraftStatus = (typeof DeleteDraftStatus)[keyof typeof DeleteDraftStatus];

export const LookupStatus = Object.freeze({
  Found: 'Found',
  NotFound: 'NotFound',
  Validation: 'Validation',
  AuthFailed: 'AuthFailed',
  Configuration: 'Configuration',
  MisaThrottled: 'MisaThrottled',
  MisaUnavailable: 'MisaUnavailable',
  TransportFailed: 'TransportFailed',
  MisaUnknown: 'MisaUnknown',
} as const);
export type LookupStatus = (typeof LookupStatus)[keyof typeof LookupStatus];

export const LookupBatchStatus = Object.freeze({
  Completed: 'Completed',
  Validation: 'Validation',
} as const);
export type LookupBatchStatus = (typeof LookupBatchStatus)[keyof typeof LookupBatchStatus];

export const SaveOutcomeDto = Object.freeze({
  Success: 'Success',
  Error: 'Error',
} as const);
export type SaveOutcomeDto = (typeof SaveOutcomeDto)[keyof typeof SaveOutcomeDto];

export const InvoiceStatus = Object.freeze({
  Draft: 'Draft',
  Signed: 'Signed',
  Issued: 'Issued',
  Cancelled: 'Cancelled',
  Replaced: 'Replaced',
  Adjusted: 'Adjusted',
} as const);
export type InvoiceStatus = (typeof InvoiceStatus)[keyof typeof InvoiceStatus];

export interface DeleteDraftOutcome {
  status: DeleteDraftStatus;
  rawErrorCode?: string;
  message?: string;
  field?: string;
  refId?: string;
}
