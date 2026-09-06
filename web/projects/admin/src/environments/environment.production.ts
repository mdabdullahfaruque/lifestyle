export const environment = {
  production: true,
  // REQUIRED before the first Cloudflare Pages deploy: replace example.com with the real root
  // domain (docs/06 step D1). This app is served from Pages, so the API is cross-origin — an
  // empty string here would make it call the Pages host itself and fail on every request.
  apiBaseUrl: 'https://api.mylifestylemart.com',
};
