import { defaultInvoiceValidator } from '../application/invoice-validator.js';
import type {
  Clock,
  InvoiceValidator,
  Logger,
  RefIdGenerator,
  TemplateResolver,
  TokenCache,
} from '../application/ports.js';
import { resolveTemplate } from '../application/template-resolver.js';
import { MeInvoiceException, type ValidationFailure } from '../domain/errors.js';
import { createInMemoryTokenCache, systemClock, uuidRefIdGenerator } from './defaults.js';
import { noopLogger } from './logging.js';

export const SandboxHost = 'testapi.meinvoice.vn';
export const ProductionHost = 'api.meinvoice.vn';

export interface MisaEInvoiceOptions {
  environment?: 'sandbox' | 'production';
  baseUrl: string;
  taxCode: string;
  userName: string;
  password: string;
  appId: string;
  delete?: { includeRawErrorMessage?: boolean };
  tokenCache?: TokenCache;
  refIdGenerator?: RefIdGenerator;
  clock?: Clock;
  validator?: InvoiceValidator;
  templateResolver?: TemplateResolver;
  logger?: Logger;
  fetch?: typeof fetch;
  correlationId?: () => string;
}

export interface ResolvedOptions {
  environment: 'sandbox' | 'production';
  baseUrl: string;
  taxCode: string;
  userName: string;
  password: string;
  appId: string;
  delete: { includeRawErrorMessage: boolean };
  tokenCache: TokenCache;
  refIdGenerator: RefIdGenerator;
  clock: Clock;
  validator: InvoiceValidator;
  templateResolver: TemplateResolver;
  logger: Logger;
  fetch: typeof fetch;
  correlationId: () => string;
}

const isBlank = (v: unknown): boolean => typeof v !== 'string' || v.trim() === '';

export function validateOptions(
  o: MisaEInvoiceOptions,
  internal?: { allowAnyBaseUrl?: boolean },
): ResolvedOptions {
  const failures: ValidationFailure[] = [];
  const fail = (fieldPath: string, message: string): void => {
    failures.push({ fieldPath, message });
  };
  const environment = o.environment ?? 'sandbox';

  if (isBlank(o.baseUrl)) {
    fail('baseUrl', 'baseUrl is required.');
  } else {
    let url: URL | undefined;
    try {
      url = new URL(o.baseUrl);
    } catch {
      url = undefined;
    }
    if (internal?.allowAnyBaseUrl) {
      if (url === undefined) fail('baseUrl', `baseUrl '${o.baseUrl}' must be an absolute URI.`);
    } else if (url === undefined || url.protocol !== 'https:') {
      fail('baseUrl', `baseUrl '${o.baseUrl}' must be an absolute https URI.`);
    } else {
      const expectedHost =
        environment === 'sandbox'
          ? SandboxHost
          : environment === 'production'
            ? ProductionHost
            : undefined;
      if (expectedHost === undefined) {
        fail(
          'environment',
          `environment '${String(environment)}' is not a known value (sandbox|production).`,
        );
      } else if (url.hostname.toLowerCase() !== expectedHost) {
        fail(
          'baseUrl',
          `baseUrl host '${url.hostname}' does not match the chosen environment ` +
            `(${environment} expects host '${expectedHost}').`,
        );
      }
    }
  }

  if (isBlank(o.taxCode)) fail('taxCode', 'taxCode is required.');
  if (isBlank(o.userName)) fail('userName', 'userName is required.');
  if (isBlank(o.password)) fail('password', 'password is required.');
  if (isBlank(o.appId)) fail('appId', 'appId is required.');

  if (failures.length > 0) {
    throw new MeInvoiceException('Configuration', {
      rawErrorCode: 'InvalidOptions',
      message: `Invalid MisaEInvoiceOptions: ${failures.map((f) => f.message).join(' ')}`,
      failures,
    });
  }

  return {
    environment,
    baseUrl: o.baseUrl.replace(/\/+$/, '') + '/',
    taxCode: o.taxCode,
    userName: o.userName,
    password: o.password,
    appId: o.appId,
    delete: { includeRawErrorMessage: o.delete?.includeRawErrorMessage ?? false },
    tokenCache: o.tokenCache ?? createInMemoryTokenCache(),
    refIdGenerator: o.refIdGenerator ?? uuidRefIdGenerator,
    clock: o.clock ?? systemClock,
    validator: o.validator ?? defaultInvoiceValidator,
    templateResolver: o.templateResolver ?? resolveTemplate,
    logger: o.logger ?? noopLogger,
    fetch: o.fetch ?? globalThis.fetch,
    correlationId: o.correlationId ?? (() => crypto.randomUUID()),
  };
}
