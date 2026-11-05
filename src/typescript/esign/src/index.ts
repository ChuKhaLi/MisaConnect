export const VERSION = '0.1.1';

export {
  DocumentFormat,
  ESignErrorCategory,
  OtpDeliveryChannel,
  SignStatus,
  WebhookStatusDto,
  WebhookValidationCategory,
  otpChannelWireValue,
  type WebhookMode,
} from './domain/enums.js';
export { httpStatusName } from './domain/http-status-names.js';
export {
  AuthenticationFailedException,
  ClientIdMismatchException,
  DocumentIdMismatchException,
  ESignException,
  ESignGeneralException,
  ESignTransportException,
  ExhaustedOtpAttemptsException,
  ExpiredOtpException,
  IncompleteSuccessEnvelopeException,
  InvalidOtpException,
  MalformedEnvelopeException,
  NoActiveCertificateException,
  OtpRejectedException,
  SignRejectedException,
  SignTerminalStateException,
  SignTimeoutException,
  UnknownTransactionException,
  WebhookValidationException,
  isESignException,
  type ESignExceptionInit,
} from './domain/errors.js';
export {
  SignXmlRequest,
  type BeginResultDto,
  type Certificate,
  type MisaCredentials,
  type OtpResendResultDto,
  type SignExcelRequestDto,
  type SignExcelResultDto,
  type SignPdfRequestDto,
  type SignPdfResultDto,
  type SignWordRequestDto,
  type SignWordResultDto,
  type SignXmlRequestDto,
  type SignXmlResultDto,
  type SignatureDescriptionDto,
  type SignaturePosInfoDto,
  type WebhookAckDto,
  type WebhookEnvelopeDto,
  type WebhookHandleResultDto,
  type WebhookOutcomeDto,
  type WebhookSignatureDto,
  type XmlSignatureContextDto,
} from './domain/dtos.js';
export { createMisaESignClient } from './client.js';
export type { CallOptions, IMisaESignClient } from './client.js';
export type { MisaESignOptions } from './infrastructure/options.js';
export type {
  AccessToken,
  CertificateSelector,
  Clock,
  Delay,
  Logger,
  MisaCredentialsProvider,
  OtpProvider,
  SigningSession,
  SigningSessionStore,
  SigningTerminal,
  TokenCache,
  WebhookDeliveryHook,
} from './application/ports.js';
export type {
  HashOutput,
  PdfHashOutput,
  WordExcelHashOutput,
  XmlHashOutput,
} from './application/hash-output.js';
export { createInMemoryTokenCache } from './infrastructure/defaults.js';
export { createInMemorySigningSessionStore } from './infrastructure/in-memory-session-store.js';
export { fromPino, type PinoLike } from './infrastructure/logging.js';
