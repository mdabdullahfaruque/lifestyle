/**
 * Web addresses, derived the same way the server derives them.
 *
 * This mirrors `Slug.From` in `Lifestyle.SharedKernel.Identifiers` — lowercase, strip diacritics,
 * collapse everything that is not a letter or digit into single hyphens, trim, cap at 80. It lives
 * in a shared lib because both places that ask for a shop address need identical behaviour, and
 * two copies of this rule is how a form starts disagreeing with the API that validates it.
 */
const MAX_LENGTH = 80;

export function slugify(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/-{2,}/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, MAX_LENGTH)
    .replace(/-+$/, '');
}

/**
 * Why a piece of text cannot become a web address, or null when it can.
 *
 * Deliberately not "is this already a slug". Someone typing their shop name, or a place, has not
 * made a mistake — the address is derived from what they type. Only text with nothing usable in
 * it, or too little, has to be refused; the three-character floor is the API's (`Slug.IsValid`).
 */
export function describeSlugProblem(value: string): string | null {
  const slug = slugify(value);

  if (!slug) return 'That has no letters or numbers in it to build a web address from.';
  if (slug.length < 3) return 'A web address needs at least three letters or numbers.';

  return null;
}
