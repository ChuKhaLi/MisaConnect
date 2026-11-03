import { describe, expect, it } from 'vitest';
import {
  defaultInvoiceValidator,
  validateAmendment,
  validatePagedLookup,
} from '../../src/application/invoice-validator.js';
import type {
  InvoiceDto,
  InvoiceLineDto,
  InvoiceTotalsDto,
  OriginalInvoiceReferenceDto,
} from '../../src/domain/dtos.js';

const line = (over: Partial<InvoiceLineDto> = {}): InvoiceLineDto => ({
  inventoryItemType: 0,
  sortOrder: 1,
  description: 'Item',
  unitName: 'cái',
  quantity: 1,
  unitPrice: 100,
  amountOC: 100,
  amount: 100,
  amountWithoutVATOC: 100,
  amountWithoutVAT: 100,
  ...over,
});
const totals = (over: Partial<InvoiceTotalsDto> = {}): InvoiceTotalsDto => ({
  totalSaleAmountOC: 100,
  totalSaleAmount: 100,
  totalDiscountAmountOC: 0,
  totalDiscountAmount: 0,
  totalAmountWithoutVATOC: 100,
  totalAmountWithoutVAT: 100,
  totalVATAmountOC: 0,
  totalVATAmount: 0,
  totalAmountOC: 100,
  totalAmount: 100,
  totalAmountInWords: 'x',
  ...over,
});
const invoice = (over: Partial<InvoiceDto> = {}): InvoiceDto => ({
  refId: 'ref-1',
  invDate: '2026-01-01',
  createdDate: '2026-01-01T00:00:00+07:00',
  modifiedDate: '2026-01-01T00:00:00+07:00',
  currency: 'VND',
  exchangeRate: 1,
  paymentMethod: 'TM/CK',
  buyerType: 2,
  buyer: { name: 'Nguyen Van A' },
  lines: [line()],
  totals: totals(),
  ...over,
});
const v = (i: InvoiceDto) => defaultInvoiceValidator.validate(i);

