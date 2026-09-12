import { MoneyRange } from 'data-access';

/**
 * Money arrives as a decimal *string* so no precision is lost on the wire (FRD §19.1). Formatting
 * parses it once, at the edge, and never does arithmetic on the result beyond the saving
 * percentage — which is display-only and rounded down, so a "SAVE 16%" badge can never overstate
 * the discount.
 */
export interface PriceParts {
  now: string;
  was: string | null;
  saving: number | null;
  currency: string;
}

const SYMBOLS: Record<string, string> = { BDT: '৳', MYR: 'RM ', USD: '$' };

export function symbolFor(currency: string): string {
  return SYMBOLS[currency] ?? `${currency} `;
}

/**
 * `1890.00` → `৳1,890` in English, `৳১,৮৯০` in Bangla.
 *
 * The locale drives the digits: a Bangla reader expects Bangla numerals, and `Intl` produces them
 * from the `bn-BD` tag. The currency symbol is prepended by hand rather than using
 * `style: 'currency'`, because that places and spaces the taka sign differently per locale and the
 * design specifies one consistent treatment.
 *
 * Whole taka only when the amount is whole — sub-unit precision is noise at these prices.
 */
export function formatMoney(amount: string, currency: string, bcp47 = 'en-GB'): string {
  const value = Number(amount);
  if (!Number.isFinite(value)) return `${symbolFor(currency)}${amount}`;

  const whole = Math.round(value * 100) % 100 === 0;
  return (
    symbolFor(currency) +
    value.toLocaleString(bcp47, {
      minimumFractionDigits: whole ? 0 : 2,
      maximumFractionDigits: whole ? 0 : 2,
    })
  );
}

export function priceParts(price: MoneyRange, compareAt?: string | null, bcp47 = 'en-GB'): PriceParts {
  const now = formatMoney(price.min, price.currency, bcp47);
  const from = Number(price.min);
  const previous = compareAt == null ? null : Number(compareAt);

  const hasSaving = previous !== null && Number.isFinite(previous) && previous > from;

  return {
    now: price.min === price.max ? now : `${now}+`,
    was: hasSaving ? formatMoney(String(previous), price.currency, bcp47) : null,
    saving: hasSaving ? Math.floor(((previous - from) / previous) * 100) : null,
    currency: price.currency,
  };
}
