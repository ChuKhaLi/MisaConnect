export type DisambiguatedDelete = 'NotFound' | 'NotDeletable';

// Port of .NET InvalidTransactionAllowList: substring match, first hit wins.
const ALLOW_LIST: ReadonlyArray<readonly [string, DisambiguatedDelete]> = [
  ['không tồn tại', 'NotFound'],
  ['đã phát hành', 'NotDeletable'],
  ['đã ký', 'NotDeletable'],
].map(([phrase, kind]) => [norm(phrase as string), kind as DisambiguatedDelete] as const);

function norm(s: string): string {
  return s.normalize('NFC').toLowerCase();
}

export function disambiguateInvalidTransaction(
  message: string | undefined,
): DisambiguatedDelete | undefined {
  if (message === undefined || message.trim() === '') return undefined;
  const text = norm(message);
  for (const [phrase, kind] of ALLOW_LIST) {
    if (text.includes(phrase)) return kind;
  }
  return undefined;
}
