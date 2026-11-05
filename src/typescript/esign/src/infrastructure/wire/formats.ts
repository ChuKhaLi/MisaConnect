import type {
  NormalizedSignRequest,
  SignFormat,
  WireSignatureInfo,
} from '../../application/mapping/sign-request-mapper.js';
import type { Certificate } from '../../domain/dtos.js';
import { ESignGeneralException } from '../../domain/errors.js';
import { decodeBase64 } from './base64.js';
import type {
  HashOutput,
  PdfHashOutput,
  WordExcelHashOutput,
  XmlHashOutput,
} from '../../application/hash-output.js';
export type { HashOutput, PdfHashOutput, WordExcelHashOutput, XmlHashOutput };

export interface WireContext {
  correlationId: string;
}

export interface FormatSpec {
  docsKey: 'pdfDocs' | 'xmlDocs' | 'wordDocs' | 'excelDocs';
  buildHashBody(cert: Certificate, n: NormalizedSignRequest): object;
  parseHash(json: unknown, ctx: WireContext): HashOutput;
  buildAttachBody(
    cert: Certificate,
    hash: HashOutput,
    signature: string,
    n?: NormalizedSignRequest,
  ): object;
  parseAttach(json: unknown, ctx: WireContext): Uint8Array;
}

type Json = Record<string, unknown>;

const str = (v: unknown): string => (typeof v === 'string' ? v : '');
const optStr = (v: unknown): string | undefined => (typeof v === 'string' ? v : undefined);

function firstDoc(json: unknown, key: string): Json | undefined {
  if (typeof json !== 'object' || json === null) return undefined;
  const arr = (json as Json)[key];
  if (!Array.isArray(arr) || arr.length === 0) return undefined;
  const first: unknown = arr[0];
  return typeof first === 'object' && first !== null ? (first as Json) : undefined;
}

function emptyResponse(endpoint: string, key: string, format: SignFormat, ctx: WireContext) {
  return new ESignGeneralException({
    category: 'MisaUnknown',
    rawCode: 'EmptyResponse',
    detail: `MISA ${endpoint} returned no ${key}.`,
    correlationId: ctx.correlationId,
    format,
  });
}

/** Full-visibility SignatureInfo: a missing Page is sent as 1 (MISA requires Page >= 1). */
function visibleInfo(info: WireSignatureInfo): WireSignatureInfo {
  const { SignaturePosInfos, ...rest } = info;
  const out: WireSignatureInfo = { ...rest, Page: info.Page ?? 1 };
  // Empty additional positions are omitted, never sent as [].
  if (SignaturePosInfos !== undefined && SignaturePosInfos.length > 0) {
    out.SignaturePosInfos = SignaturePosInfos;
  }
  return out;
}

function certPart(cert: Certificate): { certificate: string; certificateChain: string[] } {
  return { certificate: cert.certificate, certificateChain: [...cert.chain] };
}

function hashBody(
  key: FormatSpec['docsKey'],
  cert: Certificate,
  n: NormalizedSignRequest,
  info: WireSignatureInfo,
): object {
  return {
    ...certPart(cert),
    [key]: [{ DocumentId: n.documentId, FileToSign: n.fileToSign, SignatureInfo: info }],
  };
}

function attachBody(key: FormatSpec['docsKey'], cert: Certificate, entry: object): object {
  return { ...certPart(cert), [key]: [entry] };
}

function wordExcel(format: 'Word' | 'Excel', key: 'wordDocs' | 'excelDocs'): FormatSpec {
  return {
    docsKey: key,
    buildHashBody: (cert, n) => hashBody(key, cert, n, visibleInfo(n.signatureInfo)),
    parseHash(json, ctx) {
      const d = firstDoc(json, key);
      if (d === undefined) throw emptyResponse('documents/hash', key, format, ctx);
      const out: WordExcelHashOutput = {
        format,
        documentId: optStr(d['documentId']),
        documentBytes: str(d['documentBytes']),
        signatureId: str(d['signatureId']),
        digest: str(d['digest']),
        mainDom: str(d['mainDom']),
      };
      if (!out.documentBytes || !out.signatureId || !out.digest || !out.mainDom) {
        throw new ESignGeneralException({
          category: 'HashRejected',
          rawCode: 'IncompleteHashResponse',
          detail: `MISA documents/hash returned an incomplete ${format} hash response (missing documentBytes, signatureId, digest, or mainDom).`,
          correlationId: ctx.correlationId,
          format,
        });
      }
      return out;
    },
    buildAttachBody(cert, hash, signature) {
      const h = hash as WordExcelHashOutput;
      return attachBody(key, cert, {
        signature,
        documentId: h.documentId,
        documentBytes: h.documentBytes,
        digest: h.digest,
        mainDom: h.mainDom,
        signatureName: '',
        sh: '',
        signatureId: h.signatureId,
        documentHash: '',
      });
    },
    parseAttach(json, ctx) {
      const doc = str(firstDoc(json, key)?.['document']);
      if (doc === '') throw missingSigned(format, ctx);
      return decodeBase64(doc);
    },
  };
}

