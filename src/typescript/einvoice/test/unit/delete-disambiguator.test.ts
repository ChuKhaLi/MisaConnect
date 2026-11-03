import { describe, it, expect } from 'vitest';
import { disambiguateInvalidTransaction } from '../../src/application/delete-disambiguator.js';

const phrases = [
  ['không tồn tại', 'NotFound'],
  ['đã phát hành', 'NotDeletable'],
  ['đã ký', 'NotDeletable'],
] as const;

describe('disambiguateInvalidTransaction', () => {
  it.each(phrases)('classifies "%s" as %s', (phrase, expected) => {
    expect(disambiguateInvalidTransaction(`Hóa đơn ${phrase} trong hệ thống`)).toBe(expected);
  });

  it.each(phrases)('classifies NFD form of "%s" as %s', (phrase, expected) => {
    const nfd = phrase.normalize('NFD');
    expect(nfd).not.toBe(phrase);
    expect(disambiguateInvalidTransaction(`Hóa đơn ${nfd}`)).toBe(expected);
  });

  it('is case-insensitive', () => {
    expect(disambiguateInvalidTransaction('HÓA ĐƠN ĐÃ PHÁT HÀNH')).toBe('NotDeletable');
  });

  it('returns undefined when nothing matches or input is blank', () => {
    expect(disambiguateInvalidTransaction('something else')).toBeUndefined();
    expect(disambiguateInvalidTransaction('   ')).toBeUndefined();
    expect(disambiguateInvalidTransaction(undefined)).toBeUndefined();
  });
});
