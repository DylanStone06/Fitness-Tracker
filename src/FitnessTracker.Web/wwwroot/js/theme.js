// Appearance "Match my device" (Preferences, ACC-3): follow the device's light or dark setting. Dark and light pages
// arrive ready-made from the server; only this choice loads this file. It runs in <head>, before the page is drawn,
// so a device set to dark never flashes the light theme first. Without JavaScript the page stays light.
(() => {
  const dark = window.matchMedia('(prefers-color-scheme: dark)');
  const apply = () => document.documentElement.setAttribute('data-bs-theme', dark.matches ? 'dark' : 'light');
  apply();
  dark.addEventListener('change', apply);
})();
