import { describe, expect, it } from 'vitest';
import { validateSignRequest } from '../../src/application/validation/sign-request-validator.js';
import type { SignPdfRequestDto, SignXmlRequestDto } from '../../src/domain/dtos.js';
import { ESignGeneralException } from '../../src/domain/errors.js';

const pdf = (over: Partial<SignPdfRequestDto> = {}): SignPdfRequestDto => ({
  pdf: new Uint8Array([1, 2, 3]),
  documentName: 'doc.pdf',
  signerName: 'Alice',
  location: 'Hanoi',
  reason: 'Approve',
  contact: 'a@b.vn',
  logoImageBase64: 'TE9HTw==',
  dataToBeDisplayed: 'Display',
  ...over,
});

const xml = (over: Partial<SignXmlRequestDto> = {}): SignXmlRequestDto => ({
  xml: '<a/>',
  signatureContext: {
    signatureName: 'sig',
    hashAlgorithm: 'SHA256',
    signatureDescription: { signedBy: 'Alice', location: 'Hanoi', reason: 'R', contact: 'c' },
  },
  documentName: 'doc.xml',
  dataToBeDisplayed: 'Display',
  ...over,
});

function failure(fn: () => void): ESignGeneralException {
  try {
    fn();
  } catch (e) {
    expect(e).toBeInstanceOf(ESignGeneralException);
    return e as ESignGeneralException;
  }
  throw new Error('expected validation to fail');
}

describe('validateSignRequest', () => {
  it('accepts a valid request for every format', () => {
    expect(() => validateSignRequest('Pdf', pdf(), 'c')).not.toThrow();
    expect(() => validateSignRequest('Xml', xml(), 'c')).not.toThrow();
    const { pdf: bytes, ...rest } = pdf();
    expect(() => validateSignRequest('Word', { ...rest, word: bytes }, 'c')).not.toThrow();
    expect(() => validateSignRequest('Excel', { ...rest, excel: bytes }, 'c')).not.toThrow();
  });

  it.each([
    ['Pdf', 'InvalidSignPdfRequest'],
    ['Word', 'InvalidSignWordRequest'],
    ['Excel', 'InvalidSignExcelRequest'],
  ] as const)('%s failure carries category, code and format', (format, code) => {
    const key = format.toLowerCase();
    const rest = withoutPdf(pdf());
    const dto = { ...rest, [key]: new Uint8Array(0) } as never;
    const e = failure(() => validateSignRequest(format, dto, 'corr'));
    expect(e.category).toBe('Validation');
    expect(e.rawCode).toBe(code);
    expect(e.format).toBe(format);
    expect(e.correlationId).toBe('corr');
    expect(e.detail).toContain('bytes must be non-empty');
  });

  const cases: [string, Partial<SignPdfRequestDto>, string][] = [
    ['empty bytes', { pdf: new Uint8Array(0) }, 'Pdf bytes must be non-empty'],
    ['blank documentName', { documentName: ' ' }, 'DocumentName is required'],
    ['long documentName', { documentName: 'x'.repeat(101) }, 'DocumentName must be <= 100'],
    ['long documentId', { documentId: 'x'.repeat(37) }, 'DocumentId must be <= 36'],
    ['blank documentId', { documentId: '  ' }, 'DocumentId is required'],
    ['blank dataToBeDisplayed', { dataToBeDisplayed: '' }, 'DataToBeDisplayed is required'],
    ['blank signerName', { signerName: '' }, 'SignatureName is required'],
    ['blank logo', { logoImageBase64: '' }, 'LogoImage is required'],
    ['blank location', { location: '' }, 'Location is required'],
    ['blank reason', { reason: '' }, 'Reason is required'],
    ['blank contact', { contact: '' }, 'Contact is required'],
    ['rendering mode 3', { renderingMode: 3 }, 'RenderingMode must be 0, 1, or 2'],
    ['rendering mode -1', { renderingMode: -1 }, 'RenderingMode must be 0, 1, or 2'],
    ['page 0', { page: 0 }, 'Page must be >= 1'],
  ];
  it.each(cases)('pdf rejects %s', (_n, over, msg) => {
    const e = failure(() => validateSignRequest('Pdf', pdf(over), 'c'));
    expect(e.detail).toContain(msg);
  });

  it('accepts boundary values', () => {
    const over = {
      documentName: 'x'.repeat(100),
      documentId: 'x'.repeat(36),
      renderingMode: 2,
      page: 1,
    };
    expect(() => validateSignRequest('Pdf', pdf(over), 'c')).not.toThrow();
  });

  it('lists all failures together', () => {
    const e = failure(() => validateSignRequest('Pdf', pdf({ reason: '', contact: '' }), 'c'));
    expect(e.detail).toContain('Reason is required');
    expect(e.detail).toContain('Contact is required');
  });

  it('word and excel do not check Page', () => {
    const { pdf: bytes, ...rest } = pdf({ page: 0 });
    expect(() => validateSignRequest('Word', { ...rest, word: bytes }, 'c')).not.toThrow();
  });

  it('xml: both inputs fail', () => {
    const e = failure(() =>
      validateSignRequest('Xml', xml({ xmlUtf8Bytes: new Uint8Array([60]) }), 'c'),
    );
    expect(e.rawCode).toBe('InvalidSignXmlRequest');
    expect(e.format).toBe('Xml');
    expect(e.detail).toContain('exactly one of');
  });

  it('xml: neither input fails', () => {
    const noXml = withoutXml(xml());
    const e = failure(() => validateSignRequest('Xml', noXml, 'c'));
    expect(e.detail).toContain('exactly one of');
  });

  it('xml: bytes only passes; empty content fails', () => {
    const noXml = withoutXml(xml());
    expect(() =>
      validateSignRequest('Xml', { ...noXml, xmlUtf8Bytes: new Uint8Array([60, 97, 47, 62]) }, 'c'),
    ).not.toThrow();
    const e = failure(() => validateSignRequest('Xml', xml({ xml: '' }), 'c'));
    expect(e.detail).toContain('Xml content must be non-empty');
  });

  it('xml: context rules, no logo/rendering checks', () => {
    const base = xml();
    const e = failure(() =>
      validateSignRequest(
        'Xml',
        xml({
          signatureContext: {
            ...base.signatureContext,
            signatureName: '',
            signatureDescription: { signedBy: '', location: '', reason: '', contact: '' },
          },
        }),
        'c',
      ),
    );
    for (const m of ['SignatureName', 'SignedBy', 'Location', 'Reason', 'Contact']) {
      expect(e.detail).toContain(m);
    }
    expect(e.detail).not.toContain('LogoImage');
    expect(e.detail).not.toContain('RenderingMode');
  });
});

function withoutPdf(d: SignPdfRequestDto): Omit<SignPdfRequestDto, 'pdf'> {
  const c: Partial<SignPdfRequestDto> = { ...d };
  delete c.pdf;
  return c as Omit<SignPdfRequestDto, 'pdf'>;
}

function withoutXml(d: SignXmlRequestDto): Omit<SignXmlRequestDto, 'xml'> {
  const c: Partial<SignXmlRequestDto> = { ...d };
  delete c.xml;
  return c as Omit<SignXmlRequestDto, 'xml'>;
}
