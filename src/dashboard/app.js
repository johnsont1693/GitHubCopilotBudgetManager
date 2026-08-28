const config = window.BUDGET_MANAGER_CONFIG;
const apiBase = config.apiBaseUrl || "";
const enterpriseId = config.enterpriseId;
const authConfig = config.authentication || { enabled: false };
const tokenKey = "budget-manager-auth";
const state = {
  overview: null,
  budgets: [],
  classifications: [],
  changes: [],
  policies: [],
  identities: [],
  hierarchy: { items: [], memberships: [], root: null },
  notifications: [],
  userStates: [],
  billingExports: [],
  classificationTrend: [],
  budgetTrend: [],
  ingestions: [],
  safetyEvents: [],
  canManageOperations: false,
  operationPreview: null,
  audit: [],
};

const money = new Intl.NumberFormat(undefined, { style: "currency", currency: "USD", maximumFractionDigits: 0 });
const number = new Intl.NumberFormat();
const dateTime = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });

document.querySelectorAll(".nav-item").forEach(button => button.addEventListener("click", () => showView(button.dataset.view)));
document.querySelectorAll("[data-go-to]").forEach(button => button.addEventListener("click", () => showView(button.dataset.goTo)));
document.querySelector("#refresh-button").addEventListener("click", loadAll);
document.querySelector("#budget-scope-filter").addEventListener("change", renderBudgets);
document.querySelector("#budget-search").addEventListener("input", renderBudgets);
document.querySelector("#health-scope-filter").addEventListener("change", renderHealth);
document.querySelector("#health-status-filter").addEventListener("change", renderHealth);
document.querySelector("#hierarchy-scope-filter").addEventListener("change", renderHierarchy);
document.querySelector("#hierarchy-search").addEventListener("input", renderHierarchy);
document.querySelector("#notification-status-filter").addEventListener("change", renderNotifications);
document.querySelector("#notification-recipient-filter").addEventListener("input", renderNotifications);
document.querySelector("#user-budget-search").addEventListener("input", renderUserBudgetStates);
document.querySelector("#new-policy-button").addEventListener("click", () => document.querySelector("#policy-dialog").showModal());
document.querySelector("#new-change-button").addEventListener("click", openChangeDialog);
document.querySelectorAll("[data-close-dialog]").forEach(button => button.addEventListener("click", () => {
  const dialog = document.querySelector(`#${button.dataset.closeDialog}`);
  dialog.querySelector("form")?.reset();
  dialog.close();
}));
document.querySelector("#policy-form").addEventListener("submit", savePolicy);
document.querySelector("#change-form").addEventListener("submit", saveBudgetChange);
document.querySelector("#close-evidence-button").addEventListener("click", () => document.querySelector("#evidence-dialog").close());
document.querySelectorAll("[data-export]").forEach(button => button.addEventListener("click", () => downloadEvidenceExport(button.dataset.export)));
document.querySelector("#preview-retention-button").addEventListener("click", () => runOperationsPreview("retention"));
document.querySelector("#preview-reconciliation-button").addEventListener("click", () => runOperationsPreview("reconciliation"));

await handleAuthenticationCallback();
resolveIdentity();
await loadAll();

function showView(viewName) {
  document.querySelectorAll(".view").forEach(view => view.classList.toggle("active", view.id === `view-${viewName}`));
  document.querySelectorAll(".nav-item").forEach(button => button.classList.toggle("active", button.dataset.view === viewName));
  document.querySelector("#workspace").focus();
}

function resolveIdentity() {
  const link = document.querySelector("#sign-in-link");
  if (!authConfig.enabled) {
    link.textContent = "Development";
    link.removeAttribute("href");
    return;
  }

  const session = getAuthSession();
  if (!session?.accessToken) {
    link.textContent = "Sign in";
    link.href = "#sign-in";
    link.addEventListener("click", event => {
      event.preventDefault();
      beginSignIn();
    });
    return;
  }

  link.textContent = session.userName || "Account";
  link.href = "#sign-out";
  link.addEventListener("click", event => {
    event.preventDefault();
    signOut();
  });
}

