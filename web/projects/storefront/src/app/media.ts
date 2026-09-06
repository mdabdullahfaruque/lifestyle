import { InjectionToken, inject } from '@angular/core';

export const MEDIA_BASE_URL = new InjectionToken<string>('MEDIA_BASE_URL');

/** Derivative sizes the upload pipeline generates (MediaModuleOptions.VariantSizes). */
export type MediaVariant = 'thumb' | 'card' | 'large' | 'original';

/**
 * Builds the URL for an uploaded image.
 *
 * The catalog API returns a bare `mediaId` and no URL, so the key layout the media pipeline writes
 * — `{first two chars}/{id}/{variant}.png` (UploadFile.BuildKey) — has to be reproduced here.
 * That is a duplicated contract: if the server ever changes the layout, this breaks silently with
 * broken images rather than a build error. The fix is for the catalog responses to carry URLs;
 * until then this is the single place that knows the shape.
 *
 * A derivative is skipped when the source is smaller than the target (`large` on a 900px upload,
 * for instance), so anything using a named size needs an `original` fallback — see `srcWithFallback`.
 */
export function mediaUrl(base: string, mediaId: string, variant: MediaVariant = 'card'): string {
  return `${base.replace(/\/$/, '')}/${mediaId.slice(0, 2)}/${mediaId}/${variant}.png`;
}

export function useMedia() {
  const base = inject(MEDIA_BASE_URL);
  return {
    url: (mediaId: string, variant: MediaVariant = 'card') => mediaUrl(base, mediaId, variant),
    /** `onerror` handler: drop to the original when a derivative was never generated. */
    fallbackToOriginal: (event: Event, mediaId: string) => {
      const img = event.target as HTMLImageElement;
      const original = mediaUrl(base, mediaId, 'original');
      if (img.src !== original) img.src = original;
    },
  };
}
