import { InjectionToken } from '@angular/core';

/**
 * Which of the three authenticated surfaces this application is (FRD §4.2). Each app provides its
 * own value, and it is sent on every login and refresh so the API mints a token with the matching
 * audience. A buyer token is then rejected by admin endpoints outright.
 */
export type Surface = 'buyer' | 'seller' | 'admin';

export const SURFACE = new InjectionToken<Surface>('lifestyle.surface');

export const API_BASE_URL = new InjectionToken<string>('lifestyle.apiBaseUrl');
