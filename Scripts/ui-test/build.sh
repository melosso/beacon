#!/bin/bash
# Builds a static copy of the admin app with a stubbed API: build.sh <wwwroot> <out> <testscript.js>
W=$1; O=$2; T=$3
rm -rf $O/site; mkdir -p $O/site/admin $O/site/js
for d in css fonts lib js; do cp -r $W/$d $O/site/; done
cp $W/favicon.* $O/site/ 2>/dev/null
cat > $O/site/admin/config.js <<'C'
const API_BASE = "";
const PUBLIC_URL = "";
const DEFAULT_EXPIRY_DAYS = 30;
const DISABLE_EMAIL_NOTIFICATIONS = false;
const USER_AUTH_METHOD = "";
C
for p in core sidebar views records tokens webhooks submissions settings data-policies system-modals branding; do cat $W/js/admin/$p.js; echo; done > $O/site/js/admin.js
{
 sed 's#<script src="/js/theme.js"></script>#<script src="/stub.js"></script><script src="/js/theme.js"></script>#' $W/admin/_shell.html
 for p in views/overview views/subscriptions views/bucket views/new-token views/new-bucket views/submissions views/submission-create views/submission-preview views/workflow views/audit views/settings modals/bucket-settings modals/bucket-mgmt modals/permissions modals/system-settings modals/share-audit modals/users-apikeys modals/brand-identity; do cat $W/admin/$p.html; done
 sed 's#</body>#<script src="/test.js" defer></script></body>#' $W/admin/_footer.html
} > $O/site/admin/index.html
cat > $O/site/stub.js <<'J'
// Stubbed API for headless UI tests.
const FAKE = {
  "/api/admin/buckets": [{ name: "newsletter", displayName: "Newsletter", recordCount: 3, isArchived: false }],
  "/api/admin/auth/me": { role: "admin", username: "admin" },
  "/api/admin/submissions": [{ id: "f1", name: "Main", bucket: "newsletter", permission: "news", allowedOrigins: ["https://a.test"], submissionCount: 0, isEnabled: true }],
  "/api/admin/data-policies/tasks": [
    { id: "t1", reference: "WF-00000001", taskType: "RetentionPurge", status: "PendingApproval", triggeredBy: "Manual", scheduledAt: "2026-11-28T11:08:00Z", recordsAffected: 0, pendingRecords: 2 },
    { id: "t2", reference: "WF-00000002", taskType: "PendingConfirmationPurge", status: "Completed", triggeredBy: "Cron", scheduledAt: "2026-11-27T03:00:00Z", startedAt: "2026-11-27T03:00:00Z", completedAt: "2026-11-27T03:00:01Z", recordsAffected: 3 }],
  "/api/admin/audit": { records: [{ id: "e1", bucket: "newsletter", emailHash: "c".repeat(64), email: null, displayId: "cccccccccccccccc...", permission: "news", oldStatus: "OptedOut", newStatus: "OptedOut", source: "Erasure", actorId: "WF-00000002", changedAt: "2026-11-27T03:00:01Z" }], total: 1, page: 1, pageSize: 25 },
  "/api/admin/data-policies/tasks/t1/records": { total: 2, records: [
    { emailHash: "a".repeat(64), email: "anna@example.com", bucket: "newsletter", permission: "news", status: "OptedOut", changedAt: "2025-01-02T10:00:00Z" },
    { emailHash: "b".repeat(64), email: null, bucket: "newsletter", permission: "offers", status: "OptedOut", changedAt: "2025-03-04T10:00:00Z" } ] },
};
window.fetch = async (url) => {
  window.__requests = [...(window.__requests || []), String(url)];
  const path = String(url).split("?")[0];
  const query = new URLSearchParams(String(url).split("?")[1] || "");
  // Bucket records: total and per-page delay are test-controlled (window.__total, window.__slowPage).
  if (path === "/api/admin/buckets/newsletter/records") {
    const page = Number(query.get("page"));
    if (page === window.__slowPage) await new Promise((r) => setTimeout(r, 400));
    const total = page === window.__slowPage ? 999 : (window.__total ?? 14700);
    return new Response(JSON.stringify({ records: [], total, page }), { status: 200, headers: { "content-type": "application/json" } });
  }
  const body = path in FAKE ? FAKE[path] : path.startsWith("/api/admin/submissions/") ? { id: "f1", name: "Main", bucket: "newsletter", permission: "news", allowedOrigins: ["https://a.test"], formConfig: {} } : {};
  return new Response(JSON.stringify(body), { status: 200, headers: { "content-type": "application/json" } });
};
window.EventSource = class { constructor() { this.readyState = 1; } addEventListener() {} close() {} };
J
cp $T $O/site/test.js
