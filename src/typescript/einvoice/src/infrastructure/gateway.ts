import type {
  Envelope,
  InsertOutcome,
  MeInvoiceGateway,
  MeInvoiceTransport,
} from '../application/ports.js';
import { mapErrorCode } from '../application/error-mapper.js';
import type { InvoiceSnapshotDto, TemplateDto } from '../domain/dtos.js';
import { MeInvoiceException } from '../domain/errors.js';
import {
  extractErrorCode,
  extractErrorMessage,
  parseInsertErrors,
  throwFromEnvelope,
} from './envelope.js';
import type { ResolvedOptions } from './options.js';
import { formatDateOnly } from './json.js';
import { wireTaxCode } from './transport.js';
import { fromWireSnapshot, type WireLookupRow } from './wire/lookup.js';
import { toWireAmendment, toWireInvoice, type WireInvoice } from './wire/invoice.js';
import { fromWireTemplate, type WireTemplate } from './wire/templates.js';

/** Port `MeInvoiceHttpRoutes`. */
export const Routes = Object.freeze({
  Templates: 'webapp/templates',
  Preview: 'webapp/preview',
  ViewRefId: 'webapp/viewrefid',
  Insert: 'webapp/insert',
  DeleteDraft: 'webapp/delete',
  LookupByRefId: 'webapp/getlist',
  LookupStandardPaging: 'webapp/paging',
  LookupCalculatingPaging: 'webapp/paging/calculating',
});

const flag = (b: boolean): string => (b ? 'true' : 'false');

/** Base64 envelope `data` → bytes; anything else is a malformed MISA response. */
function decodeBase64(data: unknown, refId?: string): Uint8Array {
  if (typeof data === 'string') {
    try {
      const bin = atob(data);
      const bytes = new Uint8Array(bin.length);
      for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
      return bytes;
    } catch {
      // fall through
    }
  }
  throw new MeInvoiceException('MisaUnknown', {
    rawErrorCode: 'InvalidResponse',
    message: 'MISA returned data that is not base64 content.',
    ...(refId !== undefined ? { refId } : {}),
  });
}

/**
 * Port `DeserializeSnapshots`: `data` is a raw array, or (some sandbox builds) a string the
 * envelope reader could not pre-parse; unparseable / non-array data is empty, and rows that
 * are not JSON objects are skipped (.NET returns empty rather than throwing).
 */
function toSnapshots(data: unknown): InvoiceSnapshotDto[] {
  let rows: unknown = data;
  if (typeof rows === 'string' && rows !== '') {
    try {
      rows = JSON.parse(rows) as unknown;
    } catch {
      return [];
    }
  }
  if (!Array.isArray(rows)) return [];
  return rows
    .filter((r): r is WireLookupRow => typeof r === 'object' && r !== null && !Array.isArray(r))
    .map(fromWireSnapshot);
}

/** Port `ThrowFromLookupEnvelope`. */
function throwFromLookupEnvelope(envelope: Envelope<unknown>): never {
  const msg = extractErrorMessage(envelope);
  const code = mapErrorCode(extractErrorCode(envelope), msg);
  const message = code.detail ?? msg;
  throw new MeInvoiceException(code.category, {
    rawErrorCode: code.rawCode,
    ...(message !== undefined ? { message } : {}),
  });
}

/**
 * Port `PostInsertAsync` envelope interpretation: one outcome per requested RefID, in order.
 * A RefID MISA does not echo yields `MisaUnknown` / `NoEchoedResult`; an envelope failure
 * with neither echoes nor per-entry errors throws.
 */
