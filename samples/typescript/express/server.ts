import { createHash, timingSafeEqual } from 'node:crypto';
import { isIP } from 'node:net';
import express from 'express';
import { createMisaEInvoiceClient, isMeInvoiceException } from '@misaconnect/einvoice';
import type { InvoiceDto } from '@misaconnect/einvoice';
import { createMisaESignClient, isESignException } from '@misaconnect/esign';

const env = (name: string): string => {
  const value = process.env[name];
  if (!value) throw new Error(`Missing environment variable ${name}`);
  return value;
};

const client = createMisaEInvoiceClient({
  environment: 'sandbox',
  baseUrl: 'https://testapi.meinvoice.vn/api/integration',
  taxCode: env('MISACONNECT_SANDBOX_TAXCODE'),
  userName: env('MISACONNECT_SANDBOX_USERNAME'),
  password: env('MISACONNECT_SANDBOX_PASSWORD'),
  appId: env('MISACONNECT_SANDBOX_APPID'),
});

// eSign is created lazily so the einvoice endpoints work without eSign credentials.
let esignClient: ReturnType<typeof createMisaESignClient> | undefined;
const esign = (): ReturnType<typeof createMisaESignClient> =>
  (esignClient ??= createMisaESignClient({
    environment: 'sandbox',
    baseUrl: env('MISACONNECT_ESIGN_SANDBOX_BASE_URL'),
    credentials: {
      clientId: env('MISACONNECT_ESIGN_SANDBOX_CLIENT_ID'),
      clientKey: env('MISACONNECT_ESIGN_SANDBOX_CLIENT_KEY'),
      userName: env('MISACONNECT_ESIGN_SANDBOX_USERNAME'),
      password: env('MISACONNECT_ESIGN_SANDBOX_PASSWORD'),
    },
  }));

const app = express();
app.use(express.json());

app.get('/templates', async (req, res) => {
  const ac = new AbortController();
  // 'close' also fires after a normal response; abort only if the client left before we finished.
  res.on('close', () => {
    if (!res.writableFinished) ac.abort();
  });
  try {
    res.json(await client.listTemplates(true, { signal: ac.signal }));
  } catch (e) {
    if (isMeInvoiceException(e)) {
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
    if (isMeInvoiceException(e)) {
      res
        .status(e.category === 'Validation' || e.category === 'BatchOrSize' ? 400 : 502)
        .json({ category: e.category, failures: e.failures });
    } else {
      res.status(500).end();
    }
  }
});

// POST /esign/pdf  (Content-Type: application/pdf, raw body) -> signed PDF bytes.
app.post(
  '/esign/pdf',
  express.raw({ type: 'application/pdf', limit: '20mb' }),
  async (req, res) => {
    if (!Buffer.isBuffer(req.body) || req.body.length === 0) {
      res
        .status(400)
        .json({ error: 'Send the PDF as the raw body with Content-Type: application/pdf.' });
      return;
    }
    const ac = new AbortController();
    res.on('close', () => {
      if (!res.writableFinished) ac.abort();
    });
    try {
      const signed = await esign().signPdf(
        {
          pdf: new Uint8Array(req.body),
          documentName: 'document.pdf',
          signerName: 'Sample Signer',
          location: 'Ho Chi Minh City',
          reason: 'Sample signature',
          contact: 'signer@example.com',
          // 1x1 transparent PNG; replace with your signature logo.
          logoImageBase64:
            'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
          dataToBeDisplayed: 'Signed with MisaConnect',
          page: 1,
          positionX: 50,
          positionY: 50,
          width: 200,
          height: 80,
        },
        { signal: ac.signal },
      );
      res.type('application/pdf').send(Buffer.from(signed.signedPdf));
    } catch (e) {
      if (isESignException(e)) {
        res
          .status(e.category === 'Validation' ? 400 : 502)
          .json({ category: e.category, code: e.rawCode, correlationId: e.correlationId });
      } else {
        res.status(500).end();
      }
    }
  },
);

// Webhook route protection. The SDK does not authenticate webhook requests: the host must.
const digest = (v: string): Buffer => createHash('sha256').update(v, 'utf8').digest();
// Hashing both sides gives equal-length buffers, so timingSafeEqual needs no length check.
const secretMatches = (given: string, expected: string): boolean =>
  timingSafeEqual(digest(given), digest(expected));

function ipv4ToInt(ip: string): number | undefined {
  const parts = ip.split('.').map(Number);
  if (parts.length !== 4 || parts.some((p) => !Number.isInteger(p) || p < 0 || p > 255)) {
    return undefined;
  }
  return parts.reduce((acc, p) => acc * 256 + p, 0);
}

/** True when `ip` (IPv4, optionally ::ffff:-mapped) is inside any "a.b.c.d" or "a.b.c.d/len" entry. */
function ipAllowed(ip: string | undefined, allowlist: string[]): boolean {
  if (allowlist.length === 0) return true;
  const plain = ip?.startsWith('::ffff:') ? ip.slice(7) : ip;
  if (plain === undefined || isIP(plain) !== 4) return false;
  const addr = ipv4ToInt(plain);
  if (addr === undefined) return false;
  return allowlist.some((entry) => {
    const [base, len] = entry.split('/');
    const baseInt = ipv4ToInt(base ?? '');
    const bits = len === undefined ? 32 : Number(len);
    if (baseInt === undefined || !Number.isInteger(bits) || bits < 0 || bits > 32) return false;
    const mask = bits === 0 ? 0 : (~0 << (32 - bits)) >>> 0;
    return (addr & mask) === (baseInt & mask);
  });
}

// POST /esign/webhook/:secret  -- the URL you register with MISA.
// ESIGN_WEBHOOK_SECRET (>= 32 chars) is the secret path segment; ESIGN_WEBHOOK_ALLOWED_IPS is an
// optional comma-separated list of IPv4 addresses / CIDRs (e.g. "203.0.113.0/24,198.51.100.7").
app.post('/esign/webhook/:secret', async (req, res) => {
  const expected = process.env['ESIGN_WEBHOOK_SECRET'] ?? '';
  const allowlist = (process.env['ESIGN_WEBHOOK_ALLOWED_IPS'] ?? '')
    .split(',')
    .map((s) => s.trim())
    .filter((s) => s !== '');
  if (expected.length < 32) {
    res.status(404).end(); // not configured
    return;
  }
  if (!ipAllowed(req.ip, allowlist)) {
    res.status(403).end();
    return;
  }
  if (!secretMatches(req.params['secret'] ?? '', expected)) {
    res.status(404).end(); // do not reveal that the route exists
    return;
  }
  try {
    const { ack, outcome } = await esign().handleWebhook(req.body);
    if (outcome.kind === 'SuccessWithSignedBytes') {
      // Persist outcome.signedBytes for outcome.transactionId (also what webhookDeliveryHook sees).
    }
    res.json(ack);
  } catch (e) {
    if (isESignException(e)) res.status(502).json({ category: e.category });
    else res.status(500).end();
  }
});

app.listen(3000, () => console.log('listening on http://localhost:3000'));