function missingSigned(format: SignFormat, ctx: WireContext): ESignGeneralException {
  return new ESignGeneralException({
    category: 'AttachmentRejected',
    rawCode: 'MissingSignedDocument',
    detail: `MISA documents/attachment returned no signed ${format} document.`,
    correlationId: ctx.correlationId,
    format,
  });
}

const pdf: FormatSpec = {
  docsKey: 'pdfDocs',
  buildHashBody: (cert, n) => hashBody('pdfDocs', cert, n, visibleInfo(n.signatureInfo)),
  parseHash(json, ctx) {
    const d = firstDoc(json, 'pdfDocs');
    if (d === undefined) throw emptyResponse('documents/hash', 'pdfDocs', 'Pdf', ctx);
    const out: PdfHashOutput = {
      format: 'Pdf',
      documentId: optStr(d['documentId']),
      documentBytes: optStr(d['documentBytes']),
      documentHash: optStr(d['documentHash']),
      sh: optStr(d['sh']),
      signatureName: optStr(d['signatureName']),
      digest: str(d['digest']),
    };
    if (out.digest === '') {
      throw new ESignGeneralException({
        category: 'HashRejected',
        rawCode: 'EmptyDigest',
        detail: 'MISA documents/hash returned an empty digest.',
        correlationId: ctx.correlationId,
        format: 'Pdf',
      });
    }
    return out;
  },
  buildAttachBody(cert, hash, signature) {
    const h = hash as PdfHashOutput;
    return attachBody('pdfDocs', cert, {
      signature,
      documentId: h.documentId,
      documentBytes: h.documentBytes,
      digest: h.digest,
      signatureName: h.signatureName,
      sh: h.sh,
      documentHash: h.documentHash,
    });
  },
  parseAttach(json, ctx) {
    const doc = str(firstDoc(json, 'pdfDocs')?.['document']);
    if (doc === '') {
      throw new ESignGeneralException({
        category: 'MisaUnknown',
        rawCode: 'EmptyResponse',
        detail: 'MISA documents/attachment returned no signed document.',
        correlationId: ctx.correlationId,
        format: 'Pdf',
      });
    }
    return decodeBase64(doc);
  },
};

const xml: FormatSpec = {
  docsKey: 'xmlDocs',
  // XML is not page-positioned: the slim info is sent as-is (no Page default).
  buildHashBody: (cert, n) => hashBody('xmlDocs', cert, n, n.signatureInfo),
  parseHash(json, ctx) {
    const d = firstDoc(json, 'xmlDocs');
    if (d === undefined) throw emptyResponse('documents/hash', 'xmlDocs', 'Xml', ctx);
    const out: XmlHashOutput = {
      format: 'Xml',
      documentId: optStr(d['documentId']),
      document: str(d['document']),
      signatureId: str(d['signatureId']),
      digest: str(d['digest']),
      sh: str(d['sh']),
    };
    if (!out.document || !out.signatureId || !out.digest || !out.sh) {
      throw new ESignGeneralException({
        category: 'HashRejected',
        rawCode: 'IncompleteHashResponse',
        detail:
          'MISA documents/hash returned an incomplete xml hash response (missing document, signatureId, digest, or sh).',
        correlationId: ctx.correlationId,
        format: 'Xml',
      });
    }
    return out;
  },
  buildAttachBody(cert, hash, signature) {
    const h = hash as XmlHashOutput;
    return attachBody('xmlDocs', cert, {
      signature,
      documentId: h.documentId,
      documentBytes: h.document,
      digest: h.digest,
      signatureName: '',
      sh: h.sh,
      signatureId: h.signatureId,
      documentHash: '',
    });
  },
  parseAttach(json, ctx) {
    const doc = str(firstDoc(json, 'xmlDocs')?.['document']);
    if (doc === '') throw missingSigned('Xml', ctx);
    // The signed XML is UTF-8 text, not base64.
    return new TextEncoder().encode(doc);
  },
};

export const FORMATS: Record<SignFormat, FormatSpec> = {
  Pdf: pdf,
  Xml: xml,
  Word: wordExcel('Word', 'wordDocs'),
  Excel: wordExcel('Excel', 'excelDocs'),
};
