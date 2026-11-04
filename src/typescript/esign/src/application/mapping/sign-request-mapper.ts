import type {
  SignExcelRequestDto,
  SignPdfRequestDto,
  SignWordRequestDto,
  SignXmlRequestDto,
} from '../../domain/dtos.js';
import type { DocumentFormat } from '../../domain/enums.js';

export type SignFormat = Exclude<DocumentFormat, 'Unknown'>;

export type Base64Encoder = (bytes: Uint8Array) => string;

export interface WireSignatureDescription {
  SignedBy: string;
  ShowSignedDate?: boolean;
  Location: string;
  Reason: string;
  Contact: string;
  DisplayText?: string;
}

export interface WireSignaturePosInfo {
  positionX: number;
  positionY: number;
  width: number;
  height: number;
  page: number;
}

/** MISA `SignatureInfo` (PascalCase). Undefined members are omitted on the wire. */
export interface WireSignatureInfo {
  PositionX?: number;
  PositionY?: number;
  Width?: number;
  Height?: number;
  Page?: number;
  SignatureName: string;
  HashAlgorithm: 'SHA256';
  LogoImage: string;
  SignatureDescription: WireSignatureDescription;
  RenderingMode: number;
  SignaturePosInfos?: WireSignaturePosInfo[];
}

export interface NormalizedSignRequest {
  format: SignFormat;
  /** Base64 of the document, or the XML text for `Xml`. */
  fileToSign: string;
  documentName: string;
  documentId: string;
  dataToBeDisplayed: string;
  signatureInfo: WireSignatureInfo;
}

export interface SignRequestDtoByFormat {
  Pdf: SignPdfRequestDto;
  Xml: SignXmlRequestDto;
  Word: SignWordRequestDto;
  Excel: SignExcelRequestDto;
}

/** The per-format document bytes field of a visible-signature request. */
export function documentBytesOf(
  format: Exclude<SignFormat, 'Xml'>,
  dto: SignPdfRequestDto | SignWordRequestDto | SignExcelRequestDto,
): Uint8Array | undefined {
  switch (format) {
    case 'Pdf':
      return (dto as SignPdfRequestDto).pdf;
    case 'Word':
      return (dto as SignWordRequestDto).word;
    case 'Excel':
      return (dto as SignExcelRequestDto).excel;
  }
}

function newDocumentId(id: string | undefined): string {
  return id === undefined || id === '' ? globalThis.crypto.randomUUID() : id;
}

function set(target: object, key: string, value: unknown): void {
  if (value !== undefined) (target as Record<string, unknown>)[key] = value;
}

/**
 * Maps a validated request DTO to the wire-neutral shape the per-format builders consume.
 * Base64 encoding is injected because the application layer cannot touch `Buffer`.
 */
export function toNormalized<F extends SignFormat>(
  format: F,
  dto: SignRequestDtoByFormat[F],
  encodeBase64: Base64Encoder,
): NormalizedSignRequest {
  if (format === 'Xml') {
    const x = dto as SignXmlRequestDto;
    const hasString = x.xml !== undefined;
    const hasBytes = x.xmlUtf8Bytes !== undefined;
    if (hasString === hasBytes) {
      throw new TypeError(
        'Exactly one of xml or xmlUtf8Bytes must be set (not both, not neither).',
      );
    }
    const text = hasString ? (x.xml as string) : new TextDecoder().decode(x.xmlUtf8Bytes);
    const d = x.signatureContext.signatureDescription;
    const description: WireSignatureDescription = {
      SignedBy: d.signedBy,
      Location: d.location,
      Reason: d.reason,
      Contact: d.contact,
    };
    set(description, 'ShowSignedDate', d.showSignedDate);
    set(description, 'DisplayText', d.displayText);
    return {
      format,
      fileToSign: text,
      documentName: x.documentName,
      documentId: newDocumentId(x.documentId),
      dataToBeDisplayed: x.dataToBeDisplayed,
      // Slim context with MISA's "no visualization" defaults; the algorithm is always SHA256.
      signatureInfo: {
        SignatureName: x.signatureContext.signatureName,
        HashAlgorithm: 'SHA256',
        LogoImage: '',
        RenderingMode: 0,
        SignatureDescription: description,
      },
    };
  }

  const v = dto as SignPdfRequestDto | SignWordRequestDto | SignExcelRequestDto;
  const bytes = documentBytesOf(format as Exclude<SignFormat, 'Xml'>, v) ?? new Uint8Array(0);
  const description: WireSignatureDescription = {
    SignedBy: v.signerName,
    ShowSignedDate: v.showSignedDate ?? true,
    Location: v.location,
    Reason: v.reason,
    Contact: v.contact,
  };
  set(description, 'DisplayText', v.displayText);
  const info: WireSignatureInfo = {
    SignatureName: v.signerName,
    HashAlgorithm: 'SHA256',
    LogoImage: v.logoImageBase64,
    SignatureDescription: description,
    RenderingMode: v.renderingMode ?? 0,
  };
  set(info, 'PositionX', v.positionX);
  set(info, 'PositionY', v.positionY);
  set(info, 'Width', v.width);
  set(info, 'Height', v.height);
  set(info, 'Page', v.page);
  if (v.additionalSignaturePositions !== undefined) {
    info.SignaturePosInfos = v.additionalSignaturePositions.map((p) => ({
      positionX: p.positionX,
      positionY: p.positionY,
      width: p.width,
      height: p.height,
      page: p.page,
    }));
  }
  return {
    format,
    fileToSign: encodeBase64(bytes),
    documentName: v.documentName,
    documentId: newDocumentId(v.documentId),
    dataToBeDisplayed: v.dataToBeDisplayed,
    signatureInfo: info,
  };
}
