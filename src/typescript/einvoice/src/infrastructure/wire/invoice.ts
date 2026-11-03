import type {
  AdjustmentRequestDto,
  InvoiceDto,
  InvoiceLineDto,
  Money,
  ReplacementRequestDto,
  TemplateDto,
} from '../../domain/dtos.js';
import { MeInvoiceException } from '../../domain/errors.js';
import { parseVatRate, type VatRate } from '../../domain/vat-rate.js';
import { formatDateOnly, money, type MoneyLiteral } from '../json.js';
import { parseDateTime } from './datetime.js';

/** Outgoing line (`InvoiceDetailDto`); property order is the order the .NET SDK emits. */
export interface WireInvoiceDetail {
  InventoryItemType: number;
  SortOrder?: number | undefined;
  SortOrderView?: number | undefined;
  Description?: string | undefined;
  UnitName?: string | undefined;
  Quantity?: MoneyLiteral | undefined;
  UnitPrice?: MoneyLiteral | undefined;
  AmountOC?: MoneyLiteral | undefined;
  Amount?: MoneyLiteral | undefined;
  AmountWithoutVATOC?: MoneyLiteral | undefined;
  AmountWithoutVAT?: MoneyLiteral | undefined;
  VATAmountOC?: MoneyLiteral | undefined;
  VATAmount?: MoneyLiteral | undefined;
  VATRateName?: string | undefined;
  VATRate?: number | undefined;
  DiscountRate?: MoneyLiteral | undefined;
  DiscountAmountOC?: MoneyLiteral | undefined;
  DiscountAmount?: MoneyLiteral | undefined;
}

/** Outgoing invoice body for `/insert` and `/preview` (`InvoiceDataDto`). */
export interface WireInvoice {
  RefID: string;
  InvoiceTemplateID?: string | undefined;
  InvSeries?: string | undefined;
  InvDate?: string | undefined;
  CreatedDate?: string | undefined;
  ModifiedDate?: string | undefined;
  CurrencyCode?: string | undefined;
  ExchangeRate?: MoneyLiteral | undefined;
  PaymentMethod?: string | undefined;
  AccountObjectName?: string | undefined;
  AccountObjectTaxCode?: string | undefined;
  AccountObjectAddress?: string | undefined;
  ContactName?: string | undefined;
  ReceiverEmail?: string | undefined;
  ReceiverName?: string | undefined;
  ReceiverMobile?: string | undefined;
  AccountObjectBankAccount?: string | undefined;
  AccountObjectBankName?: string | undefined;
  TotalSaleAmountOC?: MoneyLiteral | undefined;
  TotalSaleAmount?: MoneyLiteral | undefined;
  TotalDiscountAmountOC?: MoneyLiteral | undefined;
  TotalDiscountAmount?: MoneyLiteral | undefined;
  TotalAmountWithoutVATOC?: MoneyLiteral | undefined;
  TotalAmountWithoutVAT?: MoneyLiteral | undefined;
  TotalVATAmountOC?: MoneyLiteral | undefined;
  TotalVATAmount?: MoneyLiteral | undefined;
  TotalAmountOC?: MoneyLiteral | undefined;
  TotalAmount?: MoneyLiteral | undefined;
  TotalAmountInWords?: string | undefined;
  EInvoiceStatus?: string | undefined;
  CustomField1?: string | undefined;
  OrgRefID?: string | undefined;
  OrgInvNo?: string | undefined;
  OrgInvTemplateNo?: string | undefined;
  OrgInvSeries?: string | undefined;
  OrgInvDate?: string | undefined;
  ChangeReason?: string | undefined;
  InvoiceDetails: WireInvoiceDetail[];
}

/** Wraps an optional money value, omitting it when absent. */
function optMoney(v: Money | undefined, field: string): MoneyLiteral | undefined {
  return v === undefined || v === null ? undefined : money(v, field);
}

/** Formats a decimal like .NET `ToString("0.################")`. */
function formatPercent(rate: number): string {
  const s = String(rate);
  if (!/e/i.test(s)) return s;
  return rate.toFixed(16).replace(/0+$/, '').replace(/\.$/, '');
}

