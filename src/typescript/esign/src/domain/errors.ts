import type {
  DocumentFormat,
  ESignErrorCategory,
  SignStatus,
  WebhookValidationCategory,
} from './enums.js';
import { httpStatusName } from './http-status-names.js';

const BRAND = Symbol.for('misaconnect.esign.ESignException');

/** Fields every ESign exception carries. */
export interface ESignExceptionInit {
  category: ESignErrorCategory;
  rawCode?: string | undefined;
  detail: string;
  /** Required and non-blank (mirrors .NET's `ArgumentException`). */
  correlationId: string;
  cause?: unknown;
  format?: DocumentFormat | undefined;
}

/** Base class of every error the SDK raises for a MISA eSign operation. */
export abstract class ESignException extends Error {
  override readonly name: string = 'ESignException';
  readonly category: ESignErrorCategory;
  readonly rawCode?: string;
  readonly detail: string;
  readonly correlationId: string;
  readonly format: DocumentFormat;

  protected constructor(init: ESignExceptionInit) {
    super(init.detail, init.cause === undefined ? undefined : { cause: init.cause });
    if (typeof init.correlationId !== 'string' || init.correlationId.trim() === '') {
      throw new TypeError('correlationId must be non-empty.');
    }
    this.name = new.target.name;
    Object.defineProperty(this, BRAND, { value: true, enumerable: false });
    this.category = init.category;
    if (init.rawCode !== undefined) this.rawCode = init.rawCode;
    this.detail = init.detail;
    this.correlationId = init.correlationId;
    this.format = init.format ?? 'Unknown';
  }
}

/**
 * True for an `ESignException` from any copy of this package. Prefer it over `instanceof` when
 * both the ESM and CommonJS builds can load in one process (dual-package hazard).
 */
export function isESignException(err: unknown): err is ESignException {
  if (err instanceof ESignException) return true;
  return err instanceof Error && (err as unknown as Record<symbol, unknown>)[BRAND] === true;
}

/** Common init for subclasses whose category/rawCode are fixed by the class. */
export type ESignSubInit = Omit<ESignExceptionInit, 'category' | 'rawCode'>;

export class ESignGeneralException extends ESignException {
  // Widens the protected base constructor to public.
  // eslint-disable-next-line @typescript-eslint/no-useless-constructor
  constructor(init: ESignExceptionInit) {
    super(init);
  }
}

export interface AuthenticationFailedInit extends ESignSubInit {
  rawCode?: string | undefined;
  requires2FA?: boolean | undefined;
  username?: string | undefined;
}

export class AuthenticationFailedException extends ESignException {
  readonly requires2FA: boolean;
  readonly username: string;

  constructor(init: AuthenticationFailedInit) {
    super({ ...init, category: 'Authentication' });
    this.requires2FA = init.requires2FA ?? false;
    this.username = init.username ?? '';
  }
}

export type OtpFailureInit = ESignSubInit & { rawCode?: string | undefined };

export class InvalidOtpException extends AuthenticationFailedException {
  constructor(init: OtpFailureInit) {
    super({ ...init, rawCode: init.rawCode ?? 'InvalidOtp' });
  }
}

export class ExpiredOtpException extends AuthenticationFailedException {
  constructor(init: OtpFailureInit) {
    super({ ...init, rawCode: init.rawCode ?? 'ExpiredOtp' });
  }
}

export class ExhaustedOtpAttemptsException extends AuthenticationFailedException {
  constructor(init: OtpFailureInit) {
    super({ ...init, rawCode: init.rawCode ?? 'ExhaustedOtpAttempts' });
  }
}

export class OtpRejectedException extends AuthenticationFailedException {
  constructor(init: OtpFailureInit) {
    super({ ...init, rawCode: init.rawCode ?? 'OtpRejected' });
  }
}

export class NoActiveCertificateException extends ESignException {
  constructor(init: ESignSubInit) {
    super({ ...init, category: 'NoActiveCertificate', rawCode: 'NoActiveCertificate' });
  }
}

export class SignRejectedException extends ESignException {
  readonly requiresUserCertSetup: boolean;

