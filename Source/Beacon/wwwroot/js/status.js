const ts = document.getElementById("ts");
if (ts) ts.textContent = new Date().toISOString().replace("T", " ").substring(0, 19) + " UTC";
document.querySelector(".back-link[data-back]")?.addEventListener("click", (e) => {
  if (history.length > 1) {
    e.preventDefault();
    history.back();
  }
});
