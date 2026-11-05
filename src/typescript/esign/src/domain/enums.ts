/** Document formats the SDK can sign, mirroring the .NET `DocumentFormat` enum. */
export const DocumentFormat = Object.freeze({
  Unknown: 'Unknown',
  Pdf: 'Pdf',
  Xml: 'Xml',
  Word: 'Word',
  Excel: 'Excel',
} as const);
export type DocumentFormat = (typeof DocumentFormat)[keyof typeof DocumentFormat];

/** How MISA delivers a second-factor code, mirroring `OtpDeliveryChannel`. */
export const OtpDeliveryChannel = Object.freeze({
  SmsOrEmail: 'SmsOrEmail',
  Authenticator: 'Authenticator',
} as const);
export type OtpDeliveryChannel = (typeof OtpDeliveryChannel)[keyof typeof OtpDeliveryChannel];

/** Numeric `otpType` MISA expects on the two-factor request (`SmsOrEmail=0`, `Authenticator=1`). */
export function otpChannelWireValue(channel: OtpDeliveryChannel): 0 | 1 {
  return channel === 'Authenticator' ? 1 : 0;
}

/** Failure categories, mirroring the .NET `ESignErrorCategory` enum (same order). */
export const ESignErrorCategory = Object.freeze({
  MisaUnknown: 'MisaUnknown',
  Authentication: 'Authentication',
  NoActiveCertificate: 'NoActiveCertificate',
  CertificateLookupFailed: 'CertificateLookupFailed',
  HashRejected: 'HashRejected',
  SignRejected: 'SignRejected',
  SignTerminalFailed: 'SignTerminalFailed',
  SignTerminalCancelled: 'SignTerminalCancelled',
  SignTerminalUnknown: 'SignTerminalUnknown',
  SignTimeout: 'SignTimeout',
  AttachmentRejected: 'AttachmentRejected',
  StatusLookupFailed: 'StatusLookupFailed',
  Transport: 'Transport',
  Validation: 'Validation',
  WebhookValidation: 'WebhookValidation',
  /** Reserved; never thrown (kept for parity with .NET). */
  WebhookFinalize: 'WebhookFinalize',
} as const);
export type ESignErrorCategory = (typeof ESignErrorCategory)[keyof typeof ESignErrorCategory];

/** Which transports a consumer wires up for signing results. */
export type WebhookMode = 'polling' | 'webhook' | 'both';

/** Status values MISA sends on a webhook envelope. */
export const WebhookStatusDto = Object.freeze({
  Success: 'Success',
  Failed: 'Failed',
  Cancelled: 'Cancelled',
} as const);
export type WebhookStatusDto = (typeof WebhookStatusDto)[keyof typeof WebhookStatusDto];

/** Signing transaction status as reported by MISA's status endpoint. */
export const SignStatus = Object.freeze({
  PENDING: 'PENDING',
  SUCCESS: 'SUCCESS',
  FAILED: 'FAILED',
  CANCELLED: 'CANCELLED',
  UNKNOWN: 'UNKNOWN',
} as const);
export type SignStatus = (typeof SignStatus)[keyof typeof SignStatus];

/** Why a webhook envelope was rejected (.NET `WebhookValidationCategory`). */
export const WebhookValidationCategory = Object.freeze({
  None: 'None',
  MalformedEnvelope: 'MalformedEnvelope',
  ClientIdMismatch: 'ClientIdMismatch',
  UnknownTransaction: 'UnknownTransaction',
  IncompleteSuccessEnvelope: 'IncompleteSuccessEnvelope',
  DocumentIdMismatch: 'DocumentIdMismatch',
} as const);
export type WebhookValidationCategory =
  (typeof WebhookValidationCategory)[keyof typeof WebhookValidationCategory];
