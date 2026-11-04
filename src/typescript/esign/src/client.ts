import { createMisaESignClientInternal } from './client-internal.js';
import type {
  BeginResultDto,
  MisaCredentials,
  OtpResendResultDto,
  SignExcelRequestDto,
  SignExcelResultDto,
  SignPdfRequestDto,
  SignPdfResultDto,
  SignWordRequestDto,
  SignWordResultDto,
  SignXmlRequestDto,
  SignXmlResultDto,
  WebhookHandleResultDto,
} from './domain/dtos.js';
import type { OtpDeliveryChannel } from './domain/enums.js';
import type { MisaESignOptions } from './infrastructure/options.js';

/** Optional last argument of every client method. */
export interface CallOptions {
  /** Aborts the call; rejects with the signal's reason, never retried or wrapped. */
  signal?: AbortSignal;
  /** Overrides the `correlationId` option for this call (sent as `X-Correlation-Id`). */
  correlationId?: string;
  /** Credentials for this call only; take precedence over the `credentials` option. */
  credentials?: MisaCredentials;
}

/** Port of the .NET `IMisaESignClient` facade. */
export interface IMisaESignClient {
  /** Polling mode: signs and waits for the signer's approval. Throws `WebhookModeOnly` in webhook mode. */
  signPdf(request: SignPdfRequestDto, opts?: CallOptions): Promise<SignPdfResultDto>;
  signXml(request: SignXmlRequestDto, opts?: CallOptions): Promise<SignXmlResultDto>;
  signWord(request: SignWordRequestDto, opts?: CallOptions): Promise<SignWordResultDto>;
  signExcel(request: SignExcelRequestDto, opts?: CallOptions): Promise<SignExcelResultDto>;
  signInWithOtp(
    otpCode: string,
    otpType: OtpDeliveryChannel,
    remember: boolean,
    opts?: CallOptions,
  ): Promise<void>;
  resendOtp(language?: string, opts?: CallOptions): Promise<OtpResendResultDto>;
  /** Webhook mode: starts signing and returns without polling. Throws `PollingModeOnly` in polling mode. */
  beginSignPdf(request: SignPdfRequestDto, opts?: CallOptions): Promise<BeginResultDto>;
  beginSignXml(request: SignXmlRequestDto, opts?: CallOptions): Promise<BeginResultDto>;
  beginSignWord(request: SignWordRequestDto, opts?: CallOptions): Promise<BeginResultDto>;
  beginSignExcel(request: SignExcelRequestDto, opts?: CallOptions): Promise<BeginResultDto>;
  /** Takes the parsed JSON body MISA posted to the webhook endpoint. */
  handleWebhook(envelope: unknown, opts?: CallOptions): Promise<WebhookHandleResultDto>;
}

/**
 * Validates options (throws `Validation` / `InvalidOptions`) and builds the client. One client
 * holds one token cache and one HTTP transport; reuse it rather than creating one per call.
 */
export function createMisaESignClient(options: MisaESignOptions): IMisaESignClient {
  return createMisaESignClientInternal(options);
}
