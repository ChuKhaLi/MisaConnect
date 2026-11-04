import type { WebhookAckDto, WebhookSignatureDto } from '../domain/dtos.js';
import type { WebhookStatusDto } from '../domain/enums.js';
import { MalformedEnvelopeException } from '../domain/errors.js';

/** A validated webhook envelope (`extraData` is dropped: the SDK never reads it). */
export interface WebhookEnvelope {
  messageId: string;
  clientId: string;
  transactionId: string;
  status: WebhookStatusDto;
  errorCode?: string;
  signatures: WebhookSignatureDto[];
}

const STATUS: Record<string, WebhookStatusDto> = {
  SUCCESS: 'Success',
  FAILED: 'Failed',
  CANCELLED: 'Cancelled',
  CANCELED: 'Cancelled',
};

const isRecord = (v: unknown): v is Record<string, unknown> =>
  typeof v === 'object' && v !== null && !Array.isArray(v);

/**
 * Validates the parsed JSON body MISA posts (design §9.6): non-empty string `messageId`,
 * `clientId`, `transactionId`; `status` SUCCESS|FAILED|CANCELLED|CANCELED in any case;
 * `signatures`, when present and not null, an array of `{documentId, signature}` strings.
 * Anything else throws `MalformedEnvelopeException`. Details never echo body values.
 */
export function parseWebhookEnvelope(body: unknown, correlationId = 'webhook'): WebhookEnvelope {
  const fail = (detail: string): never => {
    throw new MalformedEnvelopeException({ correlationId, detail });
  };
  if (!isRecord(body)) return fail('The webhook body is not a JSON object.');

  const required = (field: 'messageId' | 'clientId' | 'transactionId'): string => {
    const v = body[field];
    return typeof v === 'string' && v.trim() !== ''
      ? v
      : fail(`${field} is missing or not a non-empty string.`);
  };
  const messageId = required('messageId');
  const clientId = required('clientId');
  const transactionId = required('transactionId');

  const rawStatus = body['status'];
  const status = typeof rawStatus === 'string' ? STATUS[rawStatus.toUpperCase()] : undefined;
  if (status === undefined) {
    return fail('status is missing or not one of SUCCESS, FAILED, CANCELLED, CANCELED.');
  }

  const rawSignatures = body['signatures'];
  const signatures: WebhookSignatureDto[] = [];
  if (rawSignatures !== undefined && rawSignatures !== null) {
    if (!Array.isArray(rawSignatures)) return fail('signatures is not an array.');
    for (const item of rawSignatures as unknown[]) {
      if (
        !isRecord(item) ||
        typeof item['documentId'] !== 'string' ||
        typeof item['signature'] !== 'string'
      ) {
        return fail('signatures[] entries must have string documentId and signature.');
      }
      signatures.push({ documentId: item['documentId'], signature: item['signature'] });
    }
  }

  const envelope: WebhookEnvelope = { messageId, clientId, transactionId, status, signatures };
  const errorCode = body['errorCode'];
  if (typeof errorCode === 'string') envelope.errorCode = errorCode;
  else if (typeof errorCode === 'number' && Number.isFinite(errorCode)) {
    envelope.errorCode = String(errorCode);
  }
  return envelope;
}

/** ACK bodies returned to MISA, verbatim from .NET (`WebhookAck`, `MapWebhookValidationToAck`). */
export const WEBHOOK_ACKS = Object.freeze({
  success: { errorCode: '0', devMsg: 'Webhook accepted.', userMsg: 'Webhook accepted.' },
  MalformedEnvelope: {
    errorCode: 'webhook.malformed',
    devMsg: 'Inbound envelope failed shape validation.',
    userMsg: 'Webhook payload was malformed.',
  },
  ClientIdMismatch: {
    errorCode: 'webhook.client_id_mismatch',
    devMsg: 'clientId did not match configured value.',
    userMsg: 'Webhook clientId mismatch.',
  },
  UnknownTransaction: {
    errorCode: 'webhook.unknown_transaction',
    devMsg: 'No session recorded for that transactionId.',
    userMsg: 'Webhook transaction not recognized.',
  },
  IncompleteSuccessEnvelope: {
    errorCode: 'webhook.incomplete_success',
    devMsg: 'status=SUCCESS but signatures[] was empty or invalid.',
    userMsg: 'Webhook payload was incomplete.',
  },
  DocumentIdMismatch: {
    errorCode: 'webhook.document_id_mismatch',
    devMsg: 'signatures[].documentId did not match session.',
    userMsg: 'Webhook document mismatch.',
  },
  /** Any other validation category (unreachable today; kept as .NET's fallback). */
  otherValidation: {
    errorCode: 'webhook.malformed',
    devMsg: 'Unspecified webhook validation failure.',
    userMsg: 'Webhook payload was invalid.',
  },
  finalizeFailed: {
    errorCode: 'webhook.finalize_failed',
    devMsg: 'Finalize attempt failed; MISA may retry.',
    userMsg: 'Signing finalization failed; please wait for retry.',
  },
} satisfies Record<string, WebhookAckDto>);
