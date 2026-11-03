import { Module } from '@nestjs/common';
import { MisaModule } from './misa.module.js';
import { TemplatesController } from './templates.controller.js';
import { TemplatesService } from './templates.service.js';

@Module({
  imports: [MisaModule],
  controllers: [TemplatesController],
  providers: [TemplatesService],
})
// eslint-disable-next-line @typescript-eslint/no-extraneous-class -- Nest modules are decorator-only classes
export class AppModule {}
