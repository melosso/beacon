// One bucket picker for the token sheet, the form editor and the subscriber export: same suggestions, keyboard pick, all-buckets option.
(async () => {
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));
  const out = [];
  const check = (label, ok) => out.push(`${ok ? "ok  " : "FAIL"} ${label}`);
  const key = (el, k) => el.dispatchEvent(new KeyboardEvent("keydown", { key: k, bubbles: true }));
  await wait(800);
  const fields = { token: "tokenBucket", form: "nlBucket", export: "subExportBucket" };
  showView("new-token"); await wait(200);
  for (const [label, id] of Object.entries(fields)) {
    if (label === "form") { closeModal("newTokenSheet", true); showView("submission-create"); await wait(200); }
    if (label === "export") { showView("subscriptions"); await wait(300); openSubExportModal(); await wait(100); }
    const input = document.getElementById(id);
    input.focus(); input.value = "news"; input.dispatchEvent(new Event("input", { bubbles: true })); await wait(50);
    const list = document.getElementById(`${id}Suggestions`);
    const items = list?.querySelectorAll(".autocomplete-item") ?? [];
    check(`${label}: suggestions open (${items.length}), shows counts`, list?.classList.contains("open") && items.length === 1 && items[0].textContent.includes("records"));
    key(input, "ArrowDown"); key(input, "Enter"); await wait(50);
    check(`${label}: keyboard pick -> ${input.value}`, input.value === "newsletter" && !list.classList.contains("open"));
  }
  const exp = document.getElementById("subExportBucket");
  exp.value = ""; exp.dispatchEvent(new Event("input", { bubbles: true })); await wait(50);
  const first = document.querySelector("#subExportBucketSuggestions .autocomplete-item")?.textContent.trim();
  check(`export: first option is "${first}"`, first === "All buckets");
  const fails = out.filter((l) => l.startsWith("FAIL")).length;
  const pre = document.createElement("pre"); pre.id = "guardlog"; pre.textContent = [`fails=${fails}`, ...out].join("\n"); document.body.append(pre);
})();
