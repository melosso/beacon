// Matrix test: each edit type with each navigation path asserts whether the discard prompt opens.
(async () => {
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));
  const out = [];
  let fails = 0;
  const prompted = () => document.getElementById("confirmModal").open;
  const touch = (el) => el.dispatchEvent(new FocusEvent("focusin", { bubbles: true }));
  const type = (id, v) => { const el = document.getElementById(id); touch(el); el.value = v; el.dispatchEvent(new Event("input", { bubbles: true })); };
  const open = async () => {
    if (prompted()) closeConfirmModal();
    document.querySelectorAll("dialog[open]").forEach((d) => closeModal(d.id, true));
    guardReset("view-submission-create");
    history.pushState(null, "", "?view=overview");
    showView("submission-create");
    await wait(150);
  };
  const edits = {
    "typed name": () => type("nlName", "Edited"),
    "typed then reverted": () => { type("nlName", "x"); type("nlName", ""); },
    "autocomplete pick (programmatic)": () => { touch(document.getElementById("nlBucket")); document.getElementById("nlBucket").value = "newsletter"; },
    "added origin (array state)": () => { type("nlNewOrigin", "https://b.test"); addSubmissionOrigin(); },
    "toggle checkbox": () => { const c = document.getElementById("nlIsEnabled"); c.dispatchEvent(new PointerEvent("pointerdown", { bubbles: true })); c.click(); },
    "no edit": () => {},
  };
  const navs = {
    "nav Workflow": () => document.getElementById("navWorkflow").click(),
    "sidebar bucket": () => showBucket("newsletter"),
    "Cancel": () => document.querySelector('#nlStep1 [data-click="showView submissions"]').click(),
    "back": () => history.back(),
  };
  const expectPrompt = (e) => !["typed then reverted", "no edit"].includes(e);
  await wait(800);
  for (const [e, edit] of Object.entries(edits))
    for (const [n, nav] of Object.entries(navs)) {
      await open(); edit(); nav(); await wait(250);
      const ok = prompted() === expectPrompt(e) && (n !== "back" || !prompted() || location.search.includes("submission-create"));
      if (!ok) fails++;
      out.push(`${ok ? "ok  " : "FAIL"} ${e} + ${n}: prompt=${prompted()}`);
    }
  await open(); type("nlName", "x"); window.__unload = new Event("beforeunload", { cancelable: true }); window.dispatchEvent(window.__unload);
  out.push(`${window.__unload.defaultPrevented ? "ok  " : "FAIL"} beforeunload blocked when dirty`);
  // Dialog: newTokenSheet uses data-guard; edit and close.
  if (prompted()) closeConfirmModal();
  guardReset("view-submission-create");
  showView("new-token"); await wait(200);
  const sheet = document.querySelector("dialog[open]");
  const field = sheet?.querySelector("[data-guard] input:not([type=checkbox]):not([data-instant])");
  if (field) { touch(field); field.value = "zzz"; field.dispatchEvent(new Event("input", { bubbles: true })); }
  const closed = closeModal(sheet.id);
  out.push(`${!closed && prompted() ? "ok  " : "FAIL"} dirty dialog close prompts (${sheet?.id})`);
  out.unshift(`fails=${fails + out.filter((l) => l.startsWith("FAIL")).length - fails}`);
  const pre = document.createElement("pre"); pre.id = "guardlog"; pre.textContent = out.join("\n"); document.body.append(pre);
})();
