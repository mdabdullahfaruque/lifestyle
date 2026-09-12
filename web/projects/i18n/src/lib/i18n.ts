import { InjectionToken, Injectable, computed, inject, signal } from '@angular/core';

/**
 * Supported locales. Bangla first for a Bangladesh deployment is a product decision, not a
 * technical one — see `defaultLocale()` for what actually gets chosen.
 */
export type Locale = 'en' | 'bn';

export const LOCALES: readonly { code: Locale; label: string; short: string }[] = [
  { code: 'en', label: 'English', short: 'EN' },
  // Endonym, not "Bengali": a Bangla speaker looking for their own language looks for this.
  { code: 'bn', label: 'বাংলা', short: 'বাং' },
];

/** One flat dictionary per locale. Keys are dotted paths, e.g. `home.freshThisWeek`. */
export type Messages = Record<string, string>;

/**
 * Each app provides its own catalogue. A single shared catalogue would force the storefront to
 * ship the consoles' strings and vice versa.
 */
export const LOCALE_MESSAGES = new InjectionToken<Record<Locale, Messages>>('lifestyle.messages');

const STORAGE_KEY = 'lifestyle.locale';

/**
 * Translation lookup.
 *
 * `t()` is a plain method rather than a pipe on purpose. A *pure* pipe is not re-evaluated when
 * the locale signal changes but its input key does not, so the page would keep the old language
 * until something else forced a re-render. An impure pipe avoids that but runs on every change
 * detection pass for every binding. A method call reads the locale signal inside the template's
 * reactive context, so switching language marks exactly the components that display text.
 */
@Injectable({ providedIn: 'root' })
export class I18nStore {
  private readonly catalogues = inject(LOCALE_MESSAGES);

  private readonly current = signal<Locale>(defaultLocale());

  readonly locale = this.current.asReadonly();
  readonly locales = LOCALES;

  /** BCP 47 tag for Intl APIs — number and date formatting, not translation. */
  readonly bcp47 = computed(() => (this.current() === 'bn' ? 'bn-BD' : 'en-GB'));

  readonly isRtl = computed(() => false); // Neither Bangla nor English is RTL; here for the next market.

  setLocale(locale: Locale): void {
    if (!LOCALES.some((l) => l.code === locale)) return;
    this.current.set(locale);

    try {
      localStorage.setItem(STORAGE_KEY, locale);
    } catch {
      // Private browsing or blocked storage. The choice still applies to this page view, which is
      // the part the visitor actually asked for.
    }

    if (typeof document !== 'undefined') document.documentElement.lang = locale;
  }

  /**
   * Looks up `key`, interpolating `{name}` placeholders.
   *
   * A missing key falls back to English and then to the key itself. Returning the key rather than
   * an empty string is deliberate: a visible `home.freshThisWeek` on the page is a bug report,
   * whereas blank text looks like an intentionally empty design and ships unnoticed.
   */
  t(key: string, params?: Record<string, string | number>): string {
    const locale = this.current();
    const text = this.catalogues[locale]?.[key] ?? this.catalogues.en?.[key] ?? key;
    if (!params) return text;

    return text.replace(/\{(\w+)\}/g, (whole, name: string) =>
      params[name] === undefined ? whole : String(params[name]),
    );
  }

  /** Digits in the reader's script: ৳১,৮৯০ rather than ৳1,890 for a Bangla reader. */
  formatNumber(value: number, options?: Intl.NumberFormatOptions): string {
    return value.toLocaleString(this.bcp47(), options);
  }
}

/**
 * Remembered choice first, then the browser's preference, then English.
 *
 * English is the fallback rather than Bangla because a visitor whose browser asks for neither is
 * more likely to be a non-Bangla speaker than a Bangla speaker with a misconfigured browser.
 */
function defaultLocale(): Locale {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === 'en' || stored === 'bn') return stored;
  } catch {
    // Storage unavailable — fall through to the browser preference.
  }

  if (typeof navigator !== 'undefined') {
    const preferred = [navigator.language, ...(navigator.languages ?? [])].filter(Boolean);
    if (preferred.some((l) => l.toLowerCase().startsWith('bn'))) return 'bn';
  }

  return 'en';
}