describe('InvoiceValidator.validate', () => {
  it('accepts a valid invoice', () => {
    expect(v(invoice())).toEqual([]);
  });

  it('null invoice -> $ FR-006', () => {
    expect(v(null as unknown as InvoiceDto)).toEqual([
      { fieldPath: '$', message: 'Invoice is null.', ruleId: 'FR-006' },
    ]);
  });

  it('FR-021 empty refId', () => {
    expect(v(invoice({ refId: '' }))).toEqual([
      {
        fieldPath: 'refId',
        message: 'RefId must be supplied or service-generated.',
        ruleId: 'FR-021',
      },
    ]);
    expect(v(invoice({ refId: undefined as unknown as string }))[0]?.ruleId).toBe('FR-021');
  });

  it('FR-021 refId over 64 chars (64 passes)', () => {
    expect(v(invoice({ refId: 'a'.repeat(64) }))).toEqual([]);
    expect(v(invoice({ refId: 'a'.repeat(65) }))).toEqual([
      {
        fieldPath: 'refId',
        message: 'RefId length 65 exceeds the maximum of 64.',
        ruleId: 'FR-021',
      },
    ]);
  });

  it('FR-006 currency required', () => {
    expect(v(invoice({ currency: ' ' }))).toEqual([
      { fieldPath: 'currency', message: 'Currency is required.', ruleId: 'FR-006' },
    ]);
  });

  it('FR-023 non-VND needs exchangeRate > 0 (case-insensitive VND exempt)', () => {
    expect(v(invoice({ currency: 'USD', exchangeRate: 0 }))).toEqual([
      {
        fieldPath: 'exchangeRate',
        message: 'ExchangeRate must be supplied and > 0 for non-VND invoices.',
        ruleId: 'FR-023',
      },
    ]);
    expect(v(invoice({ currency: 'USD', exchangeRate: '24000.5' }))).toEqual([]);
    expect(v(invoice({ currency: 'vnd', exchangeRate: 0 }))).toEqual([]);
  });

  it('FR-024 organization buyer needs taxCode', () => {
    expect(v(invoice({ buyerType: 1, buyer: { name: 'ACME', taxCode: '  ' } }))).toEqual([
      {
        fieldPath: 'buyer.taxCode',
        message: 'Buyer tax code is required when BuyerType = Organization.',
        ruleId: 'FR-024',
      },
    ]);
    expect(v(invoice({ buyerType: 1, buyer: { name: 'ACME', taxCode: '0100' } }))).toEqual([]);
  });

  it('FR-027 paymentMethod required', () => {
    expect(v(invoice({ paymentMethod: '' }))).toEqual([
      { fieldPath: 'paymentMethod', message: 'PaymentMethod is required.', ruleId: 'FR-027' },
    ]);
  });

  it('FR-028 emoji: supplementary plane rejected, BMP symbols and Vietnamese pass', () => {
    const msg =
      'Field contains a supplementary-plane code point (likely emoji); MISA rejects these with InvalidXMLContainEmoji.';
    expect(v(invoice({ lines: [line({ unitName: '😀' })] }))).toEqual([
      { fieldPath: 'lines[0].unitName', message: msg, ruleId: 'FR-028' },
    ]);
    expect(v(invoice({ lines: [line({ unitName: 'Cà phê ☕' })] }))).toEqual([]);
    expect(
      v(invoice({ lines: [line(), line({ description: 'x😀' })] })).map((f) => f.fieldPath),
    ).toEqual(['lines[1].description']);
    const buyer = { name: '😀', contactName: '😀', address: '😀' };
    expect(v(invoice({ buyer })).map((f) => f.fieldPath)).toEqual([
      'buyer.name',
      'buyer.contactName',
      'buyer.address',
    ]);
  });

  it('FR-006 lines required', () => {
    expect(v(invoice({ lines: [] }))).toEqual([
      { fieldPath: 'lines', message: 'At least one invoice line is required.', ruleId: 'FR-006' },
    ]);
  });

  it('FR-030 more than 200 lines (200 passes)', () => {
    expect(v(invoice({ lines: Array.from({ length: 200 }, () => line()) }))).toEqual([]);
    expect(v(invoice({ lines: Array.from({ length: 201 }, () => line()) }))).toEqual([
      {
        fieldPath: 'lines',
        message: 'Line count 201 exceeds the MISA limit of 200 per invoice.',
        ruleId: 'FR-030',
      },
    ]);
  });

  it('FR-029 VAT rate must be in the accepted set', () => {
    expect(v(invoice({ lines: [line({ vatRateName: 'KHAC:0%' })] }))).toEqual([
      {
        fieldPath: 'lines[0].vatRateName',
        message:
          "VATRateName 'KHAC:0%' is not in the accepted set: the KHAC rate must be a positive percentage (> 0).",
        ruleId: 'FR-029',
      },
    ]);
    for (const ok of ['KCT', 'KKKNT', '0%', '5%', '8%', '10%', 'KHAC:3.5%']) {
      expect(v(invoice({ lines: [line({ vatRateName: ok })] }))).toEqual([]);
    }
    expect(v(invoice({ lines: [line({ vatRateName: '7%' })] }))[0]?.ruleId).toBe('FR-029');
    expect(v(invoice({ lines: [line({ vatRateName: 'KHAC:-2.5%' })] }))[0]?.message).toContain(
      'must be a positive percentage (> 0)',
    );
    // Thousands separators (accepted by .NET NumberStyles.Number) stay rejected, plainly.
    expect(v(invoice({ lines: [line({ vatRateName: 'KHAC:1,000%' })] }))[0]?.message).toBe(
      "VATRateName 'KHAC:1,000%' is not in the accepted set.",
    );
  });

  it('FR-025 discount reconciliation tolerance is 0.5 (inclusive)', () => {
    const lines = [line({ discountAmountOC: 10 }), line({ discountAmountOC: 5.25 })];
    for (const header of [15.25, 15.75, 14.75]) {
      expect(v(invoice({ lines, totals: totals({ totalDiscountAmountOC: header }) }))).toEqual([]);
    }
    const bad = v(invoice({ lines, totals: totals({ totalDiscountAmountOC: 15.76 }) }));
    expect(bad).toEqual([
      {
        fieldPath: 'totals.totalDiscountAmountOC',
        message:
          'Header TotalDiscountAmountOC 15.76 does not equal sum of goods-line DiscountAmountOC (15.25).',
        ruleId: 'FR-025',
      },
    ]);
  });

  it('FR-025 compares exactly (decimal semantics, not doubles)', () => {
    const run = (header: number | string, ...discounts: Array<number | string>) =>
      v(
        invoice({
          lines: discounts.map((d) => line({ discountAmountOC: d })),
          totals: totals({ totalDiscountAmountOC: header }),
        }),
      );
    expect(run(1.3, 0.8)).toEqual([]);
    expect(run('0.1', 0.2)).toEqual([]);
    expect(run(1.31, 0.8).map((f) => f.ruleId)).toEqual(['FR-025']);
    expect(run('12345678901234567.25', '12345678901234566.75')).toEqual([]);
    expect(run('12345678901234567.26', '12345678901234566.75').map((f) => f.ruleId)).toEqual([
      'FR-025',
    ]);
    expect(run(1e-7, 0)).toEqual([]);
  });

  it('FR-025 ignores exponent-form money strings without expanding them', () => {
    const start = Date.now();
    const inv = invoice({
      lines: [line({ discountAmountOC: '1e999999999' })],
      totals: totals({ totalDiscountAmountOC: 0 }),
    });
    expect(v(inv)).toEqual([]);
    const hdr = invoice({
      lines: [line({ discountAmountOC: 0 })],
      totals: totals({ totalDiscountAmountOC: '1e999999999' }),
    });
    expect(v(hdr)).toEqual([]);
    expect(Date.now() - start).toBeLessThan(500);
  });

  it('FR-025 skips amounts beyond .NET decimal precision (28 fraction / 29 integer digits)', () => {
    const start = Date.now();
    const huge = '0.' + '1'.repeat(1_000_000);
    const mk = (d: string, header: string | number) =>
      invoice({
        lines: [line({ discountAmountOC: d })],
        totals: totals({ totalDiscountAmountOC: header }),
      });
    expect(v(mk(huge, 0))).toEqual([]);
    expect(v(mk('0', huge))).toEqual([]);
    expect(v(mk('1' + '0'.repeat(30), 0))).toEqual([]);
    expect(v(mk('0.' + '1'.repeat(29), 0))).toEqual([]);
    expect(v(mk('0.' + '0'.repeat(27) + '1', 0))).toHaveLength(0);
    expect(v(mk('7', 0)).map((f) => f.ruleId)).toEqual(['FR-025']);
    expect(Date.now() - start).toBeLessThan(500);
  });

  it('FR-025 counts integer digits of exponent-form numbers exactly', () => {
    const mk = (header: number) =>
      invoice({
        lines: [line({ discountAmountOC: 0 })],
        totals: totals({ totalDiscountAmountOC: header }),
      });
    // 1.2345e28 has 29 integer digits (in range); 1.2345e29 has 30 (beyond .NET decimal).
    expect(v(mk(1.2345e28)).map((f) => f.ruleId)).toEqual(['FR-025']);
    expect(v(mk(1.2345e29))).toEqual([]);
  });

  it('FR-025 trims surrounding whitespace on money strings', () => {
    const mk = (header: string, d: string) =>
      invoice({
        lines: [line({ discountAmountOC: d })],
        totals: totals({ totalDiscountAmountOC: header }),
      });
    expect(v(mk(' 7 ', '0')).map((f) => f.ruleId)).toEqual(['FR-025']);
    expect(v(mk('\t10\n', ' 10'))).toEqual([]);
  });

  it('FR-025 missing header totalDiscountAmountOC counts as 0', () => {
    const inv = invoice({
      lines: [line({ discountAmountOC: 5 })],
      totals: totals({ totalDiscountAmountOC: undefined as unknown as number }),
    });
    expect(v(inv)).toEqual([
      {
        fieldPath: 'totals.totalDiscountAmountOC',
        message:
          'Header TotalDiscountAmountOC 0 does not equal sum of goods-line DiscountAmountOC (5).',
        ruleId: 'FR-025',
      },
    ]);
  });

  it('FR-025 counts only goods lines; accepts money strings; skips unparsable strings', () => {
    const lines = [
      line({ discountAmountOC: '10.00' }),
      line({ inventoryItemType: 2, discountAmountOC: 999 }),
      line({ discountAmountOC: 'abc' }),
    ];
    expect(v(invoice({ lines, totals: totals({ totalDiscountAmountOC: '10' }) }))).toEqual([]);
  });

  it('FR-006 totals required', () => {
    expect(v(invoice({ totals: undefined as unknown as InvoiceTotalsDto }))).toEqual([
      { fieldPath: 'totals', message: 'Invoice totals are required.', ruleId: 'FR-006' },
    ]);
  });
});

