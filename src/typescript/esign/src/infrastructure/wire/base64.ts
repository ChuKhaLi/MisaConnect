/** Base64 helpers for document payloads. Lives in infrastructure because it uses `Buffer`. */
export function encodeBase64(bytes: Uint8Array): string {
  return Buffer.from(bytes.buffer, bytes.byteOffset, bytes.byteLength).toString('base64');
}

export function decodeBase64(text: string): Uint8Array {
  const buf = Buffer.from(text, 'base64');
  return new Uint8Array(buf.buffer, buf.byteOffset, buf.byteLength);
}
