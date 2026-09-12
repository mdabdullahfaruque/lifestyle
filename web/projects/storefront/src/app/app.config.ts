import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideClientHydration, withEventReplay } from '@angular/platform-browser';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { API_BASE_URL, SURFACE, authInterceptor } from 'auth';
import { LOCALE_MESSAGES } from 'i18n';

import { environment } from '../environments/environment';
import { routes } from './app.routes';
import { MEDIA_BASE_URL } from './media';
import { STOREFRONT_MESSAGES } from './messages';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),

    provideRouter(
      routes,
      withComponentInputBinding(),
      // Restore scroll on back, and jump to the top on a new page. Without this a buyer paging
      // through a category lands halfway down the next page.
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled', anchorScrolling: 'enabled' }),
    ),

    // withFetch is required for SSR: it lets the server transfer the response into the client's
    // hydration state instead of the browser re-issuing every request.
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
    provideClientHydration(withEventReplay()),

    { provide: API_BASE_URL, useValue: environment.apiBaseUrl },
    { provide: MEDIA_BASE_URL, useValue: environment.mediaBaseUrl },
    { provide: LOCALE_MESSAGES, useValue: STOREFRONT_MESSAGES },
    { provide: SURFACE, useValue: 'buyer' as const },
  ],
};
