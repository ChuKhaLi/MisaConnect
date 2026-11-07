// Same object as docs/einvoice/typescript/operations.md#a-complete-invoice; `npm run typecheck` proves it
// type-checks and test/integration/client-hardening.test.ts saves it against the fake server.
import type { InvoiceDto } from '@misaconnect/einvoice';

export const invoice: InvoiceDto = {
  refId: 'order-2026-0001', // your idempotency key; omit to get a generated UUID
  template: { ipTemplateID: 'ipt-active-1', invSeries: 'C26TAA' }, // omit if you have one active template
  invDate: '2026-11-03',
  createdDate: '2026-11-03T09:00:00+07:00',
  modifiedDate: '2026-11-03T09:00:00+07:00',
  currency: 'VND',
  exchangeRate: 1,
  paymentMethod: 'TM/CK',
  buyerType: 1, // 1 = organisation (buyer.taxCode required)
  buyer: {
    name: 'Công ty TNHH Ví Dụ',
    taxCode: '0312345678',
    address: '1 Lê Lợi, Quận 1, TP. Hồ Chí Minh',
    email: 'ketoan@example.com',
  },
  lines: [
    {
      inventoryItemType: 0, // 0 = goods
      sortOrder: 1,
      description: 'Bàn phím cơ',
      unitName: 'cái',
      quantity: 2,
      unitPrice: 500000,
      amountOC: 1000000,
      amount: 1000000,
      amountWithoutVATOC: 1000000,
      amountWithoutVAT: 1000000,
      vatRateName: '8%',
      vatAmountOC: 80000,
      vatAmount: 80000,
    },
  ],
  totals: {
    totalSaleAmountOC: 1000000,
    totalSaleAmount: 1000000,
    totalDiscountAmountOC: 0,
    totalDiscountAmount: 0,
    totalAmountWithoutVATOC: 1000000,
    totalAmountWithoutVAT: 1000000,
    totalVATAmountOC: 80000,
    totalVATAmount: 80000,
    totalAmountOC: 1080000,
    totalAmount: 1080000,
    totalAmountInWords: 'Một triệu không trăm tám mươi nghìn đồng',
  },
};
