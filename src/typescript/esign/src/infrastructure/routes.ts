import type { ResolvedESignOptions } from './options.js';

import { Routes, type RouteKey } from '../application/errors/routes.js';

export { Routes, type RouteKey };

const LABELS: Readonly<Record<RouteKey, string>> = Object.freeze({
  login: 'login-api',
  twoFactor: 'two-factor-auth',
  refresh: 'refreshtoken',
  resendOtp: 'resend-otp-auth',
  certificates: 'Certificates/by-userId',
  hash: 'documents/hash',
  signHash: 'Signing/hash',
  signStatus: 'Signing/status',
  attachment: 'documents/attachment',
});

/** Short endpoint label for logs. */
export function endpointLabel(route: RouteKey): string {
  return LABELS[route];
}

/**
 * Absolute URL for a route: the origin plus the canonical path, with `webdev/` prepended to the
 * login and two-factor routes when `authUnderWebdev` is on. `suffix` is appended as an encoded
 * path segment.
 */
export function resolveUrl(
  resolved: Pick<ResolvedESignOptions, 'origin' | 'authUnderWebdev'>,
  route: RouteKey,
  suffix?: string,
): string {
  const webdev = resolved.authUnderWebdev && (route === 'login' || route === 'twoFactor');
  const base = `${resolved.origin}${webdev ? 'webdev/' : ''}${Routes[route]}`;
  return suffix === undefined ? base : `${base}/${encodeURIComponent(suffix)}`;
}
