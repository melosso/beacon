// Opens the records drill-down of a pending workflow task and checks the listed rows.
(async () => {
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));
  await wait(800);
  showView("workflow"); await wait(400);
  document.querySelector("#workflowTbody .link-btn").click(); await wait(300);
  const m = document.getElementById("workflowRecordsModal");
  const rows = m.querySelectorAll("#workflowRecordsBody tr").length;
  const pre = document.createElement("pre"); pre.id = "guardlog";
  pre.textContent = `fails=${m.open && rows === 2 ? 0 : 1}\nopen=${m.open} rows=${rows} summary=${document.getElementById("workflowRecordsSummary").textContent}`;
  document.body.append(pre);
})();
