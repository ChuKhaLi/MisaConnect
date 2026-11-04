import { randomBytes } from 'node:crypto';
import { describe, expect, it } from 'vitest';
import { toNormalized } from '../../src/application/mapping/sign-request-mapper.js';
import type { Certificate, SignPdfRequestDto, SignXmlRequestDto } from '../../src/domain/dtos.js';
import { ESignGeneralException } from '../../src/domain/errors.js';
import { decodeBase64, encodeBase64 } from '../../src/infrastructure/wire/base64.js';
import { FORMATS } from '../../src/infrastructure/wire/formats.js';

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type Json = Record<string, any>;

const cert: Certificate = {
  userId: 'u',
  keyAlias: 'ka',
  appName: 'misa',
  keyStatus: 'ACTIVE',
  certificate: 'CERT',
  chain: ['SIGN', 'INT', 'ROOT'],
  isAutoSign: false,
};
const ctx = { correlationId: 'corr' };
const ALL = ['pdfDocs', 'xmlDocs', 'wordDocs', 'excelDocs'] as const;

const visible = {
  documentName: 'doc',
  signerName: 'Alice',
  location: 'Hanoi',
  reason: 'R',
  contact: 'c',
  logoImageBase64: 'LOGO',
  dataToBeDisplayed: 'D',
};
const pdfDto = (over: Partial<SignPdfRequestDto> = {}): SignPdfRequestDto => ({
  ...visible,
  pdf: new Uint8Array([1, 2, 3]),
  ...over,
});
const xmlDto = (over: Partial<SignXmlRequestDto> = {}): SignXmlRequestDto => ({
  xml: '<a/>',
  documentName: 'doc',
  dataToBeDisplayed: 'D',
  documentId: 'x-1',
  signatureContext: {
    signatureName: 'sig',
    hashAlgorithm: 'SHA256',
    signatureDescription: { signedBy: 'Alice', location: 'H', reason: 'R', contact: 'c' },
  },
  ...over,
});

const norm = (f: 'Pdf' | 'Word' | 'Excel', over = {}) => {
  const bytes = new Uint8Array([1, 2, 3]);
  const key = f.toLowerCase();
  return toNormalized(
    f,
    { ...visible, [key]: bytes, documentId: 'd-1', ...over } as never,
    encodeBase64,
  );
};

const entry = (body: unknown, key: string): Json => (body as Json)[key][0];

function catchErr(fn: () => unknown): ESignGeneralException {
  try {
    fn();
  } catch (e) {
    expect(e).toBeInstanceOf(ESignGeneralException);
    return e as ESignGeneralException;
  }
  throw new Error('expected throw');
}

