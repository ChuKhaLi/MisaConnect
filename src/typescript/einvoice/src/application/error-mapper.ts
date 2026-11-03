import { MeInvoiceErrorCategory } from '../domain/errors.js';

export interface MappedError {
  category: MeInvoiceErrorCategory;
  rawCode: string;
  detail?: string;
  field?: string;
  component?: string;
}

// Mirrors .NET MeInvoiceErrorMapper.ExactMap, except InvoiceQuantityTooLarge and
// XMLTooLong map to BatchOrSize (spec 9.3 deviation).
const EXACT: ReadonlyArray<readonly [string, MeInvoiceErrorCategory]> = [
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

const EXACT_MAP: ReadonlyMap<string, MeInvoiceErrorCategory> = new Map(
  EXACT.map(([code, category]) => [code.toLowerCase(), category]),
);

const PREFIX_MAP: ReadonlyArray<readonly [string, MeInvoiceErrorCategory]> = [
  ['InvoiceDetail_', MeInvoiceErrorCategory.Validation],
  ['RequireError_', MeInvoiceErrorCategory.Validation],
  ['StockInTaxCode_NotInfo_', MeInvoiceErrorCategory.OtherWorkflow],
];

export function mapErrorCode(raw: string | undefined, message?: string): MappedError {
  if (raw === undefined || raw === '') {
    return withDetail({ category: MeInvoiceErrorCategory.MisaUnknown, rawCode: '' }, message);
  }
  const lower = raw.toLowerCase();
  const exact = EXACT_MAP.get(lower);
  if (exact !== undefined) return withDetail({ category: exact, rawCode: raw }, message);

  for (const [prefix, category] of PREFIX_MAP) {
    if (lower.startsWith(prefix.toLowerCase())) {
      const parts = raw.slice(prefix.length).split('_');
      const result: MappedError = { category, rawCode: raw };
      if (parts[0] !== undefined && parts[0].length > 0) result.field = parts[0];
      if (parts[1] !== undefined && parts[1].length > 0) result.component = parts[1];
      return withDetail(result, message);
    }
  }
  return withDetail({ category: MeInvoiceErrorCategory.MisaUnknown, rawCode: raw }, message);
}

function withDetail(result: MappedError, message: string | undefined): MappedError {
  if (message !== undefined && message !== '') result.detail = message;
  return result;
}
