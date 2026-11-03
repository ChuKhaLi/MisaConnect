import { Module } from '@nestjs/common';
import { createMisaEInvoiceClient } from '@misaconnect/einvoice';
import type { IMisaEInvoiceClient } from '@misaconnect/einvoice';

export const MISA_EINVOICE = Symbol('MISA_EINVOICE');

const required = (name: string): string => {
  const value = process.env[name];
  if (!value) throw new Error(`Missing environment variable ${name}`);
  return value;
};

@Module({
  providers: [
    {
      provide: MISA_EINVOICE,
      useFactory: (): IMisaEInvoiceClient =>
        createMisaEInvoiceClient({
          environment: 'sandbox',
          baseUrl: process.env['MISA_BASEURL'] ?? 'https://testapi.meinvoice.vn/api/integration',
          taxCode: required('MISACONNECT_SANDBOX_TAXCODE'),
          userName: required('MISACONNECT_SANDBOX_USERNAME'),
          password: required('MISACONNECT_SANDBOX_PASSWORD'),
          appId: required('MISACONNECT_SANDBOX_APPID'),
        }),
    },
  ],
  exports: [MISA_EINVOICE],
})
// eslint-disable-next-line @typescript-eslint/no-extraneous-class -- Nest modules are decorator-only classes
export class MisaModule {}
