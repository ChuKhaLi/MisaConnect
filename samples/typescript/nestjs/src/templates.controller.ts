import { Controller, Get, Inject, Res } from '@nestjs/common';
import type { ServerResponse } from 'node:http';
import type { TemplateDto } from '@misaconnect/einvoice';
import { TemplatesService } from './templates.service.js';

@Controller('templates')
export class TemplatesController {
  // `@Inject(...)` is required: this sample does not enable `emitDecoratorMetadata`, so Nest
  // cannot infer the constructor parameter type from the TypeScript annotation.
  constructor(@Inject(TemplatesService) private readonly templates: TemplatesService) {}

  @Get()
  list(@Res({ passthrough: true }) res: ServerResponse): Promise<TemplateDto[]> {
    // Abort the MISA call if the client disconnects before the response is written.
    // 'close' also fires after a normal response; abort only if we had not finished (as the
    // express sample does). `passthrough` keeps Nest in charge of writing the return value.
    const ac = new AbortController();
    res.on('close', () => {
      if (!res.writableFinished) ac.abort();
    });
    return this.templates.list(ac.signal);
  }
}
