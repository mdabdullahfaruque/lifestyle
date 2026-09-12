import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthStore } from './auth.store';
import { API_BASE_URL } from './surface';

/** Endpoints that must never carry a bearer token, or trigger a refresh when they fail. */
const ANONYMOUS_PATHS = [
  '/v1/auth/login',
  '/v1/auth/google',
  '/v1/auth/register',
  '/v1/auth/refresh',
  '/v1/auth/logout',
];

/**
 * Attaches the access token, and on a 401 exchanges the refresh cookie once and replays the request.
 *
 * Access tokens last 15 minutes, so a 401 mid-session is expected rather than exceptional; without
 * this the user would be bounced to the login screen four times an hour.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthStore);
  const baseUrl = inject(API_BASE_URL);

  // Only our own API gets the token. Sending it to a third-party host would leak the session.
  if (!request.url.startsWith(baseUrl)) return next(request);

  const isAnonymous = ANONYMOUS_PATHS.some((path) => request.url.includes(path));
  const token = auth.accessToken();

  const authorised =
    token && !isAnonymous
      ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` }, withCredentials: true })
      : request.clone({ withCredentials: true });

  return next(authorised).pipe(
    catchError((error: unknown) => {
      const is401 = error instanceof HttpErrorResponse && error.status === 401;

      if (!is401 || isAnonymous) return throwError(() => error);

      // One attempt only. If the refresh itself 401s, the session is genuinely over and retrying
      // would loop.
      return from(auth.restore()).pipe(
        switchMap((restored) => {
          if (!restored) return throwError(() => error);

          return next(
            request.clone({
              setHeaders: { Authorization: `Bearer ${auth.accessToken()}` },
              withCredentials: true,
            }),
          );
        }),
      );
    }),
  );
};
