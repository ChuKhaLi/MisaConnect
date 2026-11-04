import type {
  Certificate,
  MisaCredentials,
  WebhookAckDto,
  WebhookOutcomeDto,
} from '../domain/dtos.js';
import type { DocumentFormat, OtpDeliveryChannel } from '../domain/enums.js';

export interface Logger {
  debug(message: string, fields?: Record<string, unknown>): void;
  info(message: string, fields?: Record<string, unknown>): void;
  warn(message: string, fields?: Record<string, unknown>): void;
  error(message: string, fields?: Record<string, unknown>): void;
}

export interface Clock {
  now(): Date;
}

export type Delay = (ms: number, signal?: AbortSignal) => Promise<void>;

export interface AccessToken {
  /** The `remoteSigningAccessToken`. */
  value: string;
  refreshToken?: string;
  expiresAt: Date;
  userId: string;
  username: string;
  /**
   * Salted hash binding the token to the credential secrets it was obtained with. Opaque plain
   * data: custom caches must store and return it unchanged. Never log it. A cached token without
   * a matching fingerprint is treated as a miss.
   */
  credentialFingerprint?: string;
}

export interface TokenCache {
  get(key: string): AccessToken | undefined | Promise<AccessToken | undefined>;
  set(key: string, token: AccessToken): void | Promise<void>;
  delete(key: string): void | Promise<void>;
}

export type CertificateSelector = (
  certs: readonly Certificate[],
) => Certificate | Promise<Certificate>;

export interface OtpProvider {
  provide(challenge: {
    userName: string;
    correlationId: string;
  }): Promise<{ code: string; otpType: OtpDeliveryChannel; remember: boolean }>;
}

export type MisaCredentialsProvider = (ctx: {
  clientId?: string;
  userName?: string;
  signal?: AbortSignal;
}) => MisaCredentials | Promise<MisaCredentials>;

export interface SigningSession {
  clientId: string;
  userName: string;
  transactionId: string;
  format: Exclude<DocumentFormat, 'Unknown'>;
  /** Per-format hash output, JSON-serializable; narrowed by the signing use cases. */
  hash: unknown;
  recordedDocumentIds: string[];
  createdAt: string;
  ttlMs: number;
  observedMessageIds: string[];
  /** `signed` is base64. */
  cachedSuccess?: { ack: WebhookAckDto; signed: string };
}

export interface SigningSessionStore {
  register(s: SigningSession): Promise<void>;
  get(clientId: string, transactionId: string): Promise<SigningSession | undefined>;
  recordMessageId(clientId: string, transactionId: string, messageId: string): Promise<void>;
  cacheSuccess(
    clientId: string,
    transactionId: string,
    ack: WebhookAckDto,
    signedBase64: string,
  ): Promise<void>;
  withFinalizeLock<T>(clientId: string, transactionId: string, fn: () => Promise<T>): Promise<T>;
}

export type WebhookDeliveryHook = (o: WebhookOutcomeDto) => void | Promise<void>;
