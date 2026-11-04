import type { MisaCredentials } from '../../domain/dtos.js';
import { AuthenticationFailedException, ESignGeneralException } from '../../domain/errors.js';
import type { OtpProvider } from '../ports.js';
import type { TokenManager } from '../token-manager.js';

export interface TransparentOtpContext {
  credentials: MisaCredentials;
  correlationId: string;
  signal?: AbortSignal | undefined;
}

/** Shared by the transparent and explicit flows: a blank OTP code is never sent to MISA. */
export function requireOtpCode(code: string | undefined, correlationId: string): string {
  if (!code || code.trim() === '') {
    throw new ESignGeneralException({
      category: 'Validation',
      rawCode: 'InvalidOtpSubmission',
      detail: 'InvalidOtpSubmission: the OTP code must not be empty.',
      correlationId,
    });
  }
  return code;
}

/**
 * Transparent 2FA (INV §2.5): runs `operation`; when it fails with a 2FA challenge, asks the
 * provider for a code, exchanges it, and re-runs the whole operation once. A second challenge (or
 * any other failure) propagates. Shared by the polling and webhook sign flows.
 */
export async function withTransparentOtp<T>(
  deps: { tokens: TokenManager; otpProvider: OtpProvider },
  ctx: TransparentOtpContext,
  operation: () => Promise<T>,
): Promise<T> {
  try {
    return await operation();
  } catch (err) {
    if (!(err instanceof AuthenticationFailedException) || !err.requires2FA) throw err;
    // Always the caller's own user name: a name echoed by MISA must not change the cache key.
    const userName = ctx.credentials.userName;
    const submission = await deps.otpProvider.provide({
      userName,
      correlationId: ctx.correlationId,
    });
    const code = requireOtpCode(submission.code, ctx.correlationId);
    await deps.tokens.exchangeOtp(
      ctx.credentials,
      { code, otpType: submission.otpType, remember: submission.remember },
      { correlationId: ctx.correlationId, signal: ctx.signal },
    );
    return operation();
  }
}