async function loadAll() {
  setSync("Connecting", "neutral");
  try {
    const query = `enterpriseId=${encodeURIComponent(enterpriseId)}`;
    const [overview, budgets, classifications, changes, policies, identities, hierarchy, notifications, userStates, billingExports, classificationTrend, budgetTrend, ingestions, safetyHistory, audit] = await Promise.all([
      api(`/api/v1/dashboard/overview?${query}`),
      api(`/api/v1/budgets?${query}`),
      api(`/api/v1/classifications?${query}&page=1&pageSize=100`),
      api(`/api/v1/budget-change-requests?${query}&page=1&pageSize=100`),
      api(`/api/v1/policies?${query}`),
      apiOptional(`/api/v1/identity-mappings?${query}&page=1&pageSize=100`, { items: [] }),
      api(`/api/v1/hierarchy?${query}&page=1&pageSize=500`),
      apiOptional(`/api/v1/notifications?${query}&page=1&pageSize=100`, { items: [] }),
      apiOptional(`/api/v1/budget-user-states?${query}&page=1&pageSize=100`, { items: [] }),
      api(`/api/v1/operations/billing-exports?${query}&page=1&pageSize=100`),
      api(`/api/v1/trends/classifications?${query}&days=30`),
      api(`/api/v1/trends/budgets?${query}&days=30`),
      api(`/api/v1/operations/ingestions?${query}&page=1&pageSize=100`),
      apiOptional(`/api/v1/operations/safety-history?${query}&page=1&pageSize=100`, { items: [], accessDenied: true }),
      api(`/api/v1/audit?${query}&page=1&pageSize=100`),
    ]);
    Object.assign(state, {
      overview,
      budgets,
      classifications: classifications.items,
      changes: changes.items,
      policies,
      identities: identities.items,
      hierarchy,
      notifications: notifications.items,
      userStates: userStates.items,
      billingExports: billingExports.items,
      classificationTrend: classificationTrend.items,
      budgetTrend: budgetTrend.items,
      ingestions: ingestions.items,
      safetyEvents: safetyHistory.items,
      canManageOperations: !safetyHistory.accessDenied,
      audit: audit.items,
    });
    renderAll();
    setSync("Current", "green");
  } catch (error) {
    setSync("Unavailable", "red");
    toast(error.message);
  }
}

