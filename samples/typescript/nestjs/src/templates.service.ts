import { Inject, Injectable } from '@nestjs/common';
import type { IMisaEInvoiceClient, TemplateDto } from '@misaconnect/einvoice';
import { MISA_EINVOICE } from './misa.module.js';

@Injectable()
export class TemplatesService {
  // `@Inject` is needed because `emitDecoratorMetadata` is off (see tsconfig.json).
  constructor(@Inject(MISA_EINVOICE) private readonly misa: IMisaEInvoiceClient) {}

  list(signal?: AbortSignal): Promise<TemplateDto[]> {
    return this.misa.listTemplates(true, signal ? { signal } : undefined);
  }
}
