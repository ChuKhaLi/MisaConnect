import { readFileSync } from 'node:fs';
import { describe, it, expect } from 'vitest';
import type {
  InvoiceDto,
  InvoiceLineDto,
  InvoiceTotalsDto,
  TemplateDto,
} from '../../src/domain/dtos.js';
import { MeInvoiceException } from '../../src/domain/errors.js';
import { serializeRequest } from '../../src/infrastructure/json.js';
import { toWireAmendment, toWireInvoice } from '../../src/infrastructure/wire/invoice.js';
import { fromWireSnapshot } from '../../src/infrastructure/wire/lookup.js';
import { fromWireTemplate } from '../../src/infrastructure/wire/templates.js';

function golden(name: string): string {
  // Canonical re-serialization: ignores .NET's + style escapes but keeps key order and numbers.
  const raw = readFileSync(new URL(`../golden/${name}`, import.meta.url), 'utf8');
  return JSON.stringify(JSON.parse(raw));
}

const template: TemplateDto = {
  ipTemplateID: 'TPL-1',
  invSeries: '1C26TAA',
  templateName: 'Hoa don',
  invTemplateNo: '1',
  templateType: 1,
  usesTaxAuthorityCode: true,
  isActive: true,
  isMoreVATRate: false,
};

function line(
  n: number,
  description: string,
  unitName: string,
  quantity: number,
  unitPrice: number,
  amount: number,
  vatRateName: string,
  vat: number,
): InvoiceLineDto {
  return {
    inventoryItemType: 0,
    sortOrder: n,
    description,
    unitName,
    quantity,
    unitPrice,
    amountOC: amount,
    amount,
    amountWithoutVATOC: amount,
    amountWithoutVAT: amount,
    vatRateName,
    vatAmountOC: vat,
    vatAmount: vat,
    sortOrderView: n,
  };
}

const typicalTotals: InvoiceTotalsDto = {
  totalSaleAmountOC: 2000000,
  totalSaleAmount: 2000000,
  totalDiscountAmountOC: 0,
  totalDiscountAmount: 0,
  totalAmountWithoutVATOC: 2000000,
  totalAmountWithoutVAT: 2000000,
  totalVATAmountOC: 180000,
  totalVATAmount: 180000,
  totalAmountOC: 2180000,
  totalAmount: 2180000,
  totalAmountInWords: 'Hai triệu một trăm tám mươi nghìn đồng chẵn',
};

const TYPICAL_ID = '11111111-1111-1111-1111-111111111111';

const typical: InvoiceDto = {
  refId: TYPICAL_ID,
  invDate: '2026-05-11',
  createdDate: '2026-05-11T08:30:15.123+07:00',
  modifiedDate: '2026-05-11T00:00:00Z',
  currency: 'VND',
  exchangeRate: 1,
  paymentMethod: 'TM',
  buyerType: 0,
  buyer: { name: 'Công ty TNHH Mua hàng', taxCode: '0123456789' },
  lines: [
    line(1, 'Dịch vụ tư vấn', 'Lần', 1, 1000000, 1000000, '8%', 80000),
    line(2, 'Hàng hóa A', 'Cái', 2, 500000, 1000000, '10%', 100000),
  ],
  totals: typicalTotals,
  customFields: { 1: 'cf-one', 2: 'ignored' },
};

const individual: InvoiceDto = {
  invDate: '2026-05-11',
  createdDate: '2026-05-11T00:00:00Z',
  modifiedDate: '2026-05-11T00:00:00Z',
  currency: 'VND',
  exchangeRate: 1,
  paymentMethod: 'TM',
  buyerType: 1,
  buyer: { name: 'Khách hàng cá nhân' },
  lines: [line(1, 'Hàng hóa cá nhân', 'Cái', 1, 100000, 100000, '0%', 0)],
  totals: {
    totalSaleAmountOC: 100000,
    totalSaleAmount: 100000,
    totalDiscountAmountOC: 0,
    totalDiscountAmount: 0,
    totalAmountWithoutVATOC: 100000,
    totalAmountWithoutVAT: 100000,
    totalVATAmountOC: 0,
    totalVATAmount: 0,
    totalAmountOC: 100000,
    totalAmount: 100000,
    totalAmountInWords: 'Một trăm nghìn đồng chẵn',
  },
};

const multi: InvoiceDto = {
  invDate: '2026-05-11',
  createdDate: '2026-05-11T00:00:00Z',
  modifiedDate: '2026-05-11T00:00:00Z',
  currency: 'USD',
  exchangeRate: 25000,
  paymentMethod: 'TM',
  buyerType: 0,
  buyer: { name: 'Công ty TNHH Mua hàng', taxCode: '0123456789' },
  lines: [
    {
      inventoryItemType: 0,
      sortOrder: 1,
      description: 'Export service',
      unitName: 'Lần',
      quantity: 1,
      unitPrice: 100,
      amountOC: 100,
      amount: 2500000,
      amountWithoutVATOC: 100,
      amountWithoutVAT: 2500000,
      vatRateName: '10%',
      vatAmountOC: 10,
      vatAmount: 250000,
      sortOrderView: 1,
    },
  ],
  totals: {
    totalSaleAmountOC: 100,
    totalSaleAmount: 2500000,
    totalDiscountAmountOC: 0,
    totalDiscountAmount: 0,
    totalAmountWithoutVATOC: 100,
    totalAmountWithoutVAT: 2500000,
    totalVATAmountOC: 10,
    totalVATAmount: 250000,
    totalAmountOC: 110,
    totalAmount: 2750000,
    totalAmountInWords: 'One hundred ten US dollars only',
  },
};