async function api(path, options) {
  const token = await getAccessToken();
  const response = await fetch(`${apiBase}${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(options?.headers || {}),
    },
  });
  if (response.status === 401 && authConfig.enabled) {
    sessionStorage.removeItem(tokenKey);
    await beginSignIn();
    throw new Error("Authentication is required.");
  }
  if (!response.ok) {
    const detail = await response.json().catch(() => null);
    const error = new Error(detail?.detail || detail?.title || `Request failed with ${response.status}`);
    error.status = response.status;
    throw error;
  }
  if (response.status === 204) return null;
  return response.json();
}

async function apiOptional(path, fallback) {
  try { return await api(path); }
  catch (error) {
    if (error.status === 403) return fallback;
    throw error;
  }
}

function renderAll() {
  renderOverview();
  renderBudgets();
  renderUserBudgetStates();
  renderHealth();
  renderHierarchy();
  renderApprovals();
  renderPolicies();
  renderIdentity();
  renderNotifications();
  renderOperations();
  renderTrends();
  renderAudit();
}

function renderOverview() {
  const item = state.overview;
  document.querySelector("#enterprise-name").textContent =
    state.hierarchy.root?.displayName || "Enterprise";
  document.querySelector("#overview-freshness").textContent = item.latestBudgetObservation ? `Budgets observed ${dateTime.format(new Date(item.latestBudgetObservation))}` : "No observations";
  const utilization = item.totalBudget ? item.consumedAmount / item.totalBudget * 100 : 0;
  const metrics = [
    ["Total budget", money.format(item.totalBudget), `${state.budgets.length} active scopes`],
    ["Consumed", money.format(item.consumedAmount), `${utilization.toFixed(1)}% utilized`],
    ["Projected spend", item.projectedSpend == null ? "Unknown" : money.format(item.projectedSpend), item.maximumForecastUtilization == null ? "Incomplete daily data" : `${Number(item.maximumForecastUtilization).toFixed(1)}% maximum utilization`],
    ["Pending approvals", number.format(item.pendingApprovals), item.pendingApprovals ? "Enterprise admin action" : "Queue is clear"],
    ["Latest ingestion", item.latestIngestion?.status || "Unknown", item.latestIngestion?.reportEndDay || "No report day"],
  ];
  document.querySelector("#overview-metrics").innerHTML = metrics.map(([label, value, detail]) => `<article class="metric-card"><span class="metric-label">${escapeHtml(label)}</span><strong>${escapeHtml(String(value))}</strong><small>${escapeHtml(String(detail))}</small></article>`).join("");
  document.querySelector("#budget-position").innerHTML = state.budgets.map(renderBudgetBar).join("") || empty("No budget observations");
  const statuses = ["green", "yellow", "red", "unknown"];
  const total = statuses.reduce((sum, status) => sum + (item.classificationCounts[status] || 0), 0) || 1;
  document.querySelector("#health-distribution").innerHTML = statuses.map(status => {
    const count = item.classificationCounts[status] || 0;
    return `<div class="status-row"><span class="status-label"><span class="status-dot ${status}"></span>${status}</span><div class="distribution-track"><div class="distribution-value ${status}" style="width:${count / total * 100}%"></div></div><strong>${count}</strong></div>`;
  }).join("");
  const attention = [
    ...state.classifications.filter(item => item.status === "red" || item.status === "unknown").map(item => ({ title: item.scopeExternalId, detail: `${item.scopeKind} is ${item.status}`, status: item.status })),
    ...state.identities.filter(item => normalizeStatus(item.status) !== "matched").map(item => ({ title: item.gitHubLogin || item.githubLogin, detail: `Identity mapping is ${item.status}`, status: "unknown" })),
  ];
  document.querySelector("#attention-count").textContent = attention.length;
  document.querySelector("#attention-list").innerHTML = attention.slice(0, 8).map(item => `<div class="attention-item"><div><strong>${escapeHtml(item.title)}</strong><span>${escapeHtml(item.detail)}</span></div>${statusBadge(item.status)}</div>`).join("") || empty("No current exceptions");
}

function renderBudgetBar(item) {
  const percent = item.budgetAmount ? item.consumedAmount / item.budgetAmount * 100 : 0;
  const tone = percent >= 95 ? "danger" : percent >= 75 ? "warning" : "";
  return `<div class="budget-bar"><div class="budget-name"><strong>${escapeHtml(item.budgetEntityName || item.budgetId)}</strong><span>${escapeHtml(item.budgetScope)}</span></div><div class="progress-track" role="progressbar" aria-label="Budget utilization" aria-valuenow="${percent.toFixed(0)}" aria-valuemin="0" aria-valuemax="100"><div class="progress-value ${tone}" style="width:${Math.min(100, percent)}%"></div></div><span class="budget-value">${money.format(item.consumedAmount)} / ${money.format(item.budgetAmount)}</span></div>`;
}

function renderBudgets() {
  const scopes = [...new Set(state.budgets.map(item => item.budgetScope))].sort();
  const select = document.querySelector("#budget-scope-filter");
  const selected = select.value;
  select.innerHTML = `<option value="">All scopes</option>${scopes.map(scope => `<option value="${escapeHtml(scope)}">${escapeHtml(scope)}</option>`).join("")}`;
  select.value = selected;
  const search = document.querySelector("#budget-search").value.toLowerCase();
  const filtered = state.budgets.filter(item => (!select.value || item.budgetScope === select.value) && (!search || `${item.budgetId} ${item.budgetEntityName || ""}`.toLowerCase().includes(search)));
  document.querySelector("#budget-table").innerHTML = filtered.map(item => {
    const percent = item.budgetAmount ? item.consumedAmount / item.budgetAmount * 100 : 0;
    return `<tr><td><strong>${escapeHtml(item.budgetEntityName || item.budgetId)}</strong>${item.isEffective ? " <span class=\"count-badge\">Effective</span>" : ""}</td><td>${escapeHtml(item.budgetScope)}</td><td>${money.format(item.consumedAmount)}</td><td>${money.format(item.budgetAmount)}</td><td>${percent.toFixed(1)}%</td><td>${item.preventFurtherUsage ? "Hard stop" : "Alert only"}</td></tr>`;
  }).join("") || tableEmpty(6, "No budgets match the filters");
}

function renderHealth() {
  const scope = normalizeStatus(document.querySelector("#health-scope-filter").value);
  const status = normalizeStatus(document.querySelector("#health-status-filter").value);
  const filtered = state.classifications.filter(item => (!scope || normalizeStatus(item.scopeKind) === scope) && (!status || normalizeStatus(item.status) === status));
  document.querySelector("#health-table").innerHTML = filtered.map(item => `<tr><td><strong>${escapeHtml(item.scopeExternalId)}</strong></td><td>${escapeHtml(item.scopeKind)}</td><td>${statusBadge(item.status)}</td><td>${item.score == null ? "—" : item.score.toFixed(1)}</td><td>${dateTime.format(new Date(item.evaluatedAt))}</td><td><button class="text-button evidence-button" type="button" data-evidence="${escapeHtml(item.id)}">View evidence</button></td></tr>`).join("") || tableEmpty(6, "No classifications match the filters");
  document.querySelectorAll("#health-table [data-evidence]").forEach(button => button.addEventListener("click", () => openClassificationEvidence(button.dataset.evidence)));
}

function renderUserBudgetStates() {
  const search = document.querySelector("#user-budget-search").value.toLowerCase();
  const filtered = state.userStates.filter(item => !search || `${item.userLogin} ${item.budgetId}`.toLowerCase().includes(search));
  document.querySelector("#user-budget-table").innerHTML = filtered.map(item => `<tr><td><strong>${escapeHtml(item.userLogin)}</strong></td><td>${escapeHtml(item.budgetId)}</td><td>${money.format(item.consumedAmount)}</td><td>${money.format(item.targetAmount)}</td><td>${escapeHtml(item.overrideBudgetId || "—")}</td><td>${dateTime.format(new Date(item.observedAt))}</td></tr>`).join("") || tableEmpty(6, state.userStates.length ? "No user budget states match the search" : "User budget states require enterprise-admin access");
}

function renderHierarchy() {
  const scope = normalizeStatus(document.querySelector("#hierarchy-scope-filter").value);
  const search = document.querySelector("#hierarchy-search").value.toLowerCase();
  const nodes = [...state.hierarchy.items];
  if (state.hierarchy.root && !nodes.some(item => normalizeStatus(item.scopeKind) === "enterprise" && item.externalId === state.hierarchy.root.externalId)) {
    nodes.unshift(state.hierarchy.root);
  }
  const labels = new Map(nodes.map(item => [`${normalizeStatus(item.scopeKind)}:${item.externalId}`, item.displayName]));
  const memberships = state.hierarchy.memberships || [];
  const filtered = nodes.filter(item => {
    const parent = item.parentExternalId || "";
    return (!scope || normalizeStatus(item.scopeKind) === scope)
      && (!search || `${item.displayName} ${item.externalId} ${parent}`.toLowerCase().includes(search));
  });
  document.querySelector("#hierarchy-table").innerHTML = filtered.map(item => {
    const parentKey = `${normalizeStatus(item.parentScopeKind)}:${item.parentExternalId}`;
    const parent = item.parentExternalId ? labels.get(parentKey) || item.parentExternalId : "—";
    const memberCount = memberships.filter(link => normalizeStatus(link.parentScopeKind) === normalizeStatus(item.scopeKind) && link.parentExternalId === item.externalId).length;
    const health = item.classification
      ? `<button class="text-button evidence-button" type="button" data-evidence="${escapeHtml(item.classification.id)}">${statusBadge(item.classification.status)}</button>`
      : "—";
    return `<tr><td><div class="entity-cell"><strong>${escapeHtml(item.displayName)}</strong><small>${escapeHtml(item.externalId)}</small></div></td><td>${escapeHtml(item.scopeKind)}</td><td>${escapeHtml(parent)}</td><td>${number.format(memberCount)}</td><td>${health}</td><td>${item.observedAt ? dateTime.format(new Date(item.observedAt)) : "—"}</td></tr>`;
  }).join("") || tableEmpty(6, "No entities match the filters");
  document.querySelectorAll("#hierarchy-table [data-evidence]").forEach(button => button.addEventListener("click", () => openClassificationEvidence(button.dataset.evidence)));
}

function renderApprovals() {
  const pending = state.changes.filter(item => normalizeStatus(item.status) === "pendingapproval");
  document.querySelector("#approval-list").innerHTML = pending.map(item => `<article class="approval-item"><div><strong>${escapeHtml(item.budgetId)}</strong><span>Created ${dateTime.format(new Date(item.createdAt))}</span></div><div><span>Current</span><strong>${money.format(item.expectedCurrentAmount)}</strong></div><div><span>Proposed</span><strong>${money.format(item.proposedAmount)}</strong></div><div><span>Forecast</span><strong>${money.format(item.forecastAmount)}</strong></div><div class="approval-actions"><button class="secondary-button" data-reject="${item.id}" data-token="${item.concurrencyToken}">Reject</button><button class="primary-button" data-approve="${item.id}" data-token="${item.concurrencyToken}">Approve</button></div></article>`).join("") || empty("No pending approvals");
  document.querySelectorAll("[data-approve]").forEach(button => button.addEventListener("click", () => decideBudgetChange(button.dataset.approve, button.dataset.token, "approve")));
  document.querySelectorAll("[data-reject]").forEach(button => button.addEventListener("click", () => decideBudgetChange(button.dataset.reject, button.dataset.token, "reject")));
}

function renderPolicies() {
  document.querySelector("#policy-table").innerHTML = state.policies.map(item => `<tr><td><strong>${escapeHtml(item.name)}</strong></td><td>${escapeHtml(item.mode)}</td><td>${escapeHtml(item.governance)}</td><td>${escapeHtml(item.scopeKind)} · ${escapeHtml(item.scopeExternalId)}</td><td>${item.version}</td><td>${item.isActive ? statusBadge("green", "Active") : statusBadge("unknown", "Inactive")}</td></tr>`).join("") || tableEmpty(6, "No policies configured");
}

function renderIdentity() {
  document.querySelector("#identity-table").innerHTML = state.identities.map(item => { const normalized = normalizeStatus(item.status); return `<tr><td><strong>${escapeHtml(item.gitHubLogin || item.githubLogin)}</strong></td><td>${escapeHtml(item.userPrincipalName || "Unmatched")}</td><td>${escapeHtml(item.department || "—")}</td><td>${escapeHtml(item.entraCostCenterCode || item.gitHubCostCenterId || item.githubCostCenterId || "—")}</td><td>${statusBadge(normalized === "matched" ? "green" : normalized === "duplicate" ? "red" : "unknown", item.status)}</td></tr>`; }).join("") || tableEmpty(5, "No identity mappings");
}

function renderNotifications() {
  const selectedStatus = normalizeStatus(document.querySelector("#notification-status-filter").value);
  const recipient = document.querySelector("#notification-recipient-filter").value.toLowerCase();
  const filtered = state.notifications.filter(item => (!selectedStatus || normalizeStatus(item.status) === selectedStatus)
    && (!recipient || item.recipientKey.toLowerCase().includes(recipient)));
  document.querySelector("#notification-table").innerHTML = filtered.map(item => `<tr><td>${dateTime.format(new Date(item.createdAt))}</td><td><strong>${escapeHtml(item.eventType || "Unknown event")}</strong><br><code>${escapeHtml(item.eventFingerprint.slice(0, 24))}</code></td><td>${escapeHtml(item.recipientKey)}</td><td>${escapeHtml(item.channel)}</td><td>${workflowStatusBadge(item.status)}</td><td>${workflowStatusBadge(item.outboxStatus || "unknown")}</td><td>${escapeHtml(item.lastErrorCode || "—")}</td></tr>`).join("") || tableEmpty(7, state.notifications.length ? "No deliveries match the filters" : "Notification delivery data requires enterprise-admin access");
}

function renderOperations() {
  document.querySelector("#operations-admin-actions").hidden = !state.canManageOperations;
  renderOperationPreview();
  document.querySelector("#operation-list").innerHTML = state.ingestions.map(item => { const status = normalizeStatus(item.status); return `<article class="operation-item"><div><strong>${escapeHtml(item.reportType)}</strong><span>${escapeHtml(item.reportStartDay)} to ${escapeHtml(item.reportEndDay)} · ${number.format(item.recordCount)} metrics</span></div>${statusBadge(status === "succeeded" ? "green" : status === "failed" ? "red" : "yellow", item.status)}</article>`; }).join("") || empty("No ingestion runs");
  document.querySelector("#safety-history-table").innerHTML = state.safetyEvents.map(item => {
    const presentation = safetyEventPresentation(item);
    return `<tr><td>${dateTime.format(new Date(item.occurredAt))}</td><td><strong>${escapeHtml(presentation.label)}</strong><br><small>${escapeHtml(item.actorId)}</small></td><td>${statusBadge(presentation.tone, presentation.state)}</td><td>${escapeHtml(presentation.detail)}</td><td><code>${escapeHtml(item.correlationId.slice(0, 12))}</code></td></tr>`;
  }).join("") || tableEmpty(5, state.canManageOperations ? "No retention or reconciliation evidence recorded" : "Safety evidence requires enterprise-admin access");
  document.querySelector("#billing-export-table").innerHTML = state.billingExports.map(item => `<tr><td>${escapeHtml(item.startDate)} to ${escapeHtml(item.endDate)}</td><td>${escapeHtml(item.reportType)}</td><td>${workflowStatusBadge(item.status)}</td><td>${number.format(item.downloadUrlCount)}</td><td>${escapeHtml(item.actor || "—")}</td><td>${dateTime.format(new Date(item.lastObservedAt))}</td></tr>`).join("") || tableEmpty(6, "No GitHub billing exports observed");
}

function renderOperationPreview() {
  const target = document.querySelector("#operation-preview-summary");
  if (!state.operationPreview) {
    target.innerHTML = "";
    return;
  }

  const { kind, result } = state.operationPreview;
  if (kind === "retention") {
    const counts = retentionPreviewCounts(result.preview);
    const total = counts.reduce((sum, item) => sum + item.value, 0);
    const outcome = result.legalHold
      ? "Legal hold is active. No records would be deleted."
      : "Dry run complete. No records were deleted.";
    target.innerHTML = previewSummary("Retention preview", `${number.format(total)} deletion candidates`, outcome, counts);
    return;
  }

  const counts = [
    { label: "Automatic", value: Number(result.automaticRequests || 0) },
    { label: "Approval", value: Number(result.approvalRequests || 0) },
    { label: "Manual drift", value: Number(result.manualDrift || 0) },
    { label: "No action", value: Number(result.noAction || 0) },
  ];
  const evaluated = Number(result.evaluated || 0);
  target.innerHTML = previewSummary(
    "Baseline reconciliation preview",
    `${number.format(evaluated)} ${evaluated === 1 ? "budget" : "budgets"} evaluated`,
    "Dry run complete. No baseline changes were applied.",
    counts);
}

function previewSummary(label, headline, outcome, counts) {
  return `<div class="operation-preview-heading"><div><span>${escapeHtml(label)}</span><strong>${escapeHtml(headline)}</strong></div><small>${escapeHtml(outcome)}</small></div><div class="preview-counts">${counts.map(item => `<div class="preview-count"><span>${escapeHtml(item.label)}</span><strong>${number.format(item.value)}</strong></div>`).join("")}</div>`;
}

function retentionPreviewCounts(preview = {}) {
  return [
    { label: "Raw reports", value: Number(preview.ingestionManifests ?? preview.IngestionManifests ?? 0) },
    { label: "User metrics", value: Number(preview.userMetrics ?? preview.UserMetrics ?? 0) },
    { label: "Aggregate metrics", value: Number(preview.aggregateMetrics ?? preview.AggregateMetrics ?? 0) },
    { label: "Classifications", value: Number(preview.classifications ?? preview.Classifications ?? 0) },
    { label: "Notifications", value: Number(preview.notifications ?? preview.Notifications ?? 0) },
    { label: "Audit events", value: Number(preview.auditEvents ?? preview.AuditEvents ?? 0) },
  ];
}

function safetyEventPresentation(item) {
  const evidence = item.evidence || {};
  if (item.eventType === "retention.applied") {
    return { label: "Retention", state: "Applied", tone: "green", detail: "Configured retention policy applied" };
  }
  if (item.eventType === "retention.previewed") {
    const counts = retentionPreviewCounts(evidence.preview || evidence.Preview);
    const candidates = counts.reduce((sum, entry) => sum + entry.value, 0);
    const legalHold = evidence.blockedByLegalHold ?? evidence.BlockedByLegalHold ?? false;
    return { label: "Retention", state: legalHold ? "Legal hold" : "Preview", tone: legalHold ? "yellow" : "unknown", detail: `${number.format(candidates)} deletion candidates` };
  }

  const decisionCount = Number(evidence.decisionCount ?? evidence.DecisionCount ?? 0);
  const previewed = item.eventType.endsWith(".previewed");
  return { label: "Baseline reconciliation", state: previewed ? "Preview" : "Evaluated", tone: previewed ? "unknown" : "green", detail: `${number.format(decisionCount)} budget decisions recorded` };
}

async function runOperationsPreview(kind) {
  const button = document.querySelector(kind === "retention" ? "#preview-retention-button" : "#preview-reconciliation-button");
  const path = kind === "retention"
    ? `/api/v1/operations/retention-preview?enterpriseId=${encodeURIComponent(enterpriseId)}`
    : `/api/v1/budget-baselines/reconciliation-preview?enterpriseId=${encodeURIComponent(enterpriseId)}`;
  button.disabled = true;
  button.setAttribute("aria-busy", "true");
  try {
    const result = await api(path, { method: "POST" });
    state.operationPreview = { kind, result };
    renderOperationPreview();
    toast(kind === "retention" ? "Retention preview recorded" : "Baseline reconciliation preview recorded");
    await loadAll();
  } catch (error) {
    toast(error.message);
  } finally {
    button.disabled = false;
    button.removeAttribute("aria-busy");
  }
}

function renderTrends() {
  const budgets = state.budgetTrend.slice(-10);
  document.querySelector("#budget-trend").innerHTML = budgets.map(item => {
    const utilization = item.budgetAmount ? Math.min(100, Number(item.consumedAmount) / Number(item.budgetAmount) * 100) : 0;
    const tone = utilization >= 95 ? "red" : utilization >= 75 ? "yellow" : "green";
    return `<div class="trend-row"><span class="trend-label">${escapeHtml(item.day)}</span><div class="trend-track"><span class="trend-segment ${tone}" style="width:${utilization}%"></span></div><span class="trend-value">${utilization.toFixed(1)}%</span></div>`;
  }).join("") || empty("No budget history in this window");

  const byDay = new Map();
  state.classificationTrend.forEach(item => {
    if (!byDay.has(item.day)) byDay.set(item.day, { green: 0, yellow: 0, red: 0, unknown: 0 });
    byDay.get(item.day)[normalizeStatus(item.status)] = item.count;
  });
  document.querySelector("#health-trend").innerHTML = [...byDay.entries()].slice(-10).map(([day, counts]) => {
    const total = Object.values(counts).reduce((sum, value) => sum + value, 0) || 1;
    const segments = ["green", "yellow", "red", "unknown"].map(status => `<span class="trend-segment ${status}" style="width:${counts[status] / total * 100}%"></span>`).join("");
    return `<div class="trend-row"><span class="trend-label">${escapeHtml(day)}</span><div class="trend-track">${segments}</div><span class="trend-value">${number.format(total)}</span></div>`;
  }).join("") || empty("No classification history in this window");
}

function renderAudit() {
  document.querySelector("#audit-table").innerHTML = state.audit.map(item => `<tr><td>${dateTime.format(new Date(item.occurredAt))}</td><td><strong>${escapeHtml(item.eventType)}</strong></td><td>${escapeHtml(item.actorId)}</td><td>${escapeHtml(item.targetType)} · ${escapeHtml(item.targetId)}</td><td><code>${escapeHtml(item.correlationId.slice(0, 12))}</code></td></tr>`).join("") || tableEmpty(5, "No audit events");
}

async function openClassificationEvidence(snapshotId) {
  const dialog = document.querySelector("#evidence-dialog");
  const content = document.querySelector("#evidence-content");
  content.innerHTML = empty("Loading evidence");
  dialog.showModal();
  try {
    const item = await api(`/api/v1/classifications/${encodeURIComponent(snapshotId)}?enterpriseId=${encodeURIComponent(enterpriseId)}`);
    document.querySelector("#evidence-dialog-title").textContent = `${item.scopeExternalId} classification`;
    const coverage = item.coverage.required
      ? `${item.coverage.available} of ${item.coverage.required} required metrics`
      : "No required metrics declared";
    const reasons = item.reasons?.length
      ? `<ul class="reason-list">${item.reasons.map(reason => `<li><strong>${escapeHtml(reason.code)}</strong><br>${escapeHtml(reason.message)}</li>`).join("")}</ul>`
      : empty("No classifier reasons were recorded");
    const observations = item.observations?.length
      ? `<div class="table-wrap"><table><thead><tr><th>Metric</th><th>Value</th><th>Availability</th><th>Direction</th><th>Age</th><th>Source</th></tr></thead><tbody>${item.observations.map(metric => `<tr><td><div class="metric-detail"><strong>${escapeHtml(metric.displayName)}</strong><small>${escapeHtml(metric.metricKey)}${metric.caveat ? ` · ${escapeHtml(metric.caveat)}` : ""}</small></div></td><td>${metric.metricValue == null ? "—" : escapeHtml(metric.metricValue)} ${escapeHtml(metric.unit || "")}</td><td>${workflowStatusBadge(metric.availability)}${metric.availabilityDetail ? `<br><small>${escapeHtml(metric.availabilityDetail)}</small>` : ""}</td><td>${escapeHtml(metric.direction || "—")}</td><td>${number.format(metric.ageDays)} days</td><td>${escapeHtml(metric.source)}</td></tr>`).join("")}</tbody></table></div>`
      : empty("No metric observations were available for this entity");
    content.innerHTML = `<div class="evidence-summary"><div class="evidence-fact"><span>Status</span>${statusBadge(item.status)}</div><div class="evidence-fact"><span>Score</span><strong>${item.score == null ? "Unknown" : Number(item.score).toFixed(1)}</strong></div><div class="evidence-fact"><span>Coverage</span><strong>${escapeHtml(coverage)}</strong></div><div class="evidence-fact"><span>Policy</span><strong>${escapeHtml(item.policy ? `${item.policy.name} v${item.policy.version}` : "Unavailable")}</strong></div></div><section class="evidence-section"><h3>Decision reasons</h3>${reasons}</section><section class="evidence-section"><h3>Metric observations</h3>${observations}</section><section class="evidence-section"><h3>Evidence fingerprint</h3><code>${escapeHtml(item.inputsFingerprint)}</code></section>`;
  } catch (error) {
    content.innerHTML = empty(error.message);
  }
}

async function downloadEvidenceExport(kind) {
  try {
    const token = await getAccessToken();
    const response = await fetch(`${apiBase}/api/v1/exports/${encodeURIComponent(kind)}.csv?enterpriseId=${encodeURIComponent(enterpriseId)}&maximumRows=50000`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    });
    if (!response.ok) {
      const detail = await response.json().catch(() => null);
      throw new Error(detail?.detail || detail?.title || `Export failed with ${response.status}`);
    }
    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = response.headers.get("Content-Disposition")?.match(/filename="?([^";]+)"?/)?.[1] || `budget-manager-${kind}.csv`;
    document.body.append(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
    toast(`Exported ${response.headers.get("X-Row-Count") || "0"} rows · SHA-256 ${response.headers.get("X-Content-SHA256")?.slice(0, 12) || "unavailable"}`);
    await loadAll();
  } catch (error) { toast(error.message); }
}

async function savePolicy(event) {
  if (event.submitter?.value === "cancel") return;
  event.preventDefault();
  const form = event.currentTarget;
  const data = new FormData(form);
  try {
    await api("/api/v1/policies", { method: "POST", body: JSON.stringify({ enterpriseId, name: data.get("name"), mode: data.get("mode"), governance: data.get("governance"), scopeKind: data.get("scopeKind"), scopeExternalId: data.get("scopeExternalId"), definition: { yellowMinimumScore: Number(data.get("yellow")), greenMinimumScore: Number(data.get("green")) } }) });
    document.querySelector("#policy-dialog").close();
    form.reset();
    toast("Policy created");
    await loadAll();
  } catch (error) { toast(error.message); }
}

function openChangeDialog() {
  const select = document.querySelector("#change-budget-id");
  select.innerHTML = state.budgets.map(item => `<option value="${escapeHtml(item.budgetId)}" data-current="${item.budgetAmount}">${escapeHtml(item.budgetEntityName || item.budgetId)} · ${money.format(item.budgetAmount)}</option>`).join("");
  document.querySelector("#change-dialog").showModal();
}

async function saveBudgetChange(event) {
  if (event.submitter?.value === "cancel") return;
  event.preventDefault();
  const form = event.currentTarget;
  const data = new FormData(form);
  try {
    await api("/api/v1/budget-change-requests", { method: "POST", body: JSON.stringify({ enterpriseId, proposal: { budgetId: data.get("budgetId"), proposedAmount: Number(data.get("proposedAmount")) }, automaticMode: false }) });
    document.querySelector("#change-dialog").close();
    form.reset();
    toast("Budget change request created");
    await loadAll();
    showView("approvals");
  } catch (error) { toast(error.message); }
}

async function decideBudgetChange(id, token, action) {
  try {
    await api(`/api/v1/budget-change-requests/${id}/${action}`, { method: "POST", body: JSON.stringify({ expectedConcurrencyToken: token }) });
    toast(`Budget change ${action === "approve" ? "approved" : "rejected"}`);
    await loadAll();
  } catch (error) { toast(error.message); }
}

function statusBadge(status, label = status) { return `<span class="status-badge ${escapeHtml(status.toLowerCase())}"><span class="status-dot ${escapeHtml(status.toLowerCase())}"></span>${escapeHtml(label)}</span>`; }
function workflowStatusBadge(status) {
  const normalized = normalizeStatus(status);
  const tone = ["sent", "processed", "succeeded", "ready", "available"].includes(normalized)
    ? "green"
    : ["failed", "deadlettered", "conflict"].includes(normalized)
      ? "red"
      : ["pending", "processing", "suppressed"].includes(normalized)
        ? "yellow"
        : "unknown";
  return statusBadge(tone, status);
}
function tableEmpty(columns, message) { return `<tr><td colspan="${columns}" class="empty-state">${escapeHtml(message)}</td></tr>`; }
function empty(message) { return `<div class="empty-state">${escapeHtml(message)}</div>`; }
function setSync(label, tone) { document.querySelector("#sync-state").innerHTML = `<span class="status-dot ${tone}"></span>${escapeHtml(label)}`; }
function toast(message) { const element = document.querySelector("#toast"); element.textContent = message; element.classList.add("visible"); window.clearTimeout(toast.timer); toast.timer = window.setTimeout(() => element.classList.remove("visible"), 3200); }
function escapeHtml(value) { return String(value ?? "").replace(/[&<>'"]/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;" })[character]); }
function normalizeStatus(value) { return String(value || "").toLowerCase(); }

async function handleAuthenticationCallback() {
  if (!authConfig.enabled) return;
  const query = new URLSearchParams(window.location.search);
  const code = query.get("code");
  if (!code) return;
  const returnedState = query.get("state");
  const expectedState = sessionStorage.getItem("budget-manager-auth-state");
  const verifier = sessionStorage.getItem("budget-manager-auth-verifier");
  if (!returnedState || returnedState !== expectedState || !verifier) {
    throw new Error("The sign-in response could not be validated.");
  }

  const redirectUri = `${window.location.origin}${window.location.pathname}`;
  const tokenResponse = await fetch(`https://login.microsoftonline.com/${encodeURIComponent(authConfig.tenantId)}/oauth2/v2.0/token`, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      client_id: authConfig.clientId,
      grant_type: "authorization_code",
      code,
      redirect_uri: redirectUri,
      code_verifier: verifier,
      scope: `openid profile offline_access ${authConfig.apiScope}`,
    }),
  });
  if (!tokenResponse.ok) throw new Error("Microsoft Entra sign-in failed during token exchange.");
  const token = await tokenResponse.json();
  const claims = parseJwt(token.id_token);
  sessionStorage.setItem(tokenKey, JSON.stringify({
    accessToken: token.access_token,
    refreshToken: token.refresh_token,
    expiresAt: Date.now() + Number(token.expires_in) * 1000,
    userName: claims?.name || claims?.preferred_username || "Account",
  }));
  sessionStorage.removeItem("budget-manager-auth-state");
  sessionStorage.removeItem("budget-manager-auth-verifier");
  window.history.replaceState({}, document.title, redirectUri);
}

