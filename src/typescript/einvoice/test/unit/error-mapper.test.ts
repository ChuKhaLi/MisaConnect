import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, it, expect } from 'vitest';
import {
  EXACT_ERROR_CODES,
  PREFIX_ERROR_CODES,
  mapErrorCode,
} from '../../src/application/error-mapper.js';
import type { MeInvoiceErrorCategory } from '../../src/domain/errors.js';

// This table was copied from src/dotnet/.../Errors/MeInvoiceErrorMapper.cs (ExactMap). The
// "matches the .NET ExactMap" test below parses that file and guards against drift.
const exact: ReadonlyArray<readonly [string, MeInvoiceErrorCategory]> = [
  ['InvalidAppID', 'Configuration'],
  ['InActiveAppID', 'Configuration'],
  ['InvalidTaxCode', 'Configuration'],
  ['LicenseInfo_NotBuy', 'Configuration'],
  ['LicenseInfo_OutOfInvoice', 'Configuration'],
  ['LicenseInfo_Expired', 'Configuration'],
  ['UnAuthorize', 'Authentication'],
  ['TokenExpiredCode', 'Authentication'],
  ['InvalidTokenCode', 'Authentication'],
  ['DuplicateTemplateName', 'TemplateState'],
  ['DuplicateTemplateNo', 'TemplateState'],
  ['InvoiceTemplateNotExist', 'TemplateState'],
  ['TemplateIsNotUsing', 'TemplateState'],
  ['NoActiveTemplate', 'TemplateState'],
  ['UnknownTemplate', 'TemplateState'],
  ['AmbiguousTemplate', 'Configuration'],
  ['DeclarationNotExist', 'DeclarationState'],
  ['InvalidDeclaration', 'DeclarationState'],
  ['ExistDeclarationNotReceive', 'DeclarationState'],
  ['ExistsInvoiceNextYear', 'DeclarationState'],
  ['InvoiceTemplateNotValidInDeclaration', 'DeclarationState'],
  ['InvalidXMLData', 'Validation'],
  ['TaxRateInfo_VATRateName', 'Validation'],
  ['BuyerTaxCode_TaxCodeInvalid', 'Validation'],
  ['InvalidInvoiceDate', 'Validation'],
  ['TaxReductionDateInValid', 'Validation'],
  ['InvalidXMLContainEmoji', 'Validation'],
  ['CreateInvoiceDataError', 'Validation'],
  ['InvalidInvoiceData', 'Validation'],
  ['InvoiceQuantityTooLarge', 'BatchOrSize'],
  ['XMLTooLong', 'BatchOrSize'],
  ['DuplicateInvoiceRefID', 'DuplicateOrUniqueness'],
  ['DuplicateTransactionID', 'DuplicateOrUniqueness'],
  ['InvoiceDuplicated', 'DuplicateOrUniqueness'],
  ['InvoiceNumberNotCotinuous', 'DuplicateOrUniqueness'],
  ['InvalidTransactionID', 'ResourceNotFound'],
  ['RefIdNotFound', 'ResourceNotFound'],
  ['SignatureEmpty', 'Signing'],
  ['InvalidSignature', 'Signing'],
  ['CertRevocation', 'Signing'],
  ['InvalidCertByRegistration', 'Signing'],
  ['HasRegistrationStopUseCert', 'Signing'],
  ['SigningTimeNotInRegistration', 'Signing'],
  ['SignSoftDream78Exception', 'Signing'],
  ['SignEsignHSMError', 'Signing'],
  ['CallSignServiceFail', 'Signing'],
  ['X509SubjectName', 'Signing'],
  ['X509Certificate', 'Signing'],
  ['InvoiceCannotReplace', 'Replacement'],
  ['InvoiceCannotAdjust', 'Replacement'],
  ['InvoiceCannotReplaceByStatusNew', 'Replacement'],
  ['HasAdjustmentInvoice', 'Replacement'],
  ['InvalidInvNo', 'OtherWorkflow'],
  ['Exception', 'MisaUnavailable'],
];

