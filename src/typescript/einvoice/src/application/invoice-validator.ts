import type {
  AdjustmentRequestDto,
  DateOnlyInput,
  InvoiceDto,
  InvoiceLineDto,
  Money,
  PagedLookupRequestDto,
  ReplacementRequestDto,
} from '../domain/dtos.js';
import type { ValidationFailure } from '../domain/errors.js';
import { parseVatRate } from '../domain/vat-rate.js';
import { indexFailures } from './failure-paths.js';
import type { InvoiceValidator } from './ports.js';

export const MaxLinesPerInvoice = 200;
export const MaxInvoicesPerBatch = 30;
export const RefIdMaxLength = 64;
/** +/-0.5 OC tolerance (R-9). */
export const OcTolerance = 0.5;

const ORGANIZATION = 1;
const GOODS = 0;
const ALLOWED_PUBLISH_STATUSES: readonly number[] = [0, 4, 6, 7];

const fail = (fieldPath: string, message: string, ruleId: string): ValidationFailure => ({
  fieldPath,
  message,
  ruleId,
});

const isBlank = (s: string | undefined | null): boolean =>
  s === undefined || s === null || s.trim() === '';

/** Numeric value of a money field; undefined when absent or unparsable (the serializer rejects those later). */
function toNumber(m: Money | undefined | null): number | undefined {
  if (m === undefined || m === null) return undefined;
  const n = typeof m === 'number' ? m : m.trim() === '' ? Number.NaN : Number(m);
  return Number.isFinite(n) ? n : undefined;
}

interface Decimal {
  int: bigint;
  scale: number;
}
const ZERO: Decimal = { int: 0n, scale: 0 };
const PLAIN_DECIMAL_RE = /^(-?)(\d+)(?:\.(\d+))?$/;
const NUMBER_RE = /^(-?)(\d+)(?:\.(\d+))?(?:e([+-]?\d+))?$/;
/** .NET decimal limits: at most 28 fractional and 29 integer digits. */
const MAX_SCALE = 28;
const MAX_INT_DIGITS = 29;

/**
 * Exact decimal of a money value, or undefined when unparsable or beyond .NET decimal precision.
 * Strings must be plain decimals (^-?\d+(\.\d+)?$) once trimmed; exponent form is accepted only
 * for numbers.
 */
function parseDecimal(m: Money | undefined | null): Decimal | undefined {
  if (m === undefined || m === null) return undefined;
  let match: RegExpExecArray | null;
  if (typeof m === 'number') {
    if (!Number.isFinite(m)) return undefined;
    match = NUMBER_RE.exec(String(m));
  } else if (typeof m === 'string') {
    // Surrounding whitespace is tolerated here, as `Number()` (toNumber) does.
    match = PLAIN_DECIMAL_RE.exec(m.trim());
  } else {
    return undefined;
  }
  if (!match) return undefined;
  const sign = match[1];
  const whole = match[2] ?? '';
  const frac = match[3] ?? '';
  const exp = Number(match[4] ?? '0');
  // Bound sizes before any BigInt work.
  if (Math.abs(exp) > 400) return undefined;
  const scale = frac.length - exp;
  if (scale > MAX_SCALE || integerDigits(whole, frac, exp) > MAX_INT_DIGITS) return undefined;
  let digits = BigInt(whole + frac);
  if (scale < 0) digits *= 10n ** BigInt(-scale);
  return { int: sign === '-' ? -digits : digits, scale: Math.max(scale, 0) };
}

/** Exact count of integer digits of `whole.frac × 10^exp` (no BigInt; inputs may be huge). */
function integerDigits(whole: string, frac: string, exp: number): number {
  const w = whole.replace(/^0+/, '');
  if (w !== '') return Math.max(0, w.length + exp);
  const rest = frac.replace(/^0+/, '');
  if (rest === '') return 0;
  return Math.max(0, exp - (frac.length - rest.length));
}

const TOLERANCE: Decimal = (() => {
  const d = parseDecimal(OcTolerance);
  if (d === undefined) throw new Error(`OcTolerance ${OcTolerance} is not a valid decimal.`);
  return d;
})();

function rescale(d: Decimal, scale: number): bigint {
  return d.int * 10n ** BigInt(scale - d.scale);
}

function formatDecimal(d: Decimal): string {
  const neg = d.int < 0n;
  const abs = (neg ? -d.int : d.int).toString().padStart(d.scale + 1, '0');
  const whole = abs.slice(0, abs.length - d.scale);
  const frac = abs.slice(abs.length - d.scale).replace(/0+$/, '');
  return (neg ? '-' : '') + whole + (frac ? '.' + frac : '');
}

/** True when the string holds a UTF-16 high surrogate (supplementary-plane code point), like char.IsHighSurrogate. */
function hasSupplementaryPlane(value: string): boolean {
  for (let i = 0; i < value.length; i++) {
    const c = value.charCodeAt(i);
    if (c >= 0xd800 && c <= 0xdbff) return true;
  }
  return false;
}

