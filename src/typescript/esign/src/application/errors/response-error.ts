/** One per-property rejection MISA attaches to a 400 body. */
export interface ValidationFailure {
  propertyName?: string;
  errorMessage?: string;
}

/** MISA's error envelope, parsed leniently from either the flat or the nested `{status:{…}}` shape. */
export interface ResponseError {
  error?: boolean;
  errorCode?: string;
  devMsg?: string;
  userMsg?: string;
  validationFailures?: ValidationFailure[];
}

const isRecord = (v: unknown): v is Record<string, unknown> =>
  typeof v === 'object' && v !== null && !Array.isArray(v);

const str = (v: unknown): string | undefined => (typeof v === 'string' ? v : undefined);

/** `errorCode` arrives as a string, number or boolean depending on the endpoint. */
function looseString(v: unknown): string | undefined {
  if (typeof v === 'string') return v;
  if (typeof v === 'number' && Number.isFinite(v)) return String(v);
  if (typeof v === 'boolean') return v ? 'true' : 'false';
  return undefined;
}

/** MISA documents `error` as a string but sends booleans (and nulls); accept all three. */
function looseBool(v: unknown): boolean | undefined {
  if (typeof v === 'boolean') return v;
  if (typeof v === 'string') {
    const s = v.trim().toLowerCase();
    if (s === 'true') return true;
    if (s === 'false') return false;
  }
  return undefined;
}

function parseFailures(v: unknown): ValidationFailure[] | undefined {
  if (!Array.isArray(v)) return undefined;
  const out: ValidationFailure[] = [];
  for (const item of v) {
    if (!isRecord(item)) continue;
    const propertyName = str(item.propertyName) ?? str(item.property);
    const errorMessage = str(item.errorMessage) ?? str(item.failureReason);
    const f: ValidationFailure = {};
    if (propertyName !== undefined) f.propertyName = propertyName;
    if (errorMessage !== undefined) f.errorMessage = errorMessage;
    out.push(f);
  }
  return out;
}

/** Parses a decoded JSON body into a {@link ResponseError}; `undefined` when it is not an object. */
export function parseResponseError(json: unknown): ResponseError | undefined {
  if (!isRecord(json)) return undefined;
  const src = isRecord(json.status) ? json.status : json;
  const result: ResponseError = {};
  const error = looseBool(src.error);
  if (error !== undefined) result.error = error;
  const errorCode = looseString(src.errorCode);
  if (errorCode !== undefined) result.errorCode = errorCode;
  const devMsg = str(src.devMsg);
  if (devMsg !== undefined) result.devMsg = devMsg;
  const userMsg = str(src.userMsg);
  if (userMsg !== undefined) result.userMsg = userMsg;
  const failures = parseFailures(json.validationFailures) ?? parseFailures(src.validationFailures);
  if (failures !== undefined) result.validationFailures = failures;
  return result;
}

/** Renders validation failures as `validationFailures: [Prop] reason; …`, or `undefined` when empty. */
export function renderValidationFailures(
  failures: readonly ValidationFailure[] | undefined,
): string | undefined {
  if (!failures || failures.length === 0) return undefined;
  const parts: string[] = [];
  for (const f of failures) {
    const prop = f.propertyName?.trim() ? f.propertyName : undefined;
    const reason = f.errorMessage?.trim() ? f.errorMessage : undefined;
    if (prop === undefined && reason === undefined) continue;
    if (prop === undefined) parts.push((reason as string).trim());
    else if (reason === undefined) parts.push(`[${prop}]`);
    else parts.push(`[${prop}] ${reason}`);
  }
  return parts.length === 0 ? undefined : `validationFailures: ${parts.join('; ')}`;
}

/** Builds the human-readable detail shared by both mappers. */
export function buildErrorDetail(
  endpoint: string,
  envelope: ResponseError | undefined,
  includeRaw: boolean,
  withValidationFailures: boolean,
): string {
  const summary = `MISA returned errorCode=${envelope?.errorCode ?? '<none>'} on ${endpoint}.`;
  if (!includeRaw || envelope === undefined) return summary;
  const parts = [summary];
  if (envelope.userMsg?.trim()) parts.push(`userMsg=${envelope.userMsg}`);
  if (envelope.devMsg?.trim()) parts.push(`devMsg=${envelope.devMsg}`);
  if (withValidationFailures) {
    const vf = renderValidationFailures(envelope.validationFailures);
    if (vf) parts.push(vf);
  }
  return parts.join(' | ');
}
