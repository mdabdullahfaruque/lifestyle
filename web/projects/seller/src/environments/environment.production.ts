export const environment = {
  production: true,
  // REQUIRED before the first Cloudflare Pages deploy: replace example.com with the real root
  // domain (docs/06 step D1). This app is served from Pages, so the API is cross-origin — an
  // empty string here would make it call the Pages host itself and fail on every request.
  apiBaseUrl: 'https://api.mylifestylemart.com',
  // Google OAuth web client id. Public by design — it ships in this bundle. Empty disables the
  // button entirely rather than rendering one that cannot work.
  googleClientId: '910217304034-bfblkqv80jvh9b6141jm49ornt2m45qv.apps.googleusercontent.com',
};
