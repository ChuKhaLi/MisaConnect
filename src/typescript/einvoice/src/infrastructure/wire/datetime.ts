export interface ParsedDateTime {
  date: string;
  time: string;
  /** Fraction digits as written, without the dot (may be empty). */
  fraction: string;
  /** `+HH:MM` / `-HH:MM`, or undefined when the text carried no offset. */
  offset?: string;
}

const PATTERN =
  /^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2})(?::(\d{2})(?:\.(\d+))?)?)?\s*(Z|[+-]\d{2}(?::?\d{2})?)?$/i;

export function isRealDate(y: number, m: number, d: number): boolean {
  if (m < 1 || m > 12 || d < 1) return false;
  const dim = new Date(Date.UTC(2000, m, 0)).getUTCDate();
  const leap = (y % 4 === 0 && y % 100 !== 0) || y % 400 === 0;
  return d <= (m === 2 && !leap ? 28 : dim);
}

/** Parses `yyyy-MM-dd[Thh:mm[:ss[.f]]][Z|±hh[:mm]]`; returns undefined when malformed. */
export function parseDateTime(text: string): ParsedDateTime | undefined {
  const m = PATTERN.exec(text.trim());
  if (!m) return undefined;
  const [, ys, ms, ds, hs = '00', mins = '00', ss = '00', frac = '', off] = m;
  if (!isRealDate(Number(ys), Number(ms), Number(ds))) return undefined;
  if (Number(hs) > 23 || Number(mins) > 59 || Number(ss) > 59) return undefined;
  let offset: string | undefined;
  if (off !== undefined) {
    if (off.toUpperCase() === 'Z') {
      offset = '+00:00';
    } else {
      const digits = off.slice(1).replace(':', '');
      const hh = digits.slice(0, 2);
      const mm = digits.length > 2 ? digits.slice(2) : '00';
      if (Number(hh) > 14 || Number(mm) > 59) return undefined;
      offset = `${off[0]}${hh}:${mm}`;
    }
  }
  return {
    date: `${ys}-${ms}-${ds}`,
    time: `${hs}:${mins}:${ss}`,
    fraction: frac,
    ...(offset !== undefined ? { offset } : {}),
  };
}