describe('hash bodies', () => {
  it.each([
    ['Pdf', 'pdfDocs'],
    ['Word', 'wordDocs'],
    ['Excel', 'excelDocs'],
  ] as const)('%s hash body omits unused arrays', (f, key) => {
    const body = FORMATS[f].buildHashBody(cert, norm(f)) as Json;
    expect(FORMATS[f].docsKey).toBe(key);
    expect(Object.keys(body).sort()).toEqual(['certificate', 'certificateChain', key].sort());
    expect(body['certificate']).toBe('CERT');
    expect(body['certificateChain']).toEqual(['SIGN', 'INT', 'ROOT']);
    for (const other of ALL) if (other !== key) expect(other in body).toBe(false);
  });

  it('xml hash body omits unused arrays and keeps raw text', () => {
    const n = toNormalized('Xml', xmlDto(), encodeBase64);
    const body = FORMATS.Xml.buildHashBody(cert, n) as Json;
    expect(Object.keys(body).sort()).toEqual(['certificate', 'certificateChain', 'xmlDocs']);
    const e = entry(body, 'xmlDocs');
    expect(Object.keys(e).sort()).toEqual(['DocumentId', 'FileToSign', 'SignatureInfo']);
    expect(e['FileToSign']).toBe('<a/>');
    expect(e['DocumentId']).toBe('x-1');
  });

  it('pdf SignatureInfo has full PascalCase shape; undefined fields omitted', () => {
    const n = norm('Pdf', {
      renderingMode: 1,
      positionX: 10,
      page: 3,
      displayText: 'T',
      additionalSignaturePositions: [{ positionX: 1, positionY: 2, width: 3, height: 4, page: 5 }],
    });
    const e = entry(FORMATS.Pdf.buildHashBody(cert, n), 'pdfDocs');
    expect(e['FileToSign']).toBe('AQID');
    const si = e['SignatureInfo'] as Json;
    expect(Object.keys(si).sort()).toEqual(
      [
        'HashAlgorithm',
        'LogoImage',
        'Page',
        'PositionX',
        'RenderingMode',
        'SignatureDescription',
        'SignatureName',
        'SignaturePosInfos',
      ].sort(),
    );
    expect(si['SignatureName']).toBe('Alice');
    expect(si['HashAlgorithm']).toBe('SHA256');
    expect(si['LogoImage']).toBe('LOGO');
    expect(si['RenderingMode']).toBe(1);
    expect(si['Page']).toBe(3);
    expect(si['SignatureDescription']).toEqual({
      SignedBy: 'Alice',
      ShowSignedDate: true,
      Location: 'Hanoi',
      Reason: 'R',
      Contact: 'c',
      DisplayText: 'T',
    });
    expect(si['SignaturePosInfos']).toEqual([
      { positionX: 1, positionY: 2, width: 3, height: 4, page: 5 },
    ]);
  });

  it('empty additional positions are omitted; showSignedDate false is kept', () => {
    const n = norm('Pdf', { additionalSignaturePositions: [], showSignedDate: false });
    const si = entry(FORMATS.Pdf.buildHashBody(cert, n), 'pdfDocs')['SignatureInfo'] as Json;
    expect('SignaturePosInfos' in si).toBe(false);
    expect(si['SignatureDescription'].ShowSignedDate).toBe(false);
    expect(si['RenderingMode']).toBe(0);
  });

  it.each(['Pdf', 'Word', 'Excel'] as const)(
    '%s: null page is sent as 1; explicit preserved',
    (f) => {
      const key = FORMATS[f].docsKey;
      const get = (n: ReturnType<typeof norm>): unknown =>
        (entry(FORMATS[f].buildHashBody(cert, n), key)['SignatureInfo'] as Json)['Page'];
      expect(get(norm(f))).toBe(1);
      expect(get(norm(f, { page: 3 }))).toBe(3);
    },
  );

  it('xml SignatureInfo is slim, has no Page, and non-SHA256 becomes SHA256', () => {
    const n = toNormalized(
      'Xml',
      xmlDto({ signatureContext: { ...xmlDto().signatureContext, hashAlgorithm: 'SHA1' } }),
      encodeBase64,
    );
    const si = entry(FORMATS.Xml.buildHashBody(cert, n), 'xmlDocs')['SignatureInfo'] as Json;
    expect(Object.keys(si).sort()).toEqual(
      [
        'HashAlgorithm',
        'LogoImage',
        'RenderingMode',
        'SignatureDescription',
        'SignatureName',
      ].sort(),
    );
    expect(si['HashAlgorithm']).toBe('SHA256');
    expect(si['LogoImage']).toBe('');
    expect(si['RenderingMode']).toBe(0);
  });
});

describe('mapper', () => {
  it('generates a GUID document id when absent and keeps explicit ones', () => {
    const n = toNormalized('Pdf', pdfDto(), encodeBase64);
    expect(n.documentId).toMatch(/^[0-9a-f-]{36}$/);
    expect(toNormalized('Pdf', pdfDto({ documentId: 'mine' }), encodeBase64).documentId).toBe(
      'mine',
    );
    expect(toNormalized('Pdf', pdfDto({ documentId: '' }), encodeBase64).documentId).toHaveLength(
      36,
    );
  });

  it('maps signerName to SignatureName and SignedBy', () => {
    const n = toNormalized('Pdf', pdfDto(), encodeBase64);
    expect(n.signatureInfo.SignatureName).toBe('Alice');
    expect(n.signatureInfo.SignatureDescription.SignedBy).toBe('Alice');
    expect(n.format).toBe('Pdf');
    expect(n.documentName).toBe('doc');
    expect(n.dataToBeDisplayed).toBe('D');
  });

  it('xml: exactly one input', () => {
    expect(() =>
      toNormalized('Xml', xmlDto({ xmlUtf8Bytes: new Uint8Array([60]) }), encodeBase64),
    ).toThrow();
    const none = withoutXml(xmlDto());
    expect(() => toNormalized('Xml', none, encodeBase64)).toThrow();
  });

  it('xml utf8 round trip keeps Vietnamese text', () => {
    const text = '<a>Hóa đơn điện tử</a>';
    const bytes = new TextEncoder().encode(text);
    const rest = withoutXml(xmlDto());
    const n = toNormalized('Xml', { ...rest, xmlUtf8Bytes: bytes }, encodeBase64);
    expect(n.fileToSign).toBe(text);
    const body = FORMATS.Xml.buildHashBody(cert, n);
    expect(entry(body, 'xmlDocs')['FileToSign']).toBe(text);
    const out = FORMATS.Xml.parseAttach({ xmlDocs: [{ documentId: 'x-1', document: text }] }, ctx);
    expect(new TextDecoder().decode(out)).toBe(text);
    expect(Array.from(out)).toEqual(Array.from(bytes));
  });
});