function interpretInsertEnvelope(
  envelope: Envelope<unknown>,
  refIds: readonly string[],
): InsertOutcome[] {
  const successes = new Set<string>();
  if (Array.isArray(envelope.data)) {
    for (const entry of envelope.data as unknown[]) {
      const id =
        typeof entry === 'object' && entry !== null
          ? (entry as Record<string, unknown>)['RefID']
          : undefined;
      if (typeof id === 'string' && id !== '') successes.add(id);
    }
  }
  const errorEntries = parseInsertErrors(envelope.error);
  if (!envelope.success && successes.size === 0 && errorEntries.length === 0) {
    throwFromEnvelope(envelope);
  }

  const results = new Map<string, InsertOutcome>();
  for (const id of successes) results.set(id, { refId: id, outcome: 'Success' });
  for (const e of errorEntries) {
    if (e.refID === undefined || e.refID === '') continue;
    const code = mapErrorCode(e.errorCode, e.errorMessage);
    results.set(e.refID, {
      refId: e.refID,
      outcome: 'Error',
      errorCategory: code.category,
      rawErrorCode: code.rawCode,
      ...(code.detail !== undefined ? { message: code.detail } : {}),
    });
  }

  return refIds.map(
    (refId): InsertOutcome =>
      results.get(refId) ?? {
        refId,
        outcome: 'Error',
        errorCategory: 'MisaUnknown',
        rawErrorCode: 'NoEchoedResult',
      },
  );
}

