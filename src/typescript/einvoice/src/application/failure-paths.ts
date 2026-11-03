import type { ValidationFailure } from '../domain/errors.js';

/** Prefixes `invoices[i].`; the invoice root (`$` or empty) becomes `invoices[i]`. */
export const indexFailures = (
  index: number,
  failures: readonly ValidationFailure[],
): ValidationFailure[] =>
  failures.map((f) => ({
    ...f,
    fieldPath:
      f.fieldPath === '$' || f.fieldPath === ''
        ? `invoices[${index}]`
        : `invoices[${index}].${f.fieldPath}`,
  }));
