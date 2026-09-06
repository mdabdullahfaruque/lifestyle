export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5000',
  // Media is served by its own host in every environment, so it is configured separately from the
  // API rather than derived from it.
  mediaBaseUrl: 'http://localhost:5000/media',
};