/** Port of `VatRateMapper.ToWire`: name plus numeric code (KHAC has no numeric code). */
function wireVat(rate: VatRate): { name: string; numeric?: number } {
  switch (rate.kind) {
    case 'KCT':
      return { name: 'KCT', numeric: -1 };
    case 'KKKNT':
      return { name: 'KKKNT', numeric: -3 };
    case 'KHAC':
      return { name: `KHAC:${formatPercent(rate.rate)}%` };
    default:
      return { name: `${rate.kind}%`, numeric: Number(rate.kind) };
  }
}

/** .NET `DateTimeOffset.ToString("O")`: 7 fraction digits, numeric offset (never `Z`). */
function formatOffsetDateTime(text: string, field: string): string {
  const p = typeof text === 'string' ? parseDateTime(text) : undefined;
  if (!p || p.offset === undefined) {
    throw new MeInvoiceException('Validation', {
      rawErrorCode: 'InvalidDate',
      message: !p
        ? 'Date-time is not a valid ISO-8601 date-time (yyyy-MM-ddTHH:mm[:ss[.fff]] with Z or ±hh:mm).'
        : 'Date-time has no UTC offset; add Z or +hh:mm / -hh:mm (e.g. 2026-05-11T08:30:15+07:00).',
      field,
    });
  }
  return `${p.date}T${p.time}.${p.fraction.padEnd(7, '0').slice(0, 7)}${p.offset}`;
}

/** A plain (non-money) number: must be finite when present, so it fails this invoice only. */
function optNumber(v: number | undefined, field: string): number | undefined {
  if (typeof v === 'number' && !Number.isFinite(v)) {
    throw new MeInvoiceException('Validation', {
      rawErrorCode: 'InvalidNumber',
      message: 'Number must be finite.',
      field,
    });
  }
  return v;
}

function wireLine(l: InvoiceLineDto, i: number): WireInvoiceDetail {
  const f = (name: string) => `lines[${i}].${name}`;
  let vat: { name: string; numeric?: number } | undefined;
  if (typeof l.vatRateName === 'string' && l.vatRateName !== '') {
    const parsed = parseVatRate(l.vatRateName);
    if (!parsed) {
      throw new MeInvoiceException('Validation', {
        rawErrorCode: 'InvalidVatRate',
        message: `Unknown VAT rate name '${l.vatRateName}'.`,
        field: f('vatRateName'),
      });
    }
    vat = wireVat(parsed);
  }
  return {
    InventoryItemType: optNumber(l.inventoryItemType, f('inventoryItemType')) as number,
    SortOrder: optNumber(l.sortOrder, f('sortOrder')),
    SortOrderView: optNumber(l.sortOrderView, f('sortOrderView')),
    Description: l.description,
    UnitName: l.unitName,
    Quantity: optMoney(l.quantity, f('quantity')),
    UnitPrice: optMoney(l.unitPrice, f('unitPrice')),
    AmountOC: optMoney(l.amountOC, f('amountOC')),
    Amount: optMoney(l.amount, f('amount')),
    AmountWithoutVATOC: optMoney(l.amountWithoutVATOC, f('amountWithoutVATOC')),
    AmountWithoutVAT: optMoney(l.amountWithoutVAT, f('amountWithoutVAT')),
    VATAmountOC: optMoney(l.vatAmountOC, f('vatAmountOC')),
    VATAmount: optMoney(l.vatAmount, f('vatAmount')),
    VATRateName: vat?.name,
    VATRate: vat?.numeric,
    DiscountRate: optMoney(l.discountRate, f('discountRate')),
    DiscountAmountOC: optMoney(l.discountAmountOC, f('discountAmountOC')),
    DiscountAmount: optMoney(l.discountAmount, f('discountAmount')),
  };
}

interface Amendment {
  orgRefID: string;
  orgInvNo: string;
  orgInvTemplateNo: string;
  orgInvSeries: string;
  orgInvDate: string;
  changeReason: string;
}

