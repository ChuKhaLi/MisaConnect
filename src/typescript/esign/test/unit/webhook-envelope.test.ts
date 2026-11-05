import { describe, expect, it } from 'vitest';
import { parseWebhookEnvelope } from '../../src/application/webhook-envelope.js';
import { MalformedEnvelopeException } from '../../src/domain/errors.js';

const valid = {
  messageId: 'm1',
  clientId: 'cid',
  extraData: null,
  status: 'SUCCESS',
  errorCode: null,
  transactionId: 'tx-1',
  signatures: [{ documentId: 'doc-1', signature: 'SIG' }],
};

function malformed(body: unknown): MalformedEnvelopeException {
  try {
    parseWebhookEnvelope(body, 'corr-1');
  } catch (err) {
    expect(err).toBeInstanceOf(MalformedEnvelopeException);
    const e = err as MalformedEnvelopeException;
    expect(e.webhookCategory).toBe('MalformedEnvelope');
    expect(e.correlationId).toBe('corr-1');
    return e;
  }
  throw new Error('expected MalformedEnvelopeException');
}

describe('parseWebhookEnvelope', () => {
  it('parses a valid SUCCESS envelope', () => {
    expect(parseWebhookEnvelope(valid, 'corr-1')).toEqual({
      messageId: 'm1',
      clientId: 'cid',
      transactionId: 'tx-1',
      status: 'Success',
      signatures: [{ documentId: 'doc-1', signature: 'SIG' }],
    });
  });

  it.each([
    ['SUCCESS', 'Success'],
    ['success', 'Success'],
    ['Failed', 'Failed'],
    ['FAILED', 'Failed'],
    ['CANCELLED', 'Cancelled'],
    ['CANCELED', 'Cancelled'],
    ['canceled', 'Cancelled'],
  ])('accepts status %s case-insensitively', (status, expected) => {
    expect(parseWebhookEnvelope({ ...valid, status }).status).toBe(expected);
  });

  it('keeps a string or numeric errorCode', () => {
    expect(parseWebhookEnvelope({ ...valid, status: 'FAILED', errorCode: 'E1' }).errorCode).toBe(
      'E1',
    );
    expect(parseWebhookEnvelope({ ...valid, status: 'FAILED', errorCode: 42 }).errorCode).toBe(
      '42',
    );
  });

  it('treats absent or null signatures as empty', () => {
    expect(parseWebhookEnvelope({ ...valid, signatures: undefined }).signatures).toEqual([]);
    expect(parseWebhookEnvelope({ ...valid, signatures: null }).signatures).toEqual([]);
  });

  it.each(['messageId', 'clientId', 'transactionId', 'status'])('rejects a missing %s', (field) => {
    malformed(Object.fromEntries(Object.entries(valid).filter(([k]) => k !== field)));
  });

  it.each(['messageId', 'clientId', 'transactionId'])(
    'rejects an empty or non-string %s',
    (field) => {
      malformed({ ...valid, [field]: '' });
      malformed({ ...valid, [field]: '   ' });
      malformed({ ...valid, [field]: 7 });
    },
  );

  it.each(['PENDING', 'DONE', '', 1, null, 'ſuccess', 'ſUCCESS', 'constructor', '__proto__'])(
    'rejects status %j',
    (status) => {
      malformed({ ...valid, status });
    },
  );

  it('rejects signatures that are not an array', () => {
    malformed({ ...valid, signatures: 'SIG' });
    malformed({ ...valid, signatures: { documentId: 'doc-1', signature: 'SIG' } });
  });

  it('rejects signature entries without string documentId and signature', () => {
    malformed({ ...valid, signatures: [null] });
    malformed({ ...valid, signatures: [{ documentId: 'doc-1' }] });
    malformed({ ...valid, signatures: [{ documentId: 1, signature: 'SIG' }] });
  });

  it.each([['x'], [null], [undefined], [42], [[valid]]])('rejects a non-object body %j', (body) => {
    malformed(body);
  });
});