const originalRef = {
  orgRefID: '44444444-4444-4444-4444-444444444444',
  orgInvNo: '0000012',
  orgInvTemplateNo: '1',
  orgInvSeries: '1C26TAA',
  orgInvDate: '2026-04-01',
};

describe('toWireInvoice (golden parity with .NET)', () => {
  it('matches the typical VAT invoice golden byte-for-byte', () => {
    const wire = toWireInvoice(typical, TYPICAL_ID, template);
    expect(serializeRequest(wire)).toBe(golden('sample-invoice.wire.json'));
  });

  it('matches the individual-buyer golden', () => {
    const wire = toWireInvoice(individual, '22222222-2222-2222-2222-222222222222', template);
    expect(serializeRequest(wire)).toBe(golden('sample-individual.wire.json'));
  });

  it('matches the multi-currency golden', () => {
    const wire = toWireInvoice(multi, '33333333-3333-3333-3333-333333333333', template);
    expect(serializeRequest(wire)).toBe(golden('sample-multicurrency.wire.json'));
  });

  it('writes KHAC rates with name only and omits the numeric rate', () => {
    const dto = { ...typical, lines: [line(1, 'x', 'y', 1, 1, 1, 'khac:3.5%', 0)] };
    const wire = JSON.parse(serializeRequest(toWireInvoice(dto, 'r', template)));
    expect(wire.InvoiceDetails[0].VATRateName).toBe('KHAC:3.5%');
    expect(wire.InvoiceDetails[0]).not.toHaveProperty('VATRate');
  });

  it('writes KCT and KKKNT numeric codes -1 and -3', () => {
    const dto = {
      ...typical,
      lines: [line(1, 'x', 'y', 1, 1, 1, 'kct', 0), line(2, 'x', 'y', 1, 1, 1, 'KKKNT', 0)],
    };
    const wire = JSON.parse(serializeRequest(toWireInvoice(dto, 'r', template)));
    expect(wire.InvoiceDetails.map((d: { VATRate: number }) => d.VATRate)).toEqual([-1, -3]);
  });

  it('keeps big money strings verbatim', () => {
    const dto = {
      ...typical,
      totals: { ...typicalTotals, totalAmountOC: '12345678901234567890.123456' },
    };
    expect(serializeRequest(toWireInvoice(dto, 'r', template))).toContain(
      '"TotalAmountOC":12345678901234567890.123456',
    );
  });

  it('reports the TS field path for invalid money', () => {
    const dto = { ...typical, totals: { ...typicalTotals, totalAmountOC: 'abc' } };
    try {
      toWireInvoice(dto, 'r', template);
      expect.unreachable();
    } catch (e) {
      expect(e).toBeInstanceOf(MeInvoiceException);
      expect((e as MeInvoiceException).field).toBe('totals.totalAmountOC');
    }
  });

  it('rejects an unknown VAT rate name with the line field path', () => {
    const dto = { ...typical, lines: [line(1, 'x', 'y', 1, 1, 1, 'weird', 0)] };
    try {
      toWireInvoice(dto, 'r', template);
      expect.unreachable();
    } catch (e) {
      expect((e as MeInvoiceException).field).toBe('lines[0].vatRateName');
    }
  });
});

describe('toWireAmendment', () => {
  it('adjustment sets EInvoiceStatus 4 and all five Org* fields (golden)', () => {
    const wire = toWireAmendment(
      { invoice: typical, originalRef, changeReason: 'Điều chỉnh giá' },
      'adjustment',
      TYPICAL_ID,
      template,
    );
    expect(wire.EInvoiceStatus).toBe('4');
    for (const k of ['OrgRefID', 'OrgInvNo', 'OrgInvTemplateNo', 'OrgInvSeries', 'OrgInvDate']) {
      expect(wire).toHaveProperty(k);
    }
    expect(serializeRequest(wire)).toBe(golden('sample-adjustment.wire.json'));
  });

  it('replacement sets EInvoiceStatus 3 (golden)', () => {
    const wire = toWireAmendment(
      { invoice: typical, originalRef, changeReason: 'Thay thế' },
      'replacement',
      TYPICAL_ID,
      template,
    );
    expect(wire.EInvoiceStatus).toBe('3');
    expect(serializeRequest(wire)).toBe(golden('sample-replacement.wire.json'));
  });

  it('formats a Date orgInvDate as yyyy-MM-dd', () => {
    const wire = toWireAmendment(
      {
        invoice: typical,
        originalRef: { ...originalRef, orgInvDate: new Date(2026, 3, 1) },
        changeReason: 'r',
      },
      'adjustment',
      'r',
      template,
    );
    expect(wire.OrgInvDate).toBe('2026-04-01');
  });
});

