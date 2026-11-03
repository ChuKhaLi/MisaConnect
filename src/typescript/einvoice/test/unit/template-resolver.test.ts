import { describe, expect, it } from 'vitest';
import { inferWithCode, resolveTemplate } from '../../src/application/template-resolver.js';
import type { TemplateDto } from '../../src/domain/dtos.js';

const tpl = (id: string, series: string): TemplateDto => ({
  ipTemplateID: id,
  invSeries: series,
  templateName: 'n',
  templateType: 1,
  usesTaxAuthorityCode: true,
  isActive: true,
  isMoreVATRate: false,
});
const a = tpl('T-A', '1C26TAA');
const b = tpl('T-B', '1K26TBB');

describe('resolveTemplate', () => {
  it('no ref + no templates -> NoActiveTemplate', () => {
    expect(resolveTemplate([])).toEqual({ kind: 'NoActiveTemplate' });
  });
  it('no ref + one template -> Resolved', () => {
    expect(resolveTemplate([a])).toEqual({ kind: 'Resolved', template: a });
  });
  it('no ref + two templates -> AmbiguousTemplate with candidates', () => {
    expect(resolveTemplate([a, b])).toEqual({ kind: 'AmbiguousTemplate', candidates: [a, b] });
  });
  it('series match is case-insensitive', () => {
    expect(resolveTemplate([a, b], { ipTemplateID: '', invSeries: '1k26tbb' })).toEqual({
      kind: 'Resolved',
      template: b,
    });
  });
  it('id match is case-insensitive and both must match when both given', () => {
    expect(resolveTemplate([a, b], { ipTemplateID: 't-a', invSeries: '1C26TAA' }).kind).toBe(
      'Resolved',
    );
    expect(resolveTemplate([a, b], { ipTemplateID: 'T-A', invSeries: '1K26TBB' }).kind).toBe(
      'UnknownTemplate',
    );
  });
  it('unknown ref -> UnknownTemplate keyed by id, else series', () => {
    expect(resolveTemplate([a], { ipTemplateID: 'X', invSeries: 'S' })).toEqual({
      kind: 'UnknownTemplate',
      key: 'X',
    });
    expect(resolveTemplate([a], { ipTemplateID: '', invSeries: 'S' })).toEqual({
      kind: 'UnknownTemplate',
      key: 'S',
    });
  });
});

describe('inferWithCode', () => {
  it('reads the 2nd character', () => {
    expect(inferWithCode('1C26TAA')).toBe(true);
    expect(inferWithCode('1k26TAA')).toBe(false);
    expect(inferWithCode('1X26TAA')).toBeUndefined();
    expect(inferWithCode('1')).toBeUndefined();
    expect(inferWithCode(undefined)).toBeUndefined();
  });
});
