// Runs in <head> before first paint to avoid a theme/font flash.
const APP_FONTS = {
  inter: '"Inter", system-ui, -apple-system, sans-serif',
  manrope: '"Manrope", system-ui, -apple-system, sans-serif',
  system: "system-ui, -apple-system, sans-serif",
};
try {
  const saved = JSON.parse(localStorage.getItem("beacon_settings") || "{}");
  if (saved.theme && saved.theme !== "system")
    document.documentElement.setAttribute("data-theme", saved.theme);
  document.documentElement.style.setProperty(
    "--app-font",
    APP_FONTS[saved.font] || APP_FONTS.inter,
  );
} catch {
  /* Private mode or corrupt storage: defaults apply. */
}