describe('fromWireSnapshot', () => {
  it('maps EInvoiceStatus 4 to Adjusted and coerces string statuses', () => {
    const s = fromWireSnapshot({
      RefID: 'abc',
      InvoiceTemplateID: 'T',
      InvSeries: '1C26TAA',
      InvDate: '2026-05-11',
      InvNo: '0000001',
      AccountObjectTaxCode: '0123456789',
      TotalAmount: 2180000,
      EInvoiceStatus: '4',
      PublishStatus: '1',
      OrgRefID: 'org',
      CreatedDate: '2026-05-11T08:30:15',
      ModifiedDate: '2026-05-11T01:00:00Z',
    });
    expect(s).toMatchObject({
      refId: 'abc',
      invoiceTemplateID: 'T',
      invDate: '2026-05-11T00:00:00',
      totalAmount: 2180000,
      rawEInvoiceStatus: 4,
      rawPublishStatus: 1,
      status: 'Adjusted',
      orgRefID: 'org',
      createdDate: '2026-05-11T08:30:15+07:00',
      modifiedDate: '2026-05-11T01:00:00+00:00',
    });
  });

  it('maps publish status 0 to Draft and tolerates empty or invalid fields', () => {
    const s = fromWireSnapshot({
      RefID: '',
      InvDate: '11/05/2026',
      EInvoiceStatus: 'x',
      PublishStatus: '0',
      OrgRefID: '',
      CreatedDate: 'garbage',
    });
    expect(s.refId).toBe('');
    expect(s.status).toBe('Draft');
    expect(s).not.toHaveProperty('invDate');
    expect(s).not.toHaveProperty('rawEInvoiceStatus');
    expect(s).not.toHaveProperty('orgRefID');
    expect(s).not.toHaveProperty('createdDate');
  });

  it('honours explicit offsets and keeps fractional seconds', () => {
    const s = fromWireSnapshot({
      RefID: 'a',
      CreatedDate: '2026-05-11T08:30:15.1234560+09:00',
    });
    expect(s.createdDate).toBe('2026-05-11T08:30:15.123456+09:00');
  });
});

describe('fromWireTemplate', () => {
  const wire = {
    IPTemplateID: 'T1',
    InvSeries: '1K26TAA',
    TemplateName: 'N',
    InvTemplateNo: '1',
    TemplateType: 2,
    Inactive: false,
    IsMoreVATRate: true,
  };

  it('derives UsesTaxAuthorityCode from the series second character', () => {
    expect(fromWireTemplate(wire, true).usesTaxAuthorityCode).toBe(false);
    expect(fromWireTemplate({ ...wire, InvSeries: '1c26TAA' }, false).usesTaxAuthorityCode).toBe(
      true,
    );
  });

  it('falls back to the default when the series char is neither C nor K', () => {
    expect(fromWireTemplate({ ...wire, InvSeries: '1X' }, true).usesTaxAuthorityCode).toBe(true);
    expect(fromWireTemplate({ ...wire, InvSeries: '1' }).usesTaxAuthorityCode).toBe(false);
  });

  it('inverts Inactive into isActive and copies the rest', () => {
    expect(fromWireTemplate({ ...wire, Inactive: true }).isActive).toBe(false);
    expect(fromWireTemplate(wire)).toMatchObject({
      ipTemplateID: 'T1',
      templateName: 'N',
      invTemplateNo: '1',
      templateType: 2,
      isMoreVATRate: true,
    });
  });
});

describe('date-time error messages', () => {
  const failOf = (createdDate: string): MeInvoiceException => {
    try {
      toWireInvoice({ ...typical, createdDate }, TYPICAL_ID, template);
    } catch (e) {
      return e as MeInvoiceException;
    }
    throw new Error('expected a failure');
  };

  it('a well-formed date-time without offset says the offset is missing', () => {
    const err = failOf('2026-05-11T08:30:15');
    expect(err).toMatchObject({ rawErrorCode: 'InvalidDate', field: 'createdDate' });
    expect(err.message).toBe(
      'Date-time has no UTC offset; add Z or +hh:mm / -hh:mm (e.g. 2026-05-11T08:30:15+07:00).',
    );
  });

  it('a malformed date-time says it is malformed', () => {
    for (const bad of ['yesterday', '2026-02-30T00:00:00Z', '2026-05-11T25:00:00+07:00']) {
      const err = failOf(bad);
      expect(err).toMatchObject({ rawErrorCode: 'InvalidDate', field: 'createdDate' });
      expect(err.message).toBe(
        'Date-time is not a valid ISO-8601 date-time (yyyy-MM-ddTHH:mm[:ss[.fff]] with Z or ±hh:mm).',
      );
    }
  });
});
