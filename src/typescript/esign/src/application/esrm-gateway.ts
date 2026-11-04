import type { Certificate, MisaCredentials } from '../domain/dtos.js';
import type { HashOutput } from './hash-output.js';
import type { NormalizedSignRequest, SignFormat } from './mapping/sign-request-mapper.js';

/** Everything one authenticated ESRM call needs. */
export interface EsrmCall {
  credentials: MisaCredentials;
  /** The `remoteSigningAccessToken`. */
  bearer: string;
  correlationId: string;
  signal?: AbortSignal | undefined;
  format: SignFormat;
}

/** A certificate as listed by MISA, before the chain-length check and the ACTIVE filter. */
export interface RawCertificate extends Omit<Certificate, 'chain'> {
  chain: string[];
}

/** One `Signing/status` answer; `status` is MISA's raw value. */
export interface RawSignStatus {
  status?: string | undefined;
  errorCode?: string | undefined;
  errorDescription?: string | undefined;
  transactionId: string;
  /** `signatures[0].signature`, when present. */
  firstSignature?: string | undefined;
}

/** Inputs of the `Signing/hash` body (INV §2.4 step 5). */
export interface SignHashInput {
  userId: string;
  certAlias: string;
  dataToBeDisplayed: string;
  documentId?: string | undefined;
  digest: string;
  documentName: string;
}

/**
 * The ESRM endpoints. Implementations throw the mapped `ESignException` for MISA error responses
 * (a 401 surfaces as a plain `AuthenticationFailedException`, which `withAuth` retries).
 */
export interface EsrmGateway {
  listCertificates(call: EsrmCall): Promise<RawCertificate[]>;
  hash(call: EsrmCall, cert: Certificate, request: NormalizedSignRequest): Promise<HashOutput>;
  /** Returns the transaction id. */
  signHash(call: EsrmCall, input: SignHashInput): Promise<string>;
  status(call: EsrmCall, transactionId: string): Promise<RawSignStatus>;
  attach(
    call: EsrmCall,
    cert: Certificate,
    hash: HashOutput,
    signature: string,
    request: NormalizedSignRequest,
  ): Promise<Uint8Array>;
}