  constructor(
    init: ESignSubInit & { rawCode?: string | undefined; requiresUserCertSetup?: boolean },
  ) {
    super({ ...init, category: 'SignRejected' });
    this.requiresUserCertSetup = init.requiresUserCertSetup ?? false;
  }
}

function categoryFromStatus(status: SignStatus): ESignErrorCategory {
  switch (status) {
    case 'FAILED':
      return 'SignTerminalFailed';
    case 'CANCELLED':
      return 'SignTerminalCancelled';
    default:
      return 'SignTerminalUnknown';
  }
}

export class SignTerminalStateException extends ESignException {
  readonly terminalStatus: SignStatus;
  readonly transactionId: string;

  constructor(
    init: ESignSubInit & {
      terminalStatus: SignStatus;
      transactionId: string;
      rawCode?: string | undefined;
    },
  ) {
    super({ ...init, category: categoryFromStatus(init.terminalStatus) });
    this.terminalStatus = init.terminalStatus;
    this.transactionId = init.transactionId;
  }
}

export class SignTimeoutException extends ESignException {
  readonly transactionId: string;
  readonly elapsedMs: number;

  constructor(init: ESignSubInit & { transactionId: string; elapsedMs: number }) {
    super({ ...init, category: 'SignTimeout', rawCode: 'SignTimeout' });
    this.transactionId = init.transactionId;
    this.elapsedMs = init.elapsedMs;
  }
}

export class ESignTransportException extends ESignException {
  readonly lastStatusCode?: number;
  readonly attemptCount: number;

  constructor(init: ESignSubInit & { lastStatusCode?: number | undefined; attemptCount: number }) {
    super({
      ...init,
      category: 'Transport',
      rawCode:
        init.lastStatusCode === undefined ? 'Transport' : httpStatusName(init.lastStatusCode),
    });
    if (init.lastStatusCode !== undefined) this.lastStatusCode = init.lastStatusCode;
    this.attemptCount = init.attemptCount;
  }
}

interface WebhookValidationInit extends ESignSubInit {
  webhookCategory: WebhookValidationCategory;
  matchedTransactionId?: string | undefined;
}

export abstract class WebhookValidationException extends ESignException {
  readonly webhookCategory: WebhookValidationCategory;
  readonly matchedTransactionId?: string;

  protected constructor(init: WebhookValidationInit) {
    super({ ...init, category: 'WebhookValidation' });
    this.webhookCategory = init.webhookCategory;
    if (init.matchedTransactionId !== undefined) {
      this.matchedTransactionId = init.matchedTransactionId;
    }
  }
}

export class MalformedEnvelopeException extends WebhookValidationException {
  constructor(init: { correlationId: string; detail: string }) {
    super({ ...init, webhookCategory: 'MalformedEnvelope', format: 'Unknown' });
  }
}

export class ClientIdMismatchException extends WebhookValidationException {
  constructor(init: { correlationId: string; detail: string }) {
    super({ ...init, webhookCategory: 'ClientIdMismatch', format: 'Unknown' });
  }
}

export class UnknownTransactionException extends WebhookValidationException {
  constructor(init: { correlationId: string; transactionId: string; detail: string }) {
    super({
      correlationId: init.correlationId,
      detail: init.detail,
      webhookCategory: 'UnknownTransaction',
      matchedTransactionId: init.transactionId,
      format: 'Unknown',
    });
  }
}

export class IncompleteSuccessEnvelopeException extends WebhookValidationException {
  constructor(init: {
    correlationId: string;
    transactionId: string;
    format: DocumentFormat;
    detail: string;
  }) {
    super({
      correlationId: init.correlationId,
      detail: init.detail,
      webhookCategory: 'IncompleteSuccessEnvelope',
      matchedTransactionId: init.transactionId,
      format: init.format,
    });
  }
}

export class DocumentIdMismatchException extends WebhookValidationException {
  constructor(init: {
    correlationId: string;
    transactionId: string;
    format: DocumentFormat;
    detail: string;
  }) {
    super({
      correlationId: init.correlationId,
      detail: init.detail,
      webhookCategory: 'DocumentIdMismatch',
      matchedTransactionId: init.transactionId,
      format: init.format,
    });
  }
}
