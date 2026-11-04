import type { DocumentFormat } from '../../domain/enums.js';
import {
  AuthenticationFailedException,
  ESignGeneralException,
  ESignTransportException,
  SignRejectedException,
  type ESignException,
} from '../../domain/errors.js';
import { Routes, type RouteKey } from './routes.js';
import { buildErrorDetail, type ResponseError } from './response-error.js';

export interface MapESignErrorInput {
  /** Canonical route key (not the resolved URL). */
  route: RouteKey;
  status: number;
  envelope: ResponseError | undefined;
  format: DocumentFormat;
  correlationId: string;
  includeRaw: boolean;
  userName?: string | undefined;
  attemptCount?: number | undefined;
  lastStatusCode?: number | undefined;
}

const has = (haystack: string | undefined, needle: string): boolean =>
  haystack !== undefined && haystack.toLowerCase().includes(needle.toLowerCase());
const hasAny = (haystack: string | undefined, needles: readonly string[]): boolean =>
  needles.some((n) => has(haystack, n));

function combinedProbe(rawCode: string | undefined, env: ResponseError | undefined) {
  const parts = [rawCode, env?.devMsg, env?.userMsg].filter(
    (p): p is string => p !== undefined && p !== '',
  );
  return parts.length === 0 ? undefined : parts.join(' ');
}

function synthesizeHashCode(rawCode: string | undefined, env: ResponseError | undefined): string {
  const probe = rawCode ?? env?.devMsg ?? env?.userMsg;
  if (probe === undefined) return 'EmptyErrorCode';
  if (has(probe, 'Cert') || has(probe, 'Certificate')) return 'InvalidCertificate';
  if (has(probe, 'Hash') || has(probe, 'Digest')) return 'InvalidHash';
  if (has(probe, 'File') || has(probe, 'Document')) return 'InvalidDocument';
  if (has(probe, 'Signature')) return 'InvalidSignatureInfo';
  return rawCode ?? 'EmptyErrorCode';
}

function synthesizeHash(
  rawCode: string | undefined,
  env: ResponseError | undefined,
  format: DocumentFormat,
): string {
  const probe = combinedProbe(rawCode, env);
  if (probe === undefined) return 'EmptyErrorCode';
  if (
    format === 'Xml' &&
    has(probe, 'xml') &&
    (has(probe, 'malformed') || has(probe, 'invalid') || has(probe, 'không hợp lệ'))
  ) {
    return 'InvalidXmlInput';
  }
  if (hasAny(probe, ['unsupported', 'variant', 'format not supported'])) {
    return 'UnsupportedDocumentVariant';
  }
  return synthesizeHashCode(rawCode, env);
}

function synthesizeAttachmentCode(
  rawCode: string | undefined,
  env: ResponseError | undefined,
): string {
  const probe = rawCode ?? env?.devMsg ?? env?.userMsg;
  if (probe === undefined) return 'EmptyErrorCode';
  if (has(probe, 'Signature')) return 'InvalidSignature';
  if (has(probe, 'Cert')) return 'InvalidCertificate';
  if (has(probe, 'Doc') || has(probe, 'Hash')) return 'InvalidHashInputs';
  return rawCode ?? 'EmptyErrorCode';
}

function synthesizeAttachment(
  rawCode: string | undefined,
  env: ResponseError | undefined,
  format: DocumentFormat,
): string {
  const probe = combinedProbe(rawCode, env);
  if (probe === undefined) return 'EmptyErrorCode';
  if (
    (format === 'Word' || format === 'Excel') &&
    hasAny(probe, ['mainDom', 'main dom', 'missing main'])
  ) {
    return 'MissingMainDom';
  }
  if (
    (format === 'Xml' || format === 'Word' || format === 'Excel') &&
    hasAny(probe, ['signatureId', 'signature id', 'missing signature'])
  ) {
    return 'MissingSignatureId';
  }
  if (hasAny(probe, ['unsupported', 'variant', 'format not supported'])) {
    return 'UnsupportedDocumentVariant';
  }
  return synthesizeAttachmentCode(rawCode, env);
}

/**
 * Translates a MISA error response into a typed exception (the caller throws it). Mirrors .NET
 * `ESignErrorMapper.Map`: 401 on non-auth routes and 429/5xx are decided first, then per route.
 */
export function mapESignError(input: MapESignErrorInput): ESignException {
  const { route, status, envelope, format, correlationId } = input;
  const rawCode = envelope?.errorCode;
  const detail = buildErrorDetail(Routes[route], envelope, input.includeRaw, true);
  const base = { detail, correlationId, format };

  if (status === 401 && route !== 'login' && route !== 'refresh') {
    return new AuthenticationFailedException({ ...base, rawCode: rawCode ?? '401' });
  }
  if (status === 429 || status >= 500) {
    return new ESignTransportException({
      ...base,
      lastStatusCode: input.lastStatusCode ?? status,
      attemptCount: input.attemptCount ?? 1,
    });
  }

  const general = (category: ESignGeneralException['category'], code: string) =>
    new ESignGeneralException({ ...base, category, rawCode: code });

  switch (route) {
    case 'login': {
      const requires2FA = rawCode?.toLowerCase() === '122';
      return new AuthenticationFailedException({
        ...base,
        rawCode,
        requires2FA,
        username: requires2FA ? (input.userName ?? '') : '',
      });
    }
    case 'refresh':
      return new AuthenticationFailedException({ ...base, rawCode });
    case 'certificates':
      return general('CertificateLookupFailed', rawCode ?? 'EmptyErrorCode');
    case 'hash':
      return general('HashRejected', synthesizeHash(rawCode, envelope, format));
    case 'signHash': {
      const requiresUserCertSetup =
        hasAny(envelope?.devMsg, ['not connected', 'not set up', 'remote signing account']) ||
        hasAny(envelope?.userMsg, ['chưa kết nối', 'chưa thiết lập']);
      return new SignRejectedException({ ...base, rawCode, requiresUserCertSetup });
    }
    case 'signStatus':
      return general('StatusLookupFailed', rawCode ?? 'EmptyErrorCode');
    case 'attachment':
      return general('AttachmentRejected', synthesizeAttachment(rawCode, envelope, format));
    default:
      return general('MisaUnknown', rawCode ?? 'EmptyErrorCode');
  }
}