/** Port of `MeInvoiceClient` (templates, preview, viewrefid). */
export function createMeInvoiceGateway(
  transport: MeInvoiceTransport,
  resolved: ResolvedOptions,
): MeInvoiceGateway {
  return {
    async listTemplates(withCode, opts) {
      const body = {
        TaxCode: wireTaxCode(resolved.taxCode, resolved.appId),
        UserName: resolved.userName,
        Password: resolved.password,
      };
      const envelope = await transport.request<unknown>(
        'POST',
        Routes.Templates,
        { invoiceWithCode: flag(withCode), isTicket: 'false' },
        body,
        opts,
      );
      if (!envelope.success) throwFromEnvelope(envelope);
      const data = envelope.data;
      if (data === undefined) return [];
      if (!Array.isArray(data)) {
        throw new MeInvoiceException('MisaUnknown', {
          rawErrorCode: 'InvalidResponse',
          message: 'MISA returned a templates payload that is not an array.',
        });
      }
      // Like lookups, skip malformed rows (null, or no string IPTemplateID / InvSeries).
      return (data as unknown[])
        .filter(
          (w): w is WireTemplate =>
            typeof w === 'object' &&
            w !== null &&
            typeof (w as Record<string, unknown>)['IPTemplateID'] === 'string' &&
            typeof (w as Record<string, unknown>)['InvSeries'] === 'string',
        )
        .map((w): TemplateDto => fromWireTemplate(w, withCode));
    },

    async preview(invoice, refId, template, withCode, opts) {
      const envelope = await transport.request<unknown>(
        'POST',
        Routes.Preview,
        { invoiceWithCode: flag(withCode) },
        toWireInvoice(invoice, refId, template),
        opts,
      );
      if (!envelope.success || envelope.data === undefined) throwFromEnvelope(envelope, refId);
      return decodeBase64(envelope.data, refId);
    },

    async insertInvoices(items, withCode, opts) {
      const outcomes: InsertOutcome[] = new Array<InsertOutcome>(items.length);
      const bodies: WireInvoice[] = [];
      const sent: { index: number; refId: string }[] = [];
      items.forEach((item, index) => {
        try {
          bodies.push(toWireInvoice(item.invoice, item.refId, item.template));
          sent.push({ index, refId: item.refId });
        } catch (err) {
          if (!(err instanceof MeInvoiceException) || err.category !== 'Validation') throw err;
          outcomes[index] = {
            refId: item.refId,
            outcome: 'Error',
            errorCategory: 'Validation',
            rawErrorCode: 'ValidationFailed',
            failures: [{ fieldPath: err.field ?? '$', message: err.message }],
          };
        }
      });
      if (bodies.length === 0) return outcomes;

      const envelope = await transport.request<unknown>(
        'POST',
        Routes.Insert,
        { invoiceWithCode: flag(withCode) },
        bodies,
        opts,
      );
      const echoed = interpretInsertEnvelope(
        envelope,
        sent.map((s) => s.refId),
      );
      sent.forEach((s, k) => {
        outcomes[s.index] = echoed[k] as InsertOutcome;
      });
      return outcomes;
    },

    async insertAmendment(kind, request, refId, template, withCode, opts) {
      const envelope = await transport.request<unknown>(
        'POST',
        Routes.Insert,
        { invoiceWithCode: flag(withCode) },
        [toWireAmendment(request, kind, refId, template)],
        opts,
      );
      const [outcome] = interpretInsertEnvelope(envelope, [refId]);
      return outcome as InsertOutcome;
    },

    async deleteDraft(refId, withCode, opts) {
      const envelope = await transport.request<unknown>(
        'DELETE',
        Routes.DeleteDraft,
        { invoiceWithCode: flag(withCode), refid: refId },
        undefined,
        opts,
      );
      if (envelope.success) return { success: true };
      const errorCode = extractErrorCode(envelope);
      return {
        success: false,
        ...(errorCode !== undefined ? { errorCode } : {}),
        ...(envelope.errorMessage !== undefined ? { errorMessage: envelope.errorMessage } : {}),
      };
    },

    async lookupByRefIdsChunk(refIds, withCode, opts) {
      const envelope = await transport.request<unknown>(
        'POST',
        Routes.LookupByRefId,
        { invoiceWithCode: flag(withCode) },
        [...refIds],
        opts,
      );
      if (!envelope.success) {
        // Deviation: a failure envelope without an error code still counts as an error
        // (.NET treats an empty code as success and would report every RefID NotFound).
        const code = extractErrorCode(envelope);
        const message = extractErrorMessage(envelope);
        return {
          snapshots: [],
          errorCode: code !== undefined && code !== '' ? code : 'UnknownError',
          ...(message !== undefined ? { errorMessage: message } : {}),
        };
      }
      return { snapshots: toSnapshots(envelope.data) };
    },

    async lookupPaged(kind, request, withCode, opts) {
      const body = {
        Start: request.start,
        Length: request.length,
        // Port LookupDtoMapper.ToDomain: blank Sort defaults to "InvDate".
        Sort:
          typeof request.sort !== 'string' || request.sort.trim() === '' ? 'InvDate' : request.sort,
        FromDate: formatDateOnly(request.fromDate, 'fromDate'),
        ToDate: formatDateOnly(request.toDate, 'toDate'),
        PublishStatus:
          request.publishStatus !== undefined && request.publishStatus !== null
            ? String(request.publishStatus)
            : undefined,
      };
      const envelope = await transport.request<unknown>(
        'POST',
        kind === 'standard' ? Routes.LookupStandardPaging : Routes.LookupCalculatingPaging,
        { invoiceWithCode: flag(withCode) },
        body,
        opts,
      );
      if (!envelope.success) throwFromLookupEnvelope(envelope);
      return toSnapshots(envelope.data);
    },

    async viewDraftPdf(refId, withCode, opts) {
      const envelope = await transport.request<unknown>(
        'GET',
        Routes.ViewRefId,
        { invoiceWithCode: flag(withCode), refid: refId },
        undefined,
        opts,
      );
      if (!envelope.success || envelope.data === undefined) {
        // MISA answers success:true with empty data for an unknown RefID; treat both as not-found.
        const msg = extractErrorMessage(envelope);
        const code = mapErrorCode(extractErrorCode(envelope) ?? 'RefIdNotFound', msg);
        const message = code.detail ?? msg;
        throw new MeInvoiceException(code.category, {
          rawErrorCode: code.rawCode,
          ...(message !== undefined ? { message } : {}),
          refId,
        });
      }
      return decodeBase64(envelope.data, refId);
    },
  };
}
