import type { InvoiceDto, TemplateDto, TemplateRefDto } from '../domain/dtos.js';
import type { ValidationFailure } from '../domain/errors.js';
import type { TemplateResolution } from './template-resolver.js';

export interface InvoiceValidator {
  validate(invoice: InvoiceDto): ValidationFailure[];
  validateBatch(invoices: InvoiceDto[]): ValidationFailure[];
}

export type TemplateResolver = (
  templates: readonly TemplateDto[],
  ref?: TemplateRefDto,
) => TemplateResolution;

export interface Clock {
  now(): Date;
}

export type RefIdGenerator = () => string;

export interface AccessToken {
  value: string;
  expiresAt: Date;
}

export interface TokenCache {
  get(key: string): Promise<AccessToken | undefined>;
  set(key: string, token: AccessToken): Promise<void>;
  delete(key: string): Promise<void>;
}

export interface Logger {
  debug(message: string, fields?: Record<string, unknown>): void;
  info(message: string, fields?: Record<string, unknown>): void;
  warn(message: string, fields?: Record<string, unknown>): void;
  error(message: string, fields?: Record<string, unknown>): void;
}
