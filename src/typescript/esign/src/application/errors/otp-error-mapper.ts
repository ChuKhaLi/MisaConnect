import {
  AuthenticationFailedException,
  ESignTransportException,
  ExhaustedOtpAttemptsException,
  ExpiredOtpException,
  InvalidOtpException,
  OtpRejectedException,
  type ESignException,
} from '../../domain/errors.js';
import { Routes } from './routes.js';
import { buildErrorDetail, parseResponseError, type ResponseError } from './response-error.js';

export const INVALID_OTP = [
  'invalid otp',
  'wrong otp',
  'otp incorrect',
  'otp wrong',
  'sai mã',
  'không đúng',
  'otp không hợp lệ',
];
export const EXPIRED_OTP = ['expired', 'hết hạn', 'quá hạn', 'timed out'];
export const EXHAUSTED_OTP = [
  'max attempts',
  'exceeded',
  'vượt quá số lần',
  'too many attempts',
  'locked',
];

export interface MapTwoFactorErrorInput {
  status: number;
  envelope: ResponseError | undefined;
  correlationId: string;
  userName?: string | undefined;
  includeRaw?: boolean | undefined;
  attemptCount?: number | undefined;
  lastStatusCode?: number | undefined;
}

/**
 * True when the body carries MISA's "second factor required" code (`122`), in either the flat or
 * the nested `{status:{…}}` shape and regardless of the HTTP status. Accepts a raw JSON value or
 * an already parsed {@link ResponseError}.
 */
export function isTwoFactorRequired(envelope: unknown): boolean {
  return parseResponseError(envelope)?.errorCode?.toLowerCase() === '122';
}

/** Maps a failed `two-factor-auth` response to a typed exception (the caller throws it). */
export function mapTwoFactorError(input: MapTwoFactorErrorInput): ESignException {
  const { status, envelope, correlationId } = input;
  const rawCode = envelope?.errorCode;
  const detail = buildErrorDetail(Routes.twoFactor, envelope, input.includeRaw ?? false, false);

  if (status === 429 || status >= 500) {
    return new ESignTransportException({
      detail,
      correlationId,
      lastStatusCode: input.lastStatusCode ?? status,
      attemptCount: input.attemptCount ?? 1,
    });
  }

  const base = { rawCode, detail, correlationId };
  if (rawCode) {
    const code = rawCode.toLowerCase();
    if (code === '122') {
      return new AuthenticationFailedException({
        ...base,
        requires2FA: true,
        username: input.userName ?? '',
      });
    }
    if (code === '1001') return new InvalidOtpException(base);
    if (code === '1002') return new ExpiredOtpException(base);
    if (code === '1003') return new ExhaustedOtpAttemptsException(base);
  }

  const probe =
    `${rawCode ?? ''} ${envelope?.devMsg ?? ''} ${envelope?.userMsg ?? ''}`.toLowerCase();
  const any = (needles: readonly string[]) => needles.some((n) => probe.includes(n));
  if (any(INVALID_OTP)) return new InvalidOtpException(base);
  if (any(EXPIRED_OTP)) return new ExpiredOtpException(base);
  if (any(EXHAUSTED_OTP)) return new ExhaustedOtpAttemptsException(base);
  return new OtpRejectedException(base);
}
