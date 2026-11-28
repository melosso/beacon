// Tooltip engine: the only tooltip implementation (rules in DESIGN.md, Tooltips).
(() => {
  const { computePosition, autoUpdate, offset, flip, shift } = window.FloatingUIDOM;
  const SHOW_DELAY = 400;
  const WARM_MS = 300;

  const tip = document.createElement("div");
  tip.className = "tooltip";
  tip.id = "tooltip";
  tip.setAttribute("role", "tooltip");
  tip.setAttribute("popover", "manual");

  let trigger = null;
  let stopAutoUpdate = null;
  let showTimer = 0;
  let pending = null;
  let hiddenAt = 0;
  const textObserver = new MutationObserver(() => {
    if (!trigger) return;
    if (trigger.dataset.tooltip) tip.textContent = trigger.dataset.tooltip;
    else hide();
  });

  // Skip text the trigger displays untruncated, and triggers with an open menu.
  function redundant(el, text) {
    if (el.getAttribute("aria-expanded") === "true") return true;
    if (el.innerText.trim() !== text) return false;
    return ![el, ...el.children].some((n) => n.scrollWidth > n.clientWidth);
  }

  function position() {
    computePosition(trigger, tip, {
      strategy: "fixed",
      placement: trigger.dataset.tooltipPlacement || "top",
      middleware: [offset(6), flip(), shift({ padding: 8 })],
    }).then(({ x, y }) => {
      tip.style.left = `${x}px`;
      tip.style.top = `${y}px`;
    });
  }

  function show(el) {
    const text = el.dataset.tooltip;
    if (!text || redundant(el, text) || !el.isConnected) return;
    if (trigger !== el) hide();
    trigger = el;
    tip.textContent = text;
    if (!tip.isConnected) document.body.append(tip);
    tip.showPopover();
    el.setAttribute("aria-describedby", tip.id);
    textObserver.observe(el, { attributes: true, attributeFilter: ["data-tooltip"] });
    stopAutoUpdate = autoUpdate(el, tip, position);
  }

  function hide() {
    clearTimeout(showTimer);
    pending = null;
    if (!trigger) return;
    stopAutoUpdate?.();
    textObserver.disconnect();
    if (trigger.getAttribute("aria-describedby") === tip.id) trigger.removeAttribute("aria-describedby");
    trigger = null;
    if (tip.matches(":popover-open")) tip.hidePopover();
    hiddenAt = Date.now();
  }

  function schedule(el) {
    if (el === pending) return;
    clearTimeout(showTimer);
    pending = el;
    // Show at once if a tooltip closed within WARM_MS.
    const delay = trigger || Date.now() - hiddenAt < WARM_MS ? 0 : SHOW_DELAY;
    showTimer = setTimeout(() => {
      pending = null;
      show(el);
    }, delay);
  }

  document.addEventListener("pointerover", (e) => {
    if (e.pointerType === "touch") return;
    const el = e.target.closest?.("[data-tooltip]");
    if (el && el !== trigger) schedule(el);
  });
  document.addEventListener("pointerout", (e) => {
    const el = e.target.closest?.("[data-tooltip]");
    if (el && !el.contains(e.relatedTarget)) hide();
  });
  document.addEventListener("focusin", (e) => {
    const el = e.target.closest?.("[data-tooltip]");
    if (el && e.target.matches(":focus-visible")) show(el);
  });
  document.addEventListener("focusout", (e) => {
    if (trigger?.contains(e.target)) hide();
  });
  document.addEventListener("pointerdown", hide, true);
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && trigger) hide();
  });
})();