describe('InvoiceValidator.validateBatch', () => {
  it('accepts 1..30 valid invoices', () => {
    expect(defaultInvoiceValidator.validateBatch([invoice()])).toEqual([]);
    expect(
      defaultInvoiceValidator.validateBatch(Array.from({ length: 30 }, () => invoice())),
    ).toEqual([]);
  });

  it('31 invoices -> $batch.count', () => {
    expect(
      defaultInvoiceValidator.validateBatch(Array.from({ length: 31 }, () => invoice())),
    ).toEqual([
      {
        fieldPath: '$batch.count',
        message: 'Batch size 31 exceeds the MISA limit of 30 invoices per request.',
        ruleId: 'FR-030',
      },
    ]);
  });

  it('empty batch -> $batch.count', () => {
    expect(defaultInvoiceValidator.validateBatch([])).toEqual([
      {
        fieldPath: '$batch.count',
        message: 'Batch must contain between 1 and 30 invoices.',
        ruleId: 'FR-030',
      },
    ]);
  });

  it('prefixes per-invoice failures with invoices[i].', () => {
    const r = defaultInvoiceValidator.validateBatch([
      invoice(),
      invoice(),
      invoice({ currency: '' }),
    ]);
    expect(r).toEqual([
      { fieldPath: 'invoices[2].currency', message: 'Currency is required.', ruleId: 'FR-006' },
    ]);
  });
});

