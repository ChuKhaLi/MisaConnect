/** Canonical MISA endpoint paths (mirrors .NET `ESignHttpRoutes`). */
export const Routes = Object.freeze({
  login: 'api/auth/api/v1/auth/login-api',
  twoFactor: 'api/auth/api/v1/auth/two-factor-auth',
  refresh: 'webdev/api/auth/api/v1/auth/refreshtoken',
  resendOtp: 'webdev/api/auth/api/v1/auth/resend-otp-auth',
  certificates: 'external/esrm/service/general/api/v1/Certificates/by-userId',
  hash: 'external/esrm/service/document/api/v1/documents/hash',
  signHash: 'external/esrm/service/signing/api/v1/Signing/hash',
  signStatus: 'external/esrm/service/signing/api/v1/Signing/status',
  attachment: 'external/esrm/service/document/api/v1/documents/attachment',
} as const);

export type RouteKey = keyof typeof Routes;