describe('mapErrorCode', () => {
  it('has all 54 .NET ExactMap entries', () => {
    expect(exact).toHaveLength(54);
  });

  it.each(exact)('maps %s to %s', (code, category) => {
    const r = mapErrorCode(code, 'msg');
    expect(r.category).toBe(category);
    expect(r.rawCode).toBe(code);
    expect(r.detail).toBe('msg');
  });

  it('matches exact codes case-insensitively and keeps the raw code', () => {
    const r = mapErrorCode('unauthorize');
    expect(r.category).toBe('Authentication');
    expect(r.rawCode).toBe('unauthorize');
  });

  it('matches prefixes case-insensitively', () => {
    expect(mapErrorCode('invoicedetail_Quantity_Line3')).toMatchObject({
      category: 'Validation',
      field: 'Quantity',
      component: 'Line3',
    });
  });

  it('splits prefix tails into field and component', () => {
    expect(mapErrorCode('InvoiceDetail_Quantity_Line3')).toMatchObject({
      category: 'Validation',
      field: 'Quantity',
      component: 'Line3',
    });
  });

  it('maps StockInTaxCode_NotInfo_ prefix to OtherWorkflow', () => {
    expect(mapErrorCode('StockInTaxCode_NotInfo_Name').category).toBe('OtherWorkflow');
  });

  it('leaves field undefined for an empty prefix tail', () => {
    const r = mapErrorCode('RequireError_');
    expect(r.category).toBe('Validation');
    expect(r.field).toBeUndefined();
    expect(r.component).toBeUndefined();
  });

  it('maps unknown codes to MisaUnknown keeping the raw code', () => {
    expect(mapErrorCode('Whatever')).toMatchObject({
      category: 'MisaUnknown',
      rawCode: 'Whatever',
    });
  });

  it('maps empty/undefined to MisaUnknown with empty rawCode', () => {
    expect(mapErrorCode(undefined)).toMatchObject({ category: 'MisaUnknown', rawCode: '' });
    expect(mapErrorCode('')).toMatchObject({ category: 'MisaUnknown', rawCode: '' });
  });
});

describe('mapErrorCode detail', () => {
  it('treats an empty message like no message', () => {
    expect(mapErrorCode('InvalidTaxCode', '')).not.toHaveProperty('detail');
    expect(mapErrorCode('', '')).not.toHaveProperty('detail');
    expect(mapErrorCode('InvoiceDetail_Qty_X', '')).not.toHaveProperty('detail');
    expect(mapErrorCode('Unknown', '')).not.toHaveProperty('detail');
    expect(mapErrorCode('InvalidTaxCode', 'x').detail).toBe('x');
  });
});

const dotnetMapper = fileURLToPath(
  new URL(
    '../../../../dotnet/MisaConnect.EInvoice.Application/Errors/MeInvoiceErrorMapper.cs',
    import.meta.url,
  ),
);

describe.skipIf(!existsSync(dotnetMapper))('drift guard against MeInvoiceErrorMapper.cs', () => {
  const source = existsSync(dotnetMapper) ? readFileSync(dotnetMapper, 'utf8') : '';
  const entries = [...source.matchAll(/\["(\w+)"\]\s*=\s*MeInvoiceErrorCategory\.(\w+)/g)].map(
    (m) => [m[1] as string, m[2] as string] as const,
  );

  it('parses every .NET ExactMap entry', () => {
    expect(entries).toHaveLength(exact.length);
  });

  // .NET slice 009 aligned the batch-size codes (BatchOrSize), so there are no deviations left.
  it.each(entries)('maps %s like .NET (%s)', (code, dotnetCat) => {
    expect(mapErrorCode(code).category).toBe(dotnetCat);
  });

  it('has exactly the .NET ExactMap keys (no TS-only or missing codes)', () => {
    const dotnetKeys = new Set(entries.map(([code]) => code));
    const tsKeys = new Set(EXACT_ERROR_CODES.map(([code]) => code));
    expect(tsKeys.size).toBe(EXACT_ERROR_CODES.length);
    expect([...tsKeys].filter((k) => !dotnetKeys.has(k))).toEqual([]);
    expect([...dotnetKeys].filter((k) => !tsKeys.has(k))).toEqual([]);
    const differing = EXACT_ERROR_CODES.filter(
      ([code, cat]) => entries.find(([c]) => c === code)?.[1] !== cat,
    ).map(([code]) => code);
    expect(differing).toEqual([]);
  });

  it('has the .NET PrefixMap entries, in order', () => {
    const prefixes = [...source.matchAll(/\("(\w+_)",\s*MeInvoiceErrorCategory\.(\w+)\)/g)].map(
      (m) => [m[1] as string, m[2] as string],
    );
    expect(prefixes.length).toBeGreaterThan(0);
    expect(PREFIX_ERROR_CODES.map(([p, c]) => [p, c])).toEqual(prefixes);
  });
});
