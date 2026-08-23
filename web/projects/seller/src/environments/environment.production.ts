export const environment = {
  production: true,
  // Same-origin in production: the reverse proxy routes /v1/* to the API, so the browser never
  // makes a cross-origin request and the refresh cookie stays SameSite=Strict.
  apiBaseUrl: '',
};