function build(
  dto: InvoiceDto,
  refId: string,
  template: TemplateDto,
  status: string,
  amendment?: Amendment,
): WireInvoice {
  const t = dto.totals;
  const b = dto.buyer;
  return {
    RefID: refId,
    InvoiceTemplateID: template.ipTemplateID,
    InvSeries: template.invSeries,
    InvDate: formatDateOnly(dto.invDate, 'invDate'),
    CreatedDate: formatOffsetDateTime(dto.createdDate, 'createdDate'),
    ModifiedDate: formatOffsetDateTime(dto.modifiedDate, 'modifiedDate'),
    CurrencyCode: dto.currency,
    ExchangeRate: optMoney(dto.exchangeRate, 'exchangeRate'),
    PaymentMethod: dto.paymentMethod,
    AccountObjectName: b.name,
    AccountObjectTaxCode: b.taxCode,
    AccountObjectAddress: b.address,
    ContactName: b.contactName,
    ReceiverEmail: b.email,
    ReceiverName: b.receiverName,
    ReceiverMobile: b.mobile,
    AccountObjectBankAccount: b.bankAccount,
    AccountObjectBankName: b.bankName,
    TotalSaleAmountOC: optMoney(t.totalSaleAmountOC, 'totals.totalSaleAmountOC'),
    TotalSaleAmount: optMoney(t.totalSaleAmount, 'totals.totalSaleAmount'),
    TotalDiscountAmountOC: optMoney(t.totalDiscountAmountOC, 'totals.totalDiscountAmountOC'),
    TotalDiscountAmount: optMoney(t.totalDiscountAmount, 'totals.totalDiscountAmount'),
    TotalAmountWithoutVATOC: optMoney(t.totalAmountWithoutVATOC, 'totals.totalAmountWithoutVATOC'),
    TotalAmountWithoutVAT: optMoney(t.totalAmountWithoutVAT, 'totals.totalAmountWithoutVAT'),
    TotalVATAmountOC: optMoney(t.totalVATAmountOC, 'totals.totalVATAmountOC'),
    TotalVATAmount: optMoney(t.totalVATAmount, 'totals.totalVATAmount'),
    TotalAmountOC: optMoney(t.totalAmountOC, 'totals.totalAmountOC'),
    TotalAmount: optMoney(t.totalAmount, 'totals.totalAmount'),
    TotalAmountInWords: t.totalAmountInWords,
    EInvoiceStatus: status,
    CustomField1: dto.customFields?.[1],
    OrgRefID: amendment?.orgRefID,
    OrgInvNo: amendment?.orgInvNo,
    OrgInvTemplateNo: amendment?.orgInvTemplateNo,
    OrgInvSeries: amendment?.orgInvSeries,
    OrgInvDate: amendment?.orgInvDate,
    ChangeReason: amendment?.changeReason,
    InvoiceDetails: dto.lines.map(wireLine),
  };
}

/** Port of `InvoiceDtoMapper.ToWire` for a plain draft (`EInvoiceStatus "1"`). */
export function toWireInvoice(dto: InvoiceDto, refId: string, template: TemplateDto): WireInvoice {
  return build(dto, refId, template, '1');
}

/**
 * Port of the replacement/adjustment bodies (`EInvoiceStatus "3"` / `"4"` + `Org*` + `ChangeReason`).
 * `kind` alone decides the status: `ReplacementRequestDto` and `AdjustmentRequestDto` are
 * structurally identical (no discriminator field, as in .NET where the use case, not the DTO,
 * picks the status), so the DTO cannot be checked against `kind`.
 */
export function toWireAmendment(
  dto: ReplacementRequestDto | AdjustmentRequestDto,
  kind: 'replacement' | 'adjustment',
  refId: string,
  template: TemplateDto,
): WireInvoice {
  const o = dto.originalRef;
  return build(dto.invoice, refId, template, kind === 'replacement' ? '3' : '4', {
    orgRefID: o.orgRefID,
    orgInvNo: o.orgInvNo,
    orgInvTemplateNo: o.orgInvTemplateNo,
    orgInvSeries: o.orgInvSeries,
    orgInvDate: formatDateOnly(o.orgInvDate, 'originalRef.orgInvDate'),
    changeReason: dto.changeReason,
  });
}
