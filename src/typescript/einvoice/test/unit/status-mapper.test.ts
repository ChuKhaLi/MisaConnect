import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { mapInvoiceStatus } from '../../src/application/status-mapper.js';

// Truth table from tests/dotnet/.../Contracts/status-mapping.md (99 = "unknown").
const rows: Array<[number | undefined, number | undefined, string]> = [
  [1, 0, 'Draft'],
  [1, 4, 'Signed'],
  [1, 6, 'Issued'],
  [1, 7, 'Issued'],
  [1, 99, 'Issued'],
  [1, undefined, 'Issued'],
  [3, 0, 'Replaced'],
  [3, 4, 'Replaced'],
  [3, 6, 'Replaced'],
  [3, 7, 'Replaced'],
  [3, 99, 'Replaced'],
  [3, undefined, 'Replaced'],
  [4, 0, 'Adjusted'],
  [4, 4, 'Adjusted'],
  [4, 6, 'Adjusted'],
  [4, 7, 'Adjusted'],
  [4, 99, 'Adjusted'],
  [4, undefined, 'Adjusted'],
  [99, 0, 'Draft'],
  [99, 4, 'Signed'],
  [99, 6, 'Issued'],
  [99, 7, 'Issued'],
  [99, 99, 'Issued'],
  [undefined, 0, 'Draft'],
  [undefined, 4, 'Signed'],
  [undefined, 6, 'Issued'],
  [undefined, 7, 'Issued'],
  [undefined, undefined, 'Issued'],
];

describe('mapInvoiceStatus', () => {
  it.each(rows)('(%s, %s) -> %s', (e, p, expected) => {
    expect(mapInvoiceStatus(e, p)).toBe(expected);
  });

  it('never returns Cancelled', () => {
    for (const [e, p] of rows) expect(mapInvoiceStatus(e, p)).not.toBe('Cancelled');
  });
});

const contract = fileURLToPath(
  new URL(
    '../../../../../tests/dotnet/MisaConnect.EInvoice.UnitTests/Contracts/status-mapping.md',
    import.meta.url,
  ),
);

// Drift guard: the .NET contract table is the load-bearing copy of the mapping.
describe.skipIf(!existsSync(contract))(
  'drift guard against the .NET status-mapping contract',
  () => {
    const axis = (cell: string): number | undefined => {
      const t = cell.replace(/[`*()]/g, '').trim();
      if (t === 'null') return undefined;
      if (t === 'unknown') return 99;
      return Number(t);
    };
    const table = existsSync(contract)
      ? readFileSync(contract, 'utf8')
          .split(/\r?\n/)
          .map((l) => l.split('|').map((c) => c.trim()))
          .filter(
            (c) =>
              c.length >= 5 &&
              /^`?\d+`?$|unknown|null/.test(c[1] ?? '') &&
              !/^`?Raw/.test(c[1] ?? ''),
          )
          .map((c) => [axis(c[1] ?? ''), axis(c[2] ?? ''), (c[3] ?? '').replace(/`/g, '')] as const)
      : [];

    it('parses all 28 contract rows', () => {
      expect(table).toHaveLength(28);
    });

    it.each(table)('(%s, %s) -> %s', (e, p, expected) => {
      expect(mapInvoiceStatus(e, p)).toBe(expected);
    });
  },
);
