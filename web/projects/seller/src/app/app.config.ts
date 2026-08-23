import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { API_BASE_URL, SURFACE, authInterceptor } from 'auth';

import { environment } from '../environments/environment';
import { routes } from './app.routes';

// SPA, not SSR: this surface sits behind authentication, so there is no SEO value and SSR would
// add complexity for nothing (FRD §20.2).
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),

    { provide: API_BASE_URL, useValue: environment.apiBaseUrl },
    { provide: SURFACE, useValue: 'seller' as const },
  ],
};
