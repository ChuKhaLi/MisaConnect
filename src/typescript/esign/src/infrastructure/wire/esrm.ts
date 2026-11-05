import { mapESignError } from '../../application/errors/esign-error-mapper.js';
import type {
  EsrmCall,
  EsrmGateway,
  RawCertificate,
  RawSignStatus,
  SignHashInput,
} from '../../application/esrm-gateway.js';
import { ESignGeneralException } from '../../domain/errors.js';
import type { ResolvedESignOptions } from '../options.js';
import { endpointLabel, type RouteKey } from '../routes.js';
import type { Transport, TransportResponse } from '../transport.js';
import { FORMATS } from './formats.js';
import { parseResponseError } from './response-error.js';

type Json = Record<string, unknown>;

const isRecord = (v: unknown): v is Json =>
  typeof v === 'object' && v !== null && !Array.isArray(v);
const str = (v: unknown): string => (typeof v === 'string' ? v : '');
const optStr = (v: unknown): string | undefined => (typeof v === 'string' ? v : undefined);

/** `errorCode` arrives as a string, number or boolean depending on the endpoint. */
function looseString(v: unknown): string | undefined {
  if (typeof v === 'string') return v;
  if (typeof v === 'number' && Number.isFinite(v)) return String(v);
  if (typeof v === 'boolean') return v ? 'true' : 'false';
  return undefined;
}

function set(target: object, key: string, value: unknown): void {
  if (value !== undefined) (target as Json)[key] = value;
}

/** One wire certificate (`certiticateChain` is MISA's spelling). Missing fields become empty. */
function toRawCertificate(v: unknown): RawCertificate {
  const d = isRecord(v) ? v : {};
  const chainIn = d['certiticateChain'];
  const cert: RawCertificate = {
    userId: str(d['userId']),
    keyAlias: str(d['keyAlias']),
    appName: str(d['appName']),
    keyStatus: str(d['keyStatus']),
    certificate: str(d['certificate']),
    chain: Array.isArray(chainIn) ? chainIn.map(str) : [],
    isAutoSign: d['isAutoSign'] === true,
  };
  set(cert, 'certStatus', optStr(d['certStatus']));
  set(cert, 'effectiveDate', optStr(d['effectiveDate']));
  set(cert, 'expirationDate', optStr(d['expirationDate']));
  set(cert, 'emailName', optStr(d['emailName']));
  return cert;
}

function emptyResponse(route: RouteKey, what: string, call: EsrmCall): ESignGeneralException {
  return new ESignGeneralException({
    category: 'MisaUnknown',
    rawCode: 'EmptyResponse',
    detail: `MISA ${endpointLabel(route)} returned ${what}.`,
    correlationId: call.correlationId,
    format: call.format,
  });
}

/** The ESRM endpoints over the transport; non-2xx answers are thrown as mapped exceptions. */
export function createEsrmGateway(
  resolved: ResolvedESignOptions,
  transport: Transport,
): EsrmGateway {
  const includeRaw = resolved.errors.includeRawErrorMessage;

  async function send(
    route: RouteKey,
    call: EsrmCall,
    init: { method: 'GET' | 'POST'; body?: unknown; suffix?: string },
  ): Promise<TransportResponse> {
    const res = await transport.send({
      route,
      method: init.method,
      ...(init.suffix === undefined ? {} : { suffix: init.suffix }),
      ...(init.body === undefined ? {} : { body: init.body }),
      credentials: call.credentials,
      bearer: call.bearer,
      correlationId: call.correlationId,
      ...(call.signal === undefined ? {} : { signal: call.signal }),
      expectJson: true,
    });
    if (res.status < 200 || res.status >= 300) {
      throw mapESignError({
        route,
        status: res.status,
        envelope: parseResponseError(res.json),
        format: call.format,
        correlationId: call.correlationId,
        includeRaw,
        userName: call.credentials.userName,
      });
    }
    return res;
  }

  return {
    async listCertificates(call) {
      const res = await send('certificates', call, { method: 'GET' });
      if (res.json === undefined) return []; // empty body: no certificates
      if (!Array.isArray(res.json)) {
        // The body may carry PII (e.g. emailName): report its length only.
        throw new ESignGeneralException({
          category: 'MisaUnknown',
          rawCode: 'UnparseableResponse',
          detail: `MISA ${endpointLabel('certificates')} returned a ${res.status} success with a JSON content-type but a body (${res.text.length} bytes) that could not be parsed as a certificate array.`,
          correlationId: call.correlationId,
          format: call.format,
        });
      }
      return res.json.map(toRawCertificate);
    },

    async hash(call, cert, request) {
      const spec = FORMATS[call.format];
      const res = await send('hash', call, {
        method: 'POST',
        body: spec.buildHashBody(cert, request),
      });
      return spec.parseHash(res.json, { correlationId: call.correlationId });
    },

    async signHash(call, input: SignHashInput) {
      const res = await send('signHash', call, {
        method: 'POST',
        body: {
          DataToBeDisplayed: input.dataToBeDisplayed,
          UserId: input.userId,
          CertAlias: input.certAlias,
          // An undefined DocumentId is omitted by JSON.stringify (nulls are never sent).
          Documents: [
            {
              DocumentId: input.documentId,
              FileToSign: input.digest,
              DocumentName: input.documentName,
            },
          ],
        },
      });
      const tx = isRecord(res.json) ? str(res.json['transactionId']) : '';
      if (tx === '') throw emptyResponse('signHash', 'no transactionId', call);
      return tx;
    },

    async status(call, transactionId) {
      const res = await send('signStatus', call, { method: 'GET', suffix: transactionId });
      if (!isRecord(res.json)) throw emptyResponse('signStatus', 'an empty body', call);
      const d = res.json;
      const out: RawSignStatus = {
        transactionId: optStr(d['transactionId']) ?? transactionId,
      };
      set(out, 'status', optStr(d['status']));
      set(out, 'errorCode', looseString(d['errorCode']));
      set(out, 'errorDescription', optStr(d['errorDescription']));
      const sigs = d['signatures'];
      if (Array.isArray(sigs) && sigs.length > 0 && isRecord(sigs[0])) {
        set(out, 'firstSignature', optStr(sigs[0]['signature']));
      }
      return out;
    },

    async attach(call, cert, hash, signature, request) {
      const spec = FORMATS[call.format];
      const res = await send('attachment', call, {
        method: 'POST',
        body: spec.buildAttachBody(cert, hash, signature, request),
      });
      return spec.parseAttach(res.json, { correlationId: call.correlationId });
    },
  };
}