describe('hash parsing', () => {
  it('pdf parses all fields into plain JSON data', () => {
    const out = FORMATS.Pdf.parseHash(
      {
        pdfDocs: [
          {
            documentId: 'd',
            documentBytes: 'b',
            documentHash: 'h',
            sh: 's',
            signatureName: 'n',
            digest: 'dg',
          },
        ],
      },
      ctx,
    );
    expect(out).toMatchObject({
      documentId: 'd',
      documentBytes: 'b',
      documentHash: 'h',
      sh: 's',
      signatureName: 'n',
      digest: 'dg',
    });
    expect(JSON.parse(JSON.stringify(out))).toEqual(out);
  });

  it('pdf empty digest -> HashRejected EmptyDigest', () => {
    const e = catchErr(() =>
      FORMATS.Pdf.parseHash({ pdfDocs: [{ documentId: 'd', digest: '' }] }, ctx),
    );
    expect(e.category).toBe('HashRejected');
    expect(e.rawCode).toBe('EmptyDigest');
    expect(e.correlationId).toBe('corr');
  });

  it.each([
    ['Pdf', {}],
    ['Xml', { xmlDocs: [] }],
    ['Word', { pdfDocs: [{}] }],
    ['Excel', null],
  ] as const)('%s missing array -> MisaUnknown EmptyResponse', (f, json) => {
    const e = catchErr(() => FORMATS[f].parseHash(json, ctx));
    expect(e.category).toBe('MisaUnknown');
    expect(e.rawCode).toBe('EmptyResponse');
    expect(e.format).toBe(f);
  });

  it('xml / word / excel incomplete -> IncompleteHashResponse', () => {
    const cases = [
      ['Xml', { xmlDocs: [{ documentId: 'd', document: 'x', signatureId: 's', digest: 'g' }] }],
      [
        'Word',
        { wordDocs: [{ documentId: 'd', documentBytes: 'x', signatureId: 's', digest: 'g' }] },
      ],
      [
        'Excel',
        { excelDocs: [{ documentId: 'd', documentBytes: 'x', signatureId: 's', mainDom: 'm' }] },
      ],
    ] as const;
    for (const [f, json] of cases) {
      const e = catchErr(() => FORMATS[f].parseHash(json, ctx));
      expect(e.category).toBe('HashRejected');
      expect(e.rawCode).toBe('IncompleteHashResponse');
      expect(e.format).toBe(f);
    }
  });

  it('complete xml/word parse', () => {
    expect(
      FORMATS.Xml.parseHash(
        { xmlDocs: [{ documentId: 'd', document: 'x', signatureId: 's', digest: 'g', sh: 'h' }] },
        ctx,
      ),
    ).toMatchObject({ document: 'x', signatureId: 's', digest: 'g', sh: 'h' });
    expect(
      FORMATS.Word.parseHash(
        {
          wordDocs: [
            { documentId: 'd', documentBytes: 'x', signatureId: 's', digest: 'g', mainDom: 'm' },
          ],
        },
        ctx,
      ),
    ).toMatchObject({ documentBytes: 'x', mainDom: 'm' });
  });
});