function checkEmojiFree(
  fieldPath: string,
  value: string | undefined,
  failures: ValidationFailure[],
): void {
  if (value && hasSupplementaryPlane(value)) {
    failures.push(
      fail(
        fieldPath,
        'Field contains a supplementary-plane code point (likely emoji); MISA rejects these with InvalidXMLContainEmoji.',
        'FR-028',
      ),
    );
  }
}

/** `KHAC:<decimal>%` whose rate is <= 0 (.NET `VatRate.Other` rejects it: must be positive). */
function isNonPositiveKhac(name: unknown): boolean {
  if (typeof name !== 'string' || !name.toUpperCase().startsWith('KHAC:')) return false;
  const tail = name.slice('KHAC:'.length).replace(/%+$/, '').trim();
  return /^[+-]?(\d+(\.\d*)?|\.\d+)$/.test(tail) && Number(tail) <= 0;
}

/** yyyy-MM-dd for comparison, or undefined when missing/invalid. */
function toDateKey(d: DateOnlyInput | undefined | null): string | undefined {
  if (d === undefined || d === null) return undefined;
  if (d instanceof Date) {
    if (Number.isNaN(d.getTime())) return undefined;
    const mm = String(d.getMonth() + 1).padStart(2, '0');
    const dd = String(d.getDate()).padStart(2, '0');
    return `${String(d.getFullYear()).padStart(4, '0')}-${mm}-${dd}`;
  }
  return d.trim() === '' ? undefined : d.trim();
}

function validateInvoice(invoice: InvoiceDto): ValidationFailure[] {
  const failures: ValidationFailure[] = [];
  if (invoice === undefined || invoice === null) {
    failures.push(fail('$', 'Invoice is null.', 'FR-006'));
    return failures;
  }

  if (!invoice.refId) {
    failures.push(fail('refId', 'RefId must be supplied or service-generated.', 'FR-021'));
  } else if (invoice.refId.length > RefIdMaxLength) {
    failures.push(
      fail(
        'refId',
        `RefId length ${invoice.refId.length} exceeds the maximum of ${RefIdMaxLength}.`,
        'FR-021',
      ),
    );
  }

  if (isBlank(invoice.currency)) {
    failures.push(fail('currency', 'Currency is required.', 'FR-006'));
  } else if (
    invoice.currency.toUpperCase() !== 'VND' &&
    (toNumber(invoice.exchangeRate) ?? 0) <= 0
  ) {
    failures.push(
      fail('exchangeRate', 'ExchangeRate must be supplied and > 0 for non-VND invoices.', 'FR-023'),
    );
  }

  if (invoice.buyerType === ORGANIZATION && isBlank(invoice.buyer?.taxCode)) {
    failures.push(
      fail('buyer.taxCode', 'Buyer tax code is required when BuyerType = Organization.', 'FR-024'),
    );
  }

  if (isBlank(invoice.paymentMethod)) {
    failures.push(fail('paymentMethod', 'PaymentMethod is required.', 'FR-027'));
  }

  if (typeof invoice.buyer !== 'object' || invoice.buyer === null) {
    failures.push(fail('buyer', 'Buyer is required.', 'FR-006'));
  }
  checkEmojiFree('buyer.name', invoice.buyer?.name, failures);
  checkEmojiFree('buyer.contactName', invoice.buyer?.contactName, failures);
  checkEmojiFree('buyer.address', invoice.buyer?.address, failures);

  if (!Array.isArray(invoice.lines) || invoice.lines.length === 0) {
    failures.push(fail('lines', 'At least one invoice line is required.', 'FR-006'));
  } else {
    if (invoice.lines.length > MaxLinesPerInvoice) {
      failures.push(
        fail(
          'lines',
          `Line count ${invoice.lines.length} exceeds the MISA limit of ${MaxLinesPerInvoice} per invoice.`,
          'FR-030',
        ),
      );
    }

    invoice.lines.forEach((line: InvoiceLineDto | null, i) => {
      if (typeof line !== 'object' || line === null) {
        failures.push(fail(`lines[${i}]`, 'Invoice line is null.', 'FR-006'));
        return;
      }
      checkEmojiFree(`lines[${i}].description`, line.description, failures);
      checkEmojiFree(`lines[${i}].unitName`, line.unitName, failures);
      if (
        line.vatRateName !== undefined &&
        line.vatRateName !== null &&
        parseVatRate(line.vatRateName) === undefined
      ) {
        failures.push(
          fail(
            `lines[${i}].vatRateName`,
            `VATRateName '${line.vatRateName}' is not in the accepted set${
              isNonPositiveKhac(line.vatRateName)
                ? ': the KHAC rate must be a positive percentage (> 0)'
                : ''
            }.`,
            'FR-029',
          ),
        );
      }
    });

    // FR-025: discount reconciliation (exact decimal arithmetic, like .NET decimal).
    if (invoice.totals) {
      const headerRaw = invoice.totals.totalDiscountAmountOC;
      const header = headerRaw === undefined || headerRaw === null ? ZERO : parseDecimal(headerRaw);
      if (header !== undefined) {
        const goods: Decimal[] = [];
        for (const line of invoice.lines) {
          if (typeof line !== 'object' || line === null || line.inventoryItemType !== GOODS) {
            continue;
          }
          const d = parseDecimal(line.discountAmountOC);
          if (d !== undefined) goods.push(d);
        }
        const scale = Math.max(header.scale, TOLERANCE.scale, ...goods.map((g) => g.scale));
        const sum = goods.reduce((acc, g) => acc + rescale(g, scale), 0n);
        const diff = rescale(header, scale) - sum;
        if ((diff < 0n ? -diff : diff) > rescale(TOLERANCE, scale)) {
          failures.push(
            fail(
              'totals.totalDiscountAmountOC',
              `Header TotalDiscountAmountOC ${formatDecimal(header)} does not equal sum of goods-line DiscountAmountOC (${formatDecimal({ int: sum, scale })}).`,
              'FR-025',
            ),
          );
        }
      }
    }
  }

  if (!invoice.totals) {
    failures.push(fail('totals', 'Invoice totals are required.', 'FR-006'));
  }

  return failures;
}