async function beginSignIn() {
  validateAuthConfig();
  const verifier = base64Url(crypto.getRandomValues(new Uint8Array(64)));
  const challenge = base64Url(new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier))));
  const stateValue = base64Url(crypto.getRandomValues(new Uint8Array(24)));
  const redirectUri = `${window.location.origin}${window.location.pathname}`;
  sessionStorage.setItem("budget-manager-auth-state", stateValue);
  sessionStorage.setItem("budget-manager-auth-verifier", verifier);
  const query = new URLSearchParams({
    client_id: authConfig.clientId,
    response_type: "code",
    redirect_uri: redirectUri,
    response_mode: "query",
    scope: `openid profile offline_access ${authConfig.apiScope}`,
    state: stateValue,
    code_challenge: challenge,
    code_challenge_method: "S256",
  });
  window.location.assign(`https://login.microsoftonline.com/${encodeURIComponent(authConfig.tenantId)}/oauth2/v2.0/authorize?${query}`);
}

async function getAccessToken() {
  if (!authConfig.enabled) return null;
  validateAuthConfig();
  const session = getAuthSession();
  if (!session) {
    await beginSignIn();
    return null;
  }
  if (session.expiresAt > Date.now() + 60_000) return session.accessToken;
  if (!session.refreshToken) {
    await beginSignIn();
    return null;
  }

  const response = await fetch(`https://login.microsoftonline.com/${encodeURIComponent(authConfig.tenantId)}/oauth2/v2.0/token`, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      client_id: authConfig.clientId,
      grant_type: "refresh_token",
      refresh_token: session.refreshToken,
      scope: `openid profile offline_access ${authConfig.apiScope}`,
    }),
  });
  if (!response.ok) {
    sessionStorage.removeItem(tokenKey);
    await beginSignIn();
    return null;
  }
  const token = await response.json();
  const refreshed = {
    ...session,
    accessToken: token.access_token,
    refreshToken: token.refresh_token || session.refreshToken,
    expiresAt: Date.now() + Number(token.expires_in) * 1000,
  };
  sessionStorage.setItem(tokenKey, JSON.stringify(refreshed));
  return refreshed.accessToken;
}

function signOut() {
  sessionStorage.removeItem(tokenKey);
  const postLogout = `${window.location.origin}${window.location.pathname}`;
  window.location.assign(`https://login.microsoftonline.com/${encodeURIComponent(authConfig.tenantId)}/oauth2/v2.0/logout?post_logout_redirect_uri=${encodeURIComponent(postLogout)}`);
}

function getAuthSession() {
  try { return JSON.parse(sessionStorage.getItem(tokenKey)); } catch { return null; }
}

function validateAuthConfig() {
  if (!authConfig.tenantId || !authConfig.clientId || !authConfig.apiScope) throw new Error("Dashboard authentication configuration is incomplete.");
}

function base64Url(bytes) {
  let binary = "";
  bytes.forEach(byte => { binary += String.fromCharCode(byte); });
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
}

function parseJwt(value) {
  if (!value) return null;
  try {
    const part = value.split(".")[1].replaceAll("-", "+").replaceAll("_", "/");
    return JSON.parse(atob(part.padEnd(part.length + (4 - part.length % 4) % 4, "=")));
  } catch { return null; }
}