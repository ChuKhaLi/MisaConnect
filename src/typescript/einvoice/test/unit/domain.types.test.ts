import { describe, it, expectTypeOf } from 'vitest';
import type {
  InvoiceDto,
  InvoiceLineDto,
  Money,
  PdfDocumentDto,
  TemplateDto,
} from '../../src/domain/dtos.js';

describe('DTO types', () => {
  it('uses the naming rule and optional members', () => {
    expectTypeOf<InvoiceLineDto['vatAmountOC']>().toEqualTypeOf<Money | undefined>();
    expectTypeOf<TemplateDto>().toHaveProperty('ipTemplateID');
    expectTypeOf<TemplateDto>().toHaveProperty('isMoreVATRate');
    expectTypeOf<InvoiceDto['customFields']>().toEqualTypeOf<Record<number, string> | undefined>();
    expectTypeOf<PdfDocumentDto['content']>().toEqualTypeOf<Uint8Array>();
  });
});
