import type { BeginResultDto } from '../../domain/dtos.js';
import type { SignFormat, SignRequestDtoByFormat } from '../mapping/sign-request-mapper.js';
import type { Clock, SigningSessionStore } from '../ports.js';
import {
  startSigning,
  type SignContext,
  type StartedSigning,
  type StartSigningDeps,
} from './sign-document.js';

export interface BeginSignDeps extends StartSigningDeps {
  store: SigningSessionStore;
  clock: Clock;
  sessionTtlMs: number;
  /**
   * Wraps the authenticated work with the client's 2FA policy (transparent OTP or a recorded
   * explicit challenge), exactly as for `sign*`.
   */
  withTwoFactor: (operation: () => Promise<StartedSigning>) => Promise<StartedSigning>;
}

/**
 * Webhook-mode start (INV §3 begin): validate → token → certificates → select → hash →
 * `Signing/hash`, no polling. Registers the session after `Signing/hash` returns, holding the
 * call's credentials' `clientId` and `userName` (never the secrets) for the webhook finalize.
 */
export async function beginSign<F extends SignFormat>(
  deps: BeginSignDeps,
  format: F,
  dto: SignRequestDtoByFormat[F],
  ctx: SignContext,
): Promise<BeginResultDto> {
  const started = await deps.withTwoFactor(() => startSigning(deps, format, dto, ctx));
  await deps.store.register({
    clientId: ctx.credentials.clientId,
    userName: ctx.credentials.userName,
    transactionId: started.transactionId,
    format,
    hash: started.hash,
    recordedDocumentIds: [started.request.documentId],
    createdAt: deps.clock.now().toISOString(),
    ttlMs: deps.sessionTtlMs,
    observedMessageIds: [],
  });
  return { transactionId: started.transactionId, format };
}
