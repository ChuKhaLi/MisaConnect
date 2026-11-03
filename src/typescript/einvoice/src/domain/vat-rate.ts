export type VatRate =
  { kind: 'KCT' | 'KKKNT' | '0' | '5' | '8' | '10' } | { kind: 'KHAC'; rate: number };

const NAMED = new Map<string, VatRate>([
  ['KCT', { kind: 'KCT' }],
  ['KKKNT', { kind: 'KKKNT' }],
  ['0%', { kind: '0' }],
  ['5%', { kind: '5' }],
  ['8%', { kind: '8' }],
  ['10%', { kind: '10' }],
]);

const KHAC_PREFIX = 'KHAC:';
const DECIMAL = /^[+-]?(\d+(\.\d*)?|\.\d+)$/;

/** Parses a MISA VAT rate name (port of `VatRate.FromName`); returns undefined when unknown. */
export function parseVatRate(name: string): VatRate | undefined {
  const trimmed = name.trim();
  if (trimmed === '') return undefined;
  const named = NAMED.get(trimmed.toUpperCase());
  if (named) return { ...named } as VatRate;

  if (name.toUpperCase().startsWith(KHAC_PREFIX)) {
    const tail = name.slice(KHAC_PREFIX.length).replace(/%+$/, '').trim();
    if (DECIMAL.test(tail)) {
      const rate = Number(tail);
      if (rate > 0) return { kind: 'KHAC', rate };
    }
  }
  return undefined;
}
