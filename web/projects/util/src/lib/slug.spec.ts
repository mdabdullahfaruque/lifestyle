import { looksLikeEmail } from './email';
import { describeSlugProblem, slugify } from './slug';

/**
 * These rules have to agree with `Slug.From` and `Slug.IsValid` on the server. When they drift,
 * the symptom is a form that refuses input the API would have accepted — or the reverse, which is
 * worse: a form that accepts input and then shows a server error nobody can act on.
 */
describe('slugify', () => {
  it('turns typed text into a web address rather than refusing it', () => {
    // The case that sent "A shop address may only contain lowercase letters…" to a seller who had
    // written down where their shop is.
    expect(slugify('Uttara, Dhaka')).toBe('uttara-dhaka');
    expect(slugify('Arunima Crafts')).toBe('arunima-crafts');
  });

  it('matches the server on trimming, case, diacritics and repeated separators', () => {
    expect(slugify('  Arunima  Crafts  ')).toBe('arunima-crafts');
    expect(slugify('Café Niloy')).toBe('cafe-niloy');
    expect(slugify('Shop--With___Odd  Separators')).toBe('shop-with-odd-separators');
    expect(slugify('already-a-slug')).toBe('already-a-slug');
  });

  it('caps at the 80 characters the server allows, without a trailing hyphen', () => {
    const slug = slugify('a '.repeat(60));
    expect(slug.length).toBeLessThanOrEqual(80);
    expect(slug.endsWith('-')).toBeFalse();
  });

  it('yields nothing when there is nothing to work with', () => {
    expect(slugify('!!!')).toBe('');
    expect(slugify('   ')).toBe('');
  });
});

describe('describeSlugProblem', () => {
  it('accepts anything that survives as a usable address', () => {
    expect(describeSlugProblem('Uttara, Dhaka')).toBeNull();
    expect(describeSlugProblem('abc')).toBeNull();
  });

  it('refuses text with nothing usable in it', () => {
    expect(describeSlugProblem('!!!')).toContain('letters or numbers');
  });

  it('refuses anything shorter than the three characters Slug.IsValid requires', () => {
    expect(describeSlugProblem('ab')).toContain('three');
  });
});

describe('looksLikeEmail', () => {
  it('accepts ordinary addresses', () => {
    // This one is here by name: a regex that had lost its backslashes rejected every address
    // containing an "s", and this is the address that exposed it.
    expect(looksLikeEmail('ishratjahanaria@gmail.com')).toBeTrue();
    expect(looksLikeEmail('s@s.co')).toBeTrue();
    expect(looksLikeEmail('first.last+tag@sub.domain.co.uk')).toBeTrue();
  });

  it('catches the obvious slips, and nothing more', () => {
    expect(looksLikeEmail('no-at-sign')).toBeFalse();
    expect(looksLikeEmail('two@@at.com')).toBeFalse();
    expect(looksLikeEmail('trailing@dot.')).toBeFalse();
    expect(looksLikeEmail('spaces in@mail.com')).toBeFalse();
  });
});
