import type { Certificate } from '../../domain/dtos.js';
import { ESignGeneralException, NoActiveCertificateException } from '../../domain/errors.js';
import type { EsrmCall, EsrmGateway } from '../esrm-gateway.js';

/**
 * Lists the user's certificates and keeps those whose `keyStatus` is "ACTIVE" (any case)
 * (INV §2.4 step 3). A chain that is not exactly [signing, intermediate, root] fails with
 * `MisaUnknown` "InvalidCertChainLength"; no active certificate → `NoActiveCertificateException`.
 */
export async function listActiveCertificates(
  gateway: EsrmGateway,
  call: EsrmCall,
): Promise<Certificate[]> {
  const all = await gateway.listCertificates(call);
  const active: Certificate[] = [];
  for (const raw of all) {
    const { chain, ...rest } = raw;
    if (chain.length !== 3) {
      throw new ESignGeneralException({
        category: 'MisaUnknown',
        rawCode: 'InvalidCertChainLength',
        detail: `MISA Certificates/by-userId returned a certificate with a chain of length ${chain.length} (expected 3).`,
        correlationId: call.correlationId,
        format: call.format,
      });
    }
    if (raw.keyStatus.toUpperCase() !== 'ACTIVE') continue;
    active.push({ ...rest, chain: [chain[0] as string, chain[1] as string, chain[2] as string] });
  }
  if (active.length === 0) {
    throw new NoActiveCertificateException({
      detail: 'No certificate with keyStatus = ACTIVE is available for the authenticated user.',
      correlationId: call.correlationId,
      format: call.format,
    });
  }
  return active;
}