describe('attach bodies and parsing', () => {
  const pdfHash = FORMATS.Pdf.parseHash(
    {
      pdfDocs: [
        {
          documentId: 'd',
          documentBytes: 'b',
          documentHash: 'h',
          sh: 's',
          signatureName: 'n',
          digest: 'dg',
        },
      ],
    },
    ctx,
  );
  const xmlHash = FORMATS.Xml.parseHash(
    { xmlDocs: [{ documentId: 'd', document: 'doc', signatureId: 'sid', digest: 'dg', sh: 's' }] },
    ctx,
  );

  it('pdf attach entry keys', () => {
    const body = FORMATS.Pdf.buildAttachBody(cert, pdfHash, 'SIG', norm('Pdf')) as Json;
    expect(Object.keys(body).sort()).toEqual(['certificate', 'certificateChain', 'pdfDocs']);
    expect(entry(body, 'pdfDocs')).toEqual({
      signature: 'SIG',
      documentId: 'd',
      documentBytes: 'b',
      digest: 'dg',
      signatureName: 'n',
      sh: 's',
      documentHash: 'h',
    });
  });

  it('xml attach entry keys', () => {
    const body = FORMATS.Xml.buildAttachBody(
      cert,
      xmlHash,
      'SIG',
      toNormalized('Xml', xmlDto(), encodeBase64),
    ) as Json;
    expect(Object.keys(body).sort()).toEqual(['certificate', 'certificateChain', 'xmlDocs']);
    expect(entry(body, 'xmlDocs')).toEqual({
      signature: 'SIG',
      documentId: 'd',
      documentBytes: 'doc',
      digest: 'dg',
      signatureName: '',
      sh: 's',
      signatureId: 'sid',
      documentHash: '',
    });
  });

  it.each(['Word', 'Excel'] as const)('%s attach entry keys', (f) => {
    const key = FORMATS[f].docsKey;
    const hash = FORMATS[f].parseHash(
      {
        [key]: [
          { documentId: 'd', documentBytes: 'b', signatureId: 'sid', digest: 'dg', mainDom: 'md' },
        ],
      },
      ctx,
    );
    const body = FORMATS[f].buildAttachBody(cert, hash, '', norm(f)) as Json;
    expect(Object.keys(body).sort()).toEqual(['certificate', 'certificateChain', key].sort());
    expect(entry(body, key)).toEqual({
      signature: '',
      documentId: 'd',
      documentBytes: 'b',
      digest: 'dg',
      mainDom: 'md',
      signatureName: '',
      sh: '',
      signatureId: 'sid',
      documentHash: '',
    });
  });

  it('attach responses decode', () => {
    const dec = (f: 'Pdf' | 'Word' | 'Excel'): number[] =>
      Array.from(FORMATS[f].parseAttach({ [FORMATS[f].docsKey]: [{ document: 'AQID' }] }, ctx));
    expect(dec('Pdf')).toEqual([1, 2, 3]);
    expect(dec('Word')).toEqual([1, 2, 3]);
    expect(dec('Excel')).toEqual([1, 2, 3]);
  });

  it('attach missing document: pdf -> MisaUnknown EmptyResponse, others -> AttachmentRejected', () => {
    const p = catchErr(() => FORMATS.Pdf.parseAttach({ pdfDocs: [{ document: '' }] }, ctx));
    expect([p.category, p.rawCode]).toEqual(['MisaUnknown', 'EmptyResponse']);
    for (const [f, json] of [
      ['Xml', {}],
      ['Word', { wordDocs: [] }],
      ['Excel', { excelDocs: [{}] }],
    ] as const) {
      const e = catchErr(() => FORMATS[f].parseAttach(json, ctx));
      expect([e.category, e.rawCode, e.format]).toEqual([
        'AttachmentRejected',
        'MissingSignedDocument',
        f,
      ]);
    }
  });
});

describe('base64', () => {
  it('large pdf base64 round trip', () => {
    const bytes = new Uint8Array(randomBytes(5 * 1024 * 1024));
    const t0 = performance.now();
    const back = decodeBase64(encodeBase64(bytes));
    expect(performance.now() - t0).toBeLessThan(2000);
    expect(Buffer.compare(Buffer.from(back), Buffer.from(bytes))).toBe(0);
  });
});

function withoutXml(d: SignXmlRequestDto): Omit<SignXmlRequestDto, 'xml'> {
  const c: Partial<SignXmlRequestDto> = { ...d };
  delete c.xml;
  return c as Omit<SignXmlRequestDto, 'xml'>;
}

describe('fix round 1', () => {
  it('xml bytes keep a leading BOM like .NET', () => {
    const bytes = new Uint8Array([0xef, 0xbb, 0xbf, ...new TextEncoder().encode('<a>x</a>')]);
    const rest = withoutXml(xmlDto());
    const n = toNormalized('Xml', { ...rest, xmlUtf8Bytes: bytes }, encodeBase64);
    expect(n.fileToSign.startsWith('﻿')).toBe(true);
    expect(n.fileToSign).toBe('﻿<a>x</a>');
  });

  it('decodeBase64 returns an exact copy, not a pooled view', () => {
    const out = decodeBase64('AQID');
    expect(out.byteOffset).toBe(0);
    expect(out.buffer.byteLength).toBe(out.length);
    expect(Array.from(out)).toEqual([1, 2, 3]);
  });

  it('pdf attach omits sh/signatureName/documentHash when the hash response had null', () => {
    const hash = FORMATS.Pdf.parseHash(
      { pdfDocs: [{ documentId: 'd', documentBytes: 'b', sh: null, digest: 'dg' }] },
      ctx,
    );
    expect(JSON.parse(JSON.stringify(hash))).toEqual({
      format: 'Pdf',
      documentId: 'd',
      documentBytes: 'b',
      digest: 'dg',
    });
    const body = FORMATS.Pdf.buildAttachBody(cert, hash, 'SIG', norm('Pdf'));
    const e = JSON.parse(JSON.stringify(entry(body, 'pdfDocs'))) as Json;
    expect(Object.keys(e).sort()).toEqual(['digest', 'documentBytes', 'documentId', 'signature']);
  });
});
