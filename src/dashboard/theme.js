(() => {
  const parameters = new URLSearchParams(window.location.search);
  const requested = parameters.get("clawpilotTheme") || parameters.get("theme");
  const override = requested === "light" || requested === "dark" ? requested : null;
  const theme =
    override || (window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light");
  document.documentElement.setAttribute("data-theme", theme);
})();
