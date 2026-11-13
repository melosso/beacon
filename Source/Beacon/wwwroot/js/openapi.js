Scalar.createApiReference("#app", {
  url: "/openapi/v1.json",
  theme: "alternate",
  hideClientButton: true,
  hideSearch: true,
  defaultOpenAllTags: true,
  hideModels: true,
  telemetry: false,
  withDefaultFonts: false,
  authentication: { preferredSecurityScheme: "Bearer" },
  customCss: `:root, .light-mode, .dark-mode {
    --scalar-font: "IBM Plex Sans", system-ui, sans-serif;
    --scalar-font-code: "IBM Plex Mono", ui-monospace, monospace;
  }`,
  showDeveloperTools: 'never',
});
console.log(
  "%c@melosso/beacon%c. Licensed under %cAGPL 3.0%c.",
  "color:#6f42c1;font-weight:bold",
  "",
  "color:#28a745;font-weight:bold",
  "",
);