function validateBatch(invoices: InvoiceDto[]): ValidationFailure[] {
  const all: ValidationFailure[] = [];
  if (invoices.length < 1) {
    all.push(
      fail(
        '$batch.count',
        `Batch must contain between 1 and ${MaxInvoicesPerBatch} invoices.`,
        'FR-030',
      ),
    );
  } else if (invoices.length > MaxInvoicesPerBatch) {
    all.push(
      fail(
        '$batch.count',
        `Batch size ${invoices.length} exceeds the MISA limit of ${MaxInvoicesPerBatch} invoices per request.`,
        'FR-030',
      ),
    );
  }
  invoices.forEach((inv, i) => {
    all.push(...indexFailures(i, validateInvoice(inv)));
  });
  return all;
}

export const defaultInvoiceValidator: InvoiceValidator = {
  validate: validateInvoice,
  validateBatch,
};

/** Port of PagedLookupRequest.Validate: only the first violation is surfaced. */
export function validatePagedLookup(req: PagedLookupRequestDto): ValidationFailure[] {
  if (req.start < 0) return [fail('start', 'Start must be >= 0.', 'FR-047')];
  if (req.length < 1 || req.length > 100) {
    return [fail('length', 'Length must be in [1, 100].', 'FR-047')];
  }
  const from = toDateKey(req.fromDate);
  const to = toDateKey(req.toDate);
  if (from !== undefined && to !== undefined && from > to) {
    return [fail('fromDate', 'FromDate must be <= ToDate.', 'FR-047')];
  }
  if (
    req.publishStatus !== undefined &&
    req.publishStatus !== null &&
    !ALLOWED_PUBLISH_STATUSES.includes(req.publishStatus)
  ) {
    return [fail('publishStatus', 'PublishStatus must be one of {0, 4, 6, 7} when set.', 'FR-047')];
  }
  return [];
}

/** Port of ReplacementRequest.Validate / OriginalInvoiceReference.Validate (adjustment shares the shape). */
export function validateAmendment(
  req: ReplacementRequestDto | AdjustmentRequestDto,
): ValidationFailure[] {
  if (typeof req !== 'object' || req === null) {
    return [fail('$request', 'Required.', 'FR-058')];
  }
  const failures: ValidationFailure[] = [];
  const ref = req.originalRef;
  if (ref === undefined || ref === null) {
    failures.push(fail('$originalRef', 'Required.', 'FR-058'));
  } else {
    if (isBlank(ref.orgRefID)) failures.push(fail('$originalRef.orgRefID', 'Required.', 'FR-058'));
    if (isBlank(ref.orgInvNo)) failures.push(fail('$originalRef.orgInvNo', 'Required.', 'FR-059'));
    if (isBlank(ref.orgInvTemplateNo)) {
      failures.push(fail('$originalRef.orgInvTemplateNo', 'Required.', 'FR-059'));
    }
    if (isBlank(ref.orgInvSeries)) {
      failures.push(fail('$originalRef.orgInvSeries', 'Required.', 'FR-059'));
    }
    if (toDateKey(ref.orgInvDate) === undefined) {
      failures.push(fail('$originalRef.orgInvDate', 'Required.', 'FR-059'));
    }
  }
  if (isBlank(req.changeReason)) failures.push(fail('$changeReason', 'Required.', 'FR-060'));
  if (req.invoice === undefined || req.invoice === null) {
    failures.push(fail('$invoice', 'Required.', 'FR-061'));
  }
  return failures;
}
