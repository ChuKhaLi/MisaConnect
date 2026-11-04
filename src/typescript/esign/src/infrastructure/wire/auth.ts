import { mapESignError } from '../../application/errors/esign-error-mapper.js';
import {
  isTwoFactorRequired,
  mapTwoFactorError,
} from '../../application/errors/otp-error-mapper.js';
import { buildErrorDetail } from '../../application/errors/response-error.js';
import type { AccessToken } from '../../application/ports.js';
import {
  createTokenManager,
  type AuthCallContext,
  type AuthGateway,
  type OtpSubmission,
  type RefreshedSession,
  type TokenManager,
} from '../../application/token-manager.js';
import type { MisaCredentials, OtpResendResultDto } from '../../domain/dtos.js';
import { otpChannelWireValue } from '../../domain/enums.js';
import { AuthenticationFailedException, ESignGeneralException } from '../../domain/errors.js';
import { createInMemoryTokenCache, defaultTokenCacheKey, systemClock } from '../defaults.js';
import type { ResolvedESignOptions } from '../options.js';
import { Routes } from '../routes.js';
import type { Transport, TransportResponse } from '../transport.js';
import { parseResponseError } from './response-error.js';

const isRecord = (v: unknown): v is Record<string, unknown> =>
  typeof v === 'object' && v !== null && !Array.isArray(v);
const str = (v: unknown): string | undefined => (typeof v === 'string' ? v : undefined);
const is2xx = (status: number): boolean => status >= 200 && status < 300;

/** `expiresIn` is documented as an int of seconds; anything else counts as 0 (as .NET). */
function expiresInSeconds(v: unknown): number {
  if (typeof v === 'number' && Number.isFinite(v)) return v;
  if (typeof v === 'string' && /^\d+$/.test(v.trim())) return Number(v);
  return 0;
}

// ---- request builders -------------------------------------------------------------------------

export const buildLoginBody = (creds: MisaCredentials) => ({
  userName: creds.userName,
  password: creds.password,
});

export const buildRefreshBody = (refreshToken: string) => ({ refreshToken });

export const buildTwoFactorBody = (userName: string, s: OtpSubmission) => ({
  userName,
  code: s.code,
  otpType: otpChannelWireValue(s.otpType),
  remember: s.remember,
});

export const buildResendBody = (userName: string, language: string) => ({ userName, language });

// ---- response parsers -------------------------------------------------------------------------

interface ParseCtx {
  correlationId: string;
  includeRaw: boolean;
  now: Date;
}

/** `data` block of a login / two-factor / enveloped refresh response (mirrors `AuthSessionMapper`). */
function sessionFromData(data: Record<string, unknown>, now: Date): AccessToken {
  const user = isRecord(data.user) ? data.user : {};
  const token: AccessToken = {
    value: str(data.remoteSigningAccessToken) ?? '',
    expiresAt: new Date(now.getTime() + expiresInSeconds(data.expiresIn) * 1000),
    userId: str(user.id) ?? '',
    username: str(user.username) ?? '',
  };
  const rt = str(data.refreshToken);
  if (rt) token.refreshToken = rt;
  return token;
}

function emptyResponse(route: 'login' | 'twoFactor' | 'refresh', correlationId: string) {
  return new ESignGeneralException({
    category: 'MisaUnknown',
    rawCode: 'EmptyResponse',
    detail: `MISA ${Routes[route]} returned an empty data block.`,
    correlationId,
  });
}

/** The `status` block's `code` when present (MISA echoes an HTTP-like code there), else 400. */
function statusBlockCode(json: Record<string, unknown>): number {
  const block = json.status;
  return isRecord(block) && typeof block.code === 'number' ? block.code : 400;
}

/** Login (`route: 'login'`) and two-factor responses share one shape. */
export function parseLoginResponse(
  route: 'login' | 'twoFactor',
  res: Pick<TransportResponse, 'status' | 'json'>,
  userName: string,
  c: ParseCtx,
): AccessToken {
  const envelope = parseResponseError(res.json);
  // Design §9.7: "122" is checked first, on any status and in the flat or nested shape.
  if (isTwoFactorRequired(envelope)) {
    throw new AuthenticationFailedException({
      rawCode: envelope?.errorCode,
      requires2FA: true,
      username: userName,
      detail: buildErrorDetail(Routes[route], envelope, c.includeRaw, route === 'login'),
      correlationId: c.correlationId,
    });
  }
  const json = isRecord(res.json) ? res.json : undefined;
  const failed =
    !is2xx(res.status) || (json !== undefined && isRecord(json.status) && envelope?.error === true);
  if (failed) {
    const status = is2xx(res.status) && json !== undefined ? statusBlockCode(json) : res.status;
    throw route === 'login'
      ? mapESignError({
          route: 'login',
          status,
          envelope,
          format: 'Unknown',
          correlationId: c.correlationId,
          includeRaw: c.includeRaw,
          userName,
        })
      : mapTwoFactorError({
          status,
          envelope,
          correlationId: c.correlationId,
          includeRaw: c.includeRaw,
          userName,
        });
  }
  if (json === undefined || !isRecord(json.data)) throw emptyResponse(route, c.correlationId);
  return sessionFromData(json.data, c.now);
}

