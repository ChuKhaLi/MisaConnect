import { Controller, Get, Inject, Req } from '@nestjs/common';
import type { IncomingMessage } from 'node:http';
import type { TemplateDto } from '@misaconnect/einvoice';
import { TemplatesService } from './templates.service.js';

@Controller('templates')
export class TemplatesController {
  // `@Inject(...)` is required: this sample does not enable `emitDecoratorMetadata`, so Nest
  // cannot infer the constructor parameter type from the TypeScript annotation.
  constructor(@Inject(TemplatesService) private readonly templates: TemplatesService) {}

  @Get()
  list(@Req() req: IncomingMessage): Promise<TemplateDto[]> {
    // Abort the MISA call if the client disconnects before the response is written.
    const ac = new AbortController();
    req.on('close', () => {
      if (!req.complete || req.destroyed) ac.abort();
    });
    return this.templates.list(ac.signal);
  }
}