describe('validatePagedLookup', () => {
  const ok = { start: 0, length: 100, fromDate: '2026-01-01', toDate: '2026-01-31' };
  it('accepts a valid request', () => {
    expect(validatePagedLookup(ok)).toEqual([]);
    for (const publishStatus of [0, 4, 6, 7]) {
      expect(validatePagedLookup({ ...ok, publishStatus })).toEqual([]);
    }
  });
  it('start < 0', () => {
    expect(validatePagedLookup({ ...ok, start: -1 })).toEqual([
      { fieldPath: 'start', message: 'Start must be >= 0.', ruleId: 'FR-047' },
    ]);
  });
  it('length out of [1,100]', () => {
    const msg = 'Length must be in [1, 100].';
    expect(validatePagedLookup({ ...ok, length: 101 })).toEqual([
      { fieldPath: 'length', message: msg, ruleId: 'FR-047' },
    ]);
    expect(validatePagedLookup({ ...ok, length: 0 })[0]?.fieldPath).toBe('length');
    expect(validatePagedLookup({ ...ok, length: 1 })).toEqual([]);
  });
  it('fromDate > toDate (strings and Dates)', () => {
    expect(validatePagedLookup({ ...ok, fromDate: '2026-02-01' })).toEqual([
      { fieldPath: 'fromDate', message: 'FromDate must be <= ToDate.', ruleId: 'FR-047' },
    ]);
    expect(
      validatePagedLookup({
        ...ok,
        fromDate: new Date(2026, 1, 1),
        toDate: new Date(2026, 0, 1),
      })[0]?.fieldPath,
    ).toBe('fromDate');
    expect(
      validatePagedLookup({ ...ok, fromDate: new Date(2026, 0, 1), toDate: '2026-01-01' }),
    ).toEqual([]);
  });
  it('publishStatus not in {0,4,6,7}', () => {
    expect(validatePagedLookup({ ...ok, publishStatus: 5 })).toEqual([
      {
        fieldPath: 'publishStatus',
        message: 'PublishStatus must be one of {0, 4, 6, 7} when set.',
        ruleId: 'FR-047',
      },
    ]);
  });
  it('surfaces only the first violation', () => {
    expect(validatePagedLookup({ ...ok, start: -1, length: 0 })).toHaveLength(1);
  });
});

describe('validateAmendment', () => {
  const originalRef: OriginalInvoiceReferenceDto = {
    orgRefID: 'o1',
    orgInvNo: '1',
    orgInvTemplateNo: '1',
    orgInvSeries: '1C26TAA',
    orgInvDate: '2026-01-01',
  };
  const req = { invoice: invoice(), originalRef, changeReason: 'typo' };
  it('accepts a valid request', () => {
    expect(validateAmendment(req)).toEqual([]);
  });
  it('one failure per missing originalRef field', () => {
    const r = validateAmendment({
      ...req,
      originalRef: {
        orgRefID: ' ',
        orgInvNo: '',
        orgInvTemplateNo: '',
        orgInvSeries: '',
        orgInvDate: '',
      },
    });
    expect(r).toEqual([
      { fieldPath: '$originalRef.orgRefID', message: 'Required.', ruleId: 'FR-058' },
      { fieldPath: '$originalRef.orgInvNo', message: 'Required.', ruleId: 'FR-059' },
      { fieldPath: '$originalRef.orgInvTemplateNo', message: 'Required.', ruleId: 'FR-059' },
      { fieldPath: '$originalRef.orgInvSeries', message: 'Required.', ruleId: 'FR-059' },
      { fieldPath: '$originalRef.orgInvDate', message: 'Required.', ruleId: 'FR-059' },
    ]);
  });
  it('invalid Date counts as missing orgInvDate', () => {
    const r = validateAmendment({
      ...req,
      originalRef: { ...originalRef, orgInvDate: new Date(NaN) },
    });
    expect(r.map((f) => f.fieldPath)).toEqual(['$originalRef.orgInvDate']);
  });
  it('missing originalRef, changeReason and invoice', () => {
    const r = validateAmendment({} as never);
    expect(r).toEqual([
      { fieldPath: '$originalRef', message: 'Required.', ruleId: 'FR-058' },
      { fieldPath: '$changeReason', message: 'Required.', ruleId: 'FR-060' },
      { fieldPath: '$invoice', message: 'Required.', ruleId: 'FR-061' },
    ]);
  });
});
