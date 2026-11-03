import express from 'express';
import { createMisaEInvoiceClient, MeInvoiceException } from '@misaconnect/einvoice';
import type { InvoiceDto } from '@misaconnect/einvoice';

const env = (name: string): string => {
  const value = process.env[name];
  if (!value) throw new Error(`Missing environment variable ${name}`);
  return value;
};

const client = createMisaEInvoiceClient({
  environment: 'sandbox',
  baseUrl: process.env['MISA_BASEURL'] ?? 'https://testapi.meinvoice.vn/api/integration',
  taxCode: env('MISACONNECT_SANDBOX_TAXCODE'),
  userName: env('MISACONNECT_SANDBOX_USERNAME'),
  password: env('MISACONNECT_SANDBOX_PASSWORD'),
  appId: env('MISACONNECT_SANDBOX_APPID'),
});

const app = express();
app.use(express.json());

app.get('/templates', async (req, res) => {
  const ac = new AbortController();
  req.on('close', () => ac.abort());
  try {
    res.json(await client.listTemplates(true, { signal: ac.signal }));
  } catch (e) {
    if (e instanceof MeInvoiceException) {
      res.status(502).json({ category: e.category, code: e.rawErrorCode });
    } else {
      res.status(500).end();
    }
  }
});

app.post('/drafts', async (req, res) => {
  try {
    res.json(await client.saveDraft([req.body as InvoiceDto]));
  } catch (e) {
    if (e instanceof MeInvoiceException) {
      res
        .status(e.category === 'Validation' ? 400 : 502)
        .json({ category: e.category, failures: e.failures });
    } else {
      res.status(500).end();
    }
  }
});

app.listen(3000, () => console.log('listening on http://localhost:3000'));
