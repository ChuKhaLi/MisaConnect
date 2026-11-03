import type { TemplateDto, TemplateRefDto } from '../domain/dtos.js';
import { MeInvoiceException } from '../domain/errors.js';
import { mapErrorCode } from './error-mapper.js';

export type TemplateResolution =
  | { kind: 'Resolved'; template: TemplateDto }
  | { kind: 'AmbiguousTemplate'; candidates: readonly TemplateDto[] }
  | { kind: 'NoActiveTemplate' }
  | { kind: 'UnknownTemplate'; key: string };

const eq = (a: string, b: string): boolean => a.toLowerCase() === b.toLowerCase();

const text = (v: unknown): string => (typeof v === 'string' ? v : '');

/**
 * Port of TemplateResolver.ResolveAsync over an already-fetched active-template list. Same
 * signature as the `TemplateResolver` port; a missing or non-string ref field (untyped JS)
 * counts as blank.
 */
export function resolveTemplate(
  templates: readonly TemplateDto[],
  ref?: TemplateRefDto,
): TemplateResolution {
  if (ref === undefined || ref === null) {
    const [only] = templates;
    if (templates.length === 0) return { kind: 'NoActiveTemplate' };
    if (templates.length === 1 && only) return { kind: 'Resolved', template: only };
    return { kind: 'AmbiguousTemplate', candidates: templates };
  }

  const id = text(ref.ipTemplateID);
  const series = text(ref.invSeries);
  for (const t of templates) {
    if ((id === '' || eq(t.ipTemplateID, id)) && (series === '' || eq(t.invSeries, series))) {
      return { kind: 'Resolved', template: t };
    }
  }
  return { kind: 'UnknownTemplate', key: id !== '' ? id : series };
}

/** Infers "uses tax-authority code" from the 2nd character of the series: C = with code, K = without. */
export function inferWithCode(invSeries: string | undefined): boolean | undefined {
  if (invSeries === undefined || invSeries.length <= 1) return undefined;
  const c = invSeries.charAt(1).toUpperCase();
  if (c === 'C') return true;
  if (c === 'K') return false;
  return undefined;
}

/** Resolution failure → exception (messages per `PreviewInvoice.cs`, category via the error map). */
export function templateResolutionError(
  resolution: Exclude<TemplateResolution, { kind: 'Resolved' }>,
): MeInvoiceException {
  const message =
    resolution.kind === 'AmbiguousTemplate'
      ? 'Multiple active templates exist; caller must specify.'
      : resolution.kind === 'NoActiveTemplate'
        ? 'No active templates configured.'
        : `Template '${resolution.key}' is not active.`;
  return new MeInvoiceException(mapErrorCode(resolution.kind).category, {
    rawErrorCode: resolution.kind,
    message,
  });
}
