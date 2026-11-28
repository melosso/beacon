// Workflow tasks show a short reference; a completed run's reference opens the audit log filtered to that run.
(async () => {
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));
  await wait(800);
  showView("workflow"); await wait(300);
  if (document.getElementById("workflowArchiveToggle").textContent.includes("Show")) toggleWorkflowArchived();
  await wait(200);
  const refs = [...document.querySelectorAll("#workflowTbody td:first-child")].map((td) => td.textContent.trim());
  const link = [...document.querySelectorAll("#workflowTbody td:first-child .link-btn")].find((b) => b.textContent.trim() === "WF-00000002");
  link?.click(); await wait(400);
  const audited = window.__requests.some((u) => u.includes("/api/admin/audit") && u.includes("actor=WF-00000002"));
  const chip = document.querySelector("#view-audit .filter-chip")?.textContent.trim();
  const removed = document.getElementById("auditBody").textContent.includes("Personal data removed");
  const ok = refs.includes("WF-00000001") && !!link && audited && chip === "WF-00000002" && removed && location.search.includes("actor=WF-00000002");
  const pre = document.createElement("pre"); pre.id = "guardlog";
  pre.textContent = `fails=${ok ? 0 : 1}\nrefs=${refs.join(",")} link=${!!link} auditRequest=${audited} chip=${chip} dataRemovedChip=${removed} url=${location.search}`;
  document.body.append(pre);
})();
