(() => {
  const requested = new URLSearchParams(window.location.search).get("theme");
  const override = requested === "light" || requested === "dark" ? requested : null;
  const theme =
    override || (window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light");
  document.documentElement.setAttribute("data-theme", theme);
})();
