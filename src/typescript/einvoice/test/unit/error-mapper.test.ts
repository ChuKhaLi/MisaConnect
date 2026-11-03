import { describe, it, expect } from 'vitest';
import { mapErrorCode } from '../../src/application/error-mapper.js';
import type { MeInvoiceErrorCategory } from '../../src/domain/errors.js';

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
