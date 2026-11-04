import type {
  SignExcelRequestDto,
  SignPdfRequestDto,
  SignWordRequestDto,
  SignXmlRequestDto,
} from '../../domain/dtos.js';
import { ESignGeneralException } from '../../domain/errors.js';
import {
  documentBytesOf,
  type SignFormat,
  type SignRequestDtoByFormat,
} from '../mapping/sign-request-mapper.js';

const blank = (s: string | undefined | null): boolean =>
  s === undefined || s === null || s.trim() === '';

function checkCommon(
  errors: string[],
  r: { documentName: string; documentId?: string | undefined; dataToBeDisplayed: string },
): void {
  if (blank(r.documentName)) errors.push('DocumentName is required.');
  else if (r.documentName.length > 100) errors.push('DocumentName must be <= 100 characters.');

  // An absent or empty id is replaced by a generated GUID in the mapper; only a given one is checked.
  if (r.documentId !== undefined && r.documentId !== '') {
    if (blank(r.documentId)) errors.push('DocumentId is required.');
    else if (r.documentId.length > 36) errors.push('DocumentId must be <= 36 characters.');
  }
  if (blank(r.dataToBeDisplayed)) errors.push('DataToBeDisplayed is required.');
}

function checkDescription(
  errors: string[],
  signedBy: string | undefined,
  d: { location: string; reason: string; contact: string },
): void {
  if (blank(signedBy)) errors.push('SignatureDescription.SignedBy is required.');
  if (blank(d.location)) errors.push('SignatureDescription.Location is required.');
  if (blank(d.reason)) errors.push('SignatureDescription.Reason is required.');
  if (blank(d.contact)) errors.push('SignatureDescription.Contact is required.');
}

/**
 * Validates a sign request before any network call. Throws `ESignGeneralException`
 * (`Validation`, `InvalidSign<Format>Request`) listing every failure.
 */
export function validateSignRequest<F extends SignFormat>(
  format: F,
  dto: SignRequestDtoByFormat[F],
  correlationId: string,
): void {
  const errors: string[] = [];

  if (format === 'Xml') {
    const x = dto as SignXmlRequestDto;
    const hasString = x.xml !== undefined;
    const hasBytes = x.xmlUtf8Bytes !== undefined;
    if (hasString === hasBytes) {
      errors.push('Provide exactly one of xml or xmlUtf8Bytes.');
    } else if (hasString ? x.xml === '' : x.xmlUtf8Bytes?.length === 0) {
      errors.push('Xml content must be non-empty.');
    }
    checkCommon(errors, x);
    const ctx = x.signatureContext as SignXmlRequestDto['signatureContext'] | undefined;
    if (ctx === undefined) {
      errors.push('SignatureContext is required.');
    } else {
      if (blank(ctx.signatureName)) errors.push('SignatureContext.SignatureName is required.');
      const d = ctx.signatureDescription as typeof ctx.signatureDescription | undefined;
      if (d === undefined) errors.push('SignatureContext.SignatureDescription is required.');
      else checkDescription(errors, d.signedBy, d);
    }
  } else {
    const v = dto as SignPdfRequestDto | SignWordRequestDto | SignExcelRequestDto;
    const bytes = documentBytesOf(format as Exclude<SignFormat, 'Xml'>, v);
    if (bytes === undefined || bytes.length === 0) {
      errors.push(`${format} bytes must be non-empty.`);
    }
    checkCommon(errors, v);
    // SignatureName and SignedBy are both the signerName.
    if (blank(v.signerName)) errors.push('SignatureInfo.SignatureName is required.');
    if (blank(v.logoImageBase64)) errors.push('SignatureInfo.LogoImage is required.');
    checkDescription(errors, v.signerName, v);
    if (v.renderingMode !== undefined && (v.renderingMode < 0 || v.renderingMode > 2)) {
      errors.push('SignatureInfo.RenderingMode must be 0, 1, or 2.');
    }
    if (format === 'Pdf' && v.page !== undefined && v.page < 1) {
      errors.push('SignatureInfo.Page must be >= 1 when specified.');
    }
  }

  if (errors.length > 0) {
    throw new ESignGeneralException({
      category: 'Validation',
      rawCode: `InvalidSign${format}Request`,
      detail: `Sign${format}Request failed validation: ${errors.join('; ')}`,
      correlationId,
      format,
    });
  }
}