/** Refresh accepts `{status, data:{…}}` or the flat `{accessToken, remoteSigningAccessToken, …}` (INV §2.3). */
export function parseRefreshResponse(
  res: Pick<TransportResponse, 'status' | 'json'>,
  c: ParseCtx,
): RefreshedSession {
  if (!is2xx(res.status)) {
    throw mapESignError({
      route: 'refresh',
      status: res.status,
      envelope: parseResponseError(res.json),
      format: 'Unknown',
      correlationId: c.correlationId,
      includeRaw: c.includeRaw,
    });
  }
  const json = isRecord(res.json) ? res.json : {};
  if (isRecord(json.data)) {
    const t = sessionFromData(json.data, c.now);
    return { ...t, userId: t.userId || undefined, username: t.username || undefined };
  }
  const access = str(json.accessToken);
  const remote = str(json.remoteSigningAccessToken);
  if (access === undefined && remote === undefined) throw emptyResponse('refresh', c.correlationId);
  const out: RefreshedSession = {
    value: remote ?? '',
    expiresAt: new Date(c.now.getTime() + expiresInSeconds(json.expiresIn) * 1000),
  };
  const rt = str(json.refreshToken);
  if (rt) out.refreshToken = rt;
  return out;
}

/** INV §2.5: success when 2xx and no envelope or `error` not true; no envelope → "EmptyErrorCode". */
export function parseResendResponse(
  res: Pick<TransportResponse, 'status' | 'json'>,
  correlationId: string,
): OtpResendResultDto {
  const envelope = parseResponseError(res.json);
  if (is2xx(res.status) && envelope?.error !== true) return { success: true, correlationId };
  if (envelope === undefined) return { success: false, rawCode: 'EmptyErrorCode', correlationId };
  const out: OtpResendResultDto = { success: false, correlationId };
  if (envelope.errorCode !== undefined) out.rawCode = envelope.errorCode;
  if (envelope.userMsg !== undefined) out.userMsg = envelope.userMsg;
  if (envelope.devMsg !== undefined) out.devMsg = envelope.devMsg;
  return out;
}

// ---- gateway ----------------------------------------------------------------------------------

/** The MISA auth endpoints over the transport (429/5xx surface from the transport as `Transport`). */
export function createAuthGateway(
  resolved: ResolvedESignOptions,
  transport: Transport,
): AuthGateway {
  const clock = resolved.clock ?? systemClock;
  const includeRaw = resolved.errors.includeRawErrorMessage;
  const pc = (ctx: AuthCallContext): ParseCtx => ({
    correlationId: ctx.correlationId,
    includeRaw,
    now: clock.now(),
  });
  const post = (
    route: 'login' | 'twoFactor' | 'refresh' | 'resendOtp',
    body: unknown,
    creds: MisaCredentials,
    ctx: AuthCallContext,
    expectJson: boolean,
  ) =>
    transport.send({
      route,
      method: 'POST',
      body,
      credentials: creds,
      correlationId: ctx.correlationId,
      ...(ctx.signal === undefined ? {} : { signal: ctx.signal }),
      expectJson,
    });

  return {
    async login(creds, ctx) {
      const res = await post('login', buildLoginBody(creds), creds, ctx, true);
      return parseLoginResponse('login', res, creds.userName, pc(ctx));
    },
    async refresh(creds, refreshToken, ctx) {
      const res = await post('refresh', buildRefreshBody(refreshToken), creds, ctx, true);
      return parseRefreshResponse(res, pc(ctx));
    },
    async twoFactor(creds, submission, ctx) {
      const body = buildTwoFactorBody(creds.userName, submission);
      const res = await post('twoFactor', body, creds, ctx, true);
      return parseLoginResponse('twoFactor', res, creds.userName, pc(ctx));
    },
    async resendOtp(creds, language, ctx) {
      const body = buildResendBody(creds.userName, language);
      const res = await post('resendOtp', body, creds, ctx, false);
      return parseResendResponse(res, ctx.correlationId);
    },
  };
}

/** Wires the token manager with the resolved options' collaborators (or the defaults). */
export function createDefaultTokenManager(
  resolved: ResolvedESignOptions,
  transport: Transport,
): TokenManager {
  const keyFn = resolved.tokenCacheKey ?? defaultTokenCacheKey;
  return createTokenManager({
    gateway: createAuthGateway(resolved, transport),
    tokenCache: resolved.tokenCache ?? createInMemoryTokenCache(),
    clock: resolved.clock ?? systemClock,
    cacheKey: (creds) =>
      keyFn({ userName: creds.userName, clientId: creds.clientId, host: resolved.host }),
  });
}
