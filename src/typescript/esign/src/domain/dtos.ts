import type { DocumentFormat, WebhookStatusDto } from './enums.js';

export interface SignaturePosInfoDto {
  positionX: number;
  positionY: number;
  width: number;
  height: number;
  page: number;
}

export interface SignatureDescriptionDto {
  signedBy: string;
  location: string;
  reason: string;
  contact: string;
  showSignedDate?: boolean;
  displayText?: string;
}

export interface XmlSignatureContextDto {
  signatureName: string;
  /** Unparseable values fall back to SHA256. */
  hashAlgorithm: string;
  signatureDescription: SignatureDescriptionDto;
}

interface VisibleSignatureFields {
  documentName: string;
  signerName: string;
  location: string;
  reason: string;
  contact: string;
  logoImageBase64: string;
  dataToBeDisplayed: string;
  page?: number;
  positionX?: number;
  positionY?: number;
  width?: number;
  height?: number;
  renderingMode?: number;
  documentId?: string;
  displayText?: string;
  /** Defaults to true when omitted. */
  showSignedDate?: boolean;
  additionalSignaturePositions?: readonly SignaturePosInfoDto[];
}

export interface SignPdfRequestDto extends VisibleSignatureFields {
  pdf: Uint8Array;
}

export interface SignWordRequestDto extends VisibleSignatureFields {
  word: Uint8Array;
}

export interface SignExcelRequestDto extends VisibleSignatureFields {
  excel: Uint8Array;
}

/** Exactly one of `xml` / `xmlUtf8Bytes` must be set; prefer {@link SignXmlRequest}. */
export interface SignXmlRequestDto {
  xml?: string;
  xmlUtf8Bytes?: Uint8Array;
  signatureContext: XmlSignatureContextDto;
  documentName: string;
  dataToBeDisplayed: string;
  documentId?: string;
}

type SignXmlRest = Omit<SignXmlRequestDto, 'xml' | 'xmlUtf8Bytes'>;

export const SignXmlRequest = Object.freeze({
  fromString(xml: string, rest: SignXmlRest): SignXmlRequestDto {
    return { ...rest, xml };
  },
  fromUtf8Bytes(xmlUtf8Bytes: Uint8Array, rest: SignXmlRest): SignXmlRequestDto {
    return { ...rest, xmlUtf8Bytes };
  },
});

interface SignResultBase {
  transactionId: string;
  certificateKeyAlias: string;
  /** ISO-8601 UTC instant. */
  completedAtUtc: string;
  format: DocumentFormat;
}

export interface SignPdfResultDto extends SignResultBase {
  signedPdf: Uint8Array;
}
/** `signedXml` is UTF-8 bytes. */
export interface SignXmlResultDto extends SignResultBase {
  signedXml: Uint8Array;
}
export interface SignWordResultDto extends SignResultBase {
  signedWord: Uint8Array;
}
export interface SignExcelResultDto extends SignResultBase {
  signedExcel: Uint8Array;
}

export interface OtpResendResultDto {
  success: boolean;
  rawCode?: string;
  userMsg?: string;
  devMsg?: string;
  correlationId: string;
}

export interface BeginResultDto {
  transactionId: string;
  format: DocumentFormat;
}

export interface WebhookSignatureDto {
  documentId: string;
  signature: string;
}

/** Wire shape MISA posts to the webhook endpoint. */
export interface WebhookEnvelopeDto {
  messageId: string;
  clientId: string;
  extraData?: Record<string, unknown> | null;
  status: string;
  errorCode?: string | null;
  transactionId: string;
  signatures: WebhookSignatureDto[];
}

export interface WebhookAckDto {
  errorCode: string;
  devMsg: string;
  userMsg: string;
}

export type WebhookOutcomeDto =
  | {
      kind: 'SuccessWithSignedBytes';
      transactionId: string;
      format: DocumentFormat;
      signedBytes: Uint8Array;
      correlationId: string;
    }
  | {
      kind: 'FailureWithError';
      transactionId?: string;
      format: DocumentFormat;
      categoryName: string;
      misaErrorCode?: string;
      correlationId: string;
    }
  | {
      kind: 'TerminalWithoutFinalize';
      transactionId: string;
      format: DocumentFormat;
      status: WebhookStatusDto;
      misaErrorCode?: string;
      correlationId: string;
    };

export interface WebhookHandleResultDto {
  ack: WebhookAckDto;
  outcome: WebhookOutcomeDto;
}

/** MISA RemoteSigning credentials. Never log; `clientKey` and `password` are secrets. */
export interface MisaCredentials {
  clientId: string;
  clientKey: string;
  userName: string;
  password: string;
}

/** Internal: a signing certificate returned by MISA (not exported from the package index). */
export interface Certificate {
  userId: string;
  keyAlias: string;
  appName: string;
  keyStatus: 'ACTIVE' | 'INACTIVE';
  certStatus?: string;
  /** Signing certificate (base64). */
  certificate: string;
  /** [signing, intermediate, root] — MISA returns exactly three. */
  chain: [string, string, string];
  effectiveDate?: string;
  expirationDate?: string;
  emailName?: string;
  isAutoSign: boolean;
}
