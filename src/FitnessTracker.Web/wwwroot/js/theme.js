// Dark mode follows the device's setting (docs/design.md). Loaded in <head>, before the page is drawn, so a device set
// to dark never flashes the light theme first. Without JavaScript the page stays light.
(() => {
  const dark = window.matchMedia('(prefers-color-scheme: dark)');
  const apply = () => document.documentElement.setAttribute('data-bs-theme', dark.matches ? 'dark' : 'light');
  apply();
  dark.addEventListener('change', apply);
})();
