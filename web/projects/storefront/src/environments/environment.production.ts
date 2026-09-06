export const environment = {
  production: true,
  // Same-origin in production: the reverse proxy routes /v1/* to the API, so the browser never
  // makes a cross-origin request and the refresh cookie stays SameSite=Strict.
  apiBaseUrl: '',
  // Uploads are served read-only from their own host, cached hard (docs/05 §6).
  mediaBaseUrl: 'https://media.mylifestylemart.com',
};
