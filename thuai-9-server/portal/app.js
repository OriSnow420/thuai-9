const state = {
  authMode: "login",
  workspaceTab: "code",
  boardScope: "active",
  accessToken: localStorage.getItem("thuai9AccessToken") || "",
  gameToken: localStorage.getItem("thuai9GameToken") || "",
  currentUser: null,
  submissions: [],
  competitions: [],
  competitionDetail: null,
  selectedCompetitionId: null,
  adminAccounts: [],
};

const logPollers = new Map();

const els = {
  authForm: document.getElementById("authForm"),
  authSubmit: document.getElementById("authSubmit"),
  authStatus: document.getElementById("authStatus"),
  teamNameField: document.getElementById("teamNameField"),
  sessionPanel: document.getElementById("sessionPanel"),
  sessionDisplayName: document.getElementById("sessionDisplayName"),
  sessionEmail: document.getElementById("sessionEmail"),
  sessionRole: document.getElementById("sessionRole"),
  gameTokenWrap: document.getElementById("gameTokenWrap"),
  gameToken: document.getElementById("gameToken"),
  uploadForm: document.getElementById("uploadForm"),
  uploadStatus: document.getElementById("uploadStatus"),
  submissionsList: document.getElementById("submissionsList"),
  leaderboardBody: document.getElementById("leaderboardBody"),
  codeWorkspace: document.getElementById("codeWorkspace"),
  competitionsWorkspace: document.getElementById("competitionsWorkspace"),
  teamOnlyNotice: document.getElementById("teamOnlyNotice"),
  teamWorkspace: document.getElementById("teamWorkspace"),
  competitionAccessNotice: document.getElementById("competitionAccessNotice"),
  competitionWorkspace: document.getElementById("competitionWorkspace"),
  competitionsList: document.getElementById("competitionsList"),
  competitionDetail: document.getElementById("competitionDetail"),
  refreshCompetitions: document.getElementById("refreshCompetitions"),
  competitionForm: document.getElementById("competitionForm"),
  competitionFormStatus: document.getElementById("competitionFormStatus"),
  eligibleTeamsList: document.getElementById("eligibleTeamsList"),
};

document.querySelectorAll("[data-auth-tab]").forEach((button) => {
  button.addEventListener("click", () => setAuthMode(button.dataset.authTab));
});

document.querySelectorAll("[data-workspace-tab]").forEach((button) => {
  button.addEventListener("click", () => setWorkspaceTab(button.dataset.workspaceTab));
});

document.querySelectorAll("[data-board-scope]").forEach((button) => {
  button.addEventListener("click", () => setBoardScope(button.dataset.boardScope));
});

document.getElementById("logoutButton")?.addEventListener("click", logout);
document.getElementById("refreshLeaderboard")?.addEventListener("click", loadLeaderboard);
document.getElementById("refreshSubmissions")?.addEventListener("click", loadSubmissions);
els.refreshCompetitions?.addEventListener("click", async () => {
  if (state.currentUser?.role === "admin") {
    await loadAdminAccounts();
  }
  await loadCompetitions({ refreshDetail: true });
});
els.authForm?.addEventListener("submit", submitAuth);
els.uploadForm?.addEventListener("submit", submitCode);
els.competitionForm?.addEventListener("submit", submitCompetition);

bootstrap();

function canUseCodeWorkspace() {
  return state.currentUser?.role === "team" || state.currentUser?.role === "admin";
}

async function bootstrap() {
  renderSession();
  renderWorkspace();
  loadLeaderboard();
  if (!state.accessToken) {
    return;
  }
  try {
    await loadCurrentUser();
    await Promise.all([
      loadCompetitions({ refreshDetail: true }),
      canUseCodeWorkspace() ? loadSubmissions() : Promise.resolve(renderSubmissions([])),
      state.currentUser?.role === "admin" ? loadAdminAccounts() : Promise.resolve(renderAdminAccounts([])),
    ]);
  } catch (error) {
    logout(false);
    setStatus(els.authStatus, error.message, "error");
  }
}

function setAuthMode(mode) {
  state.authMode = mode === "register" ? "register" : "login";
  document.querySelectorAll("[data-auth-tab]").forEach((button) => {
    button.classList.toggle("is-active", button.dataset.authTab === state.authMode);
  });
  els.teamNameField?.classList.toggle("is-hidden", state.authMode !== "register");
  const nameInput = els.teamNameField?.querySelector("input");
  if (nameInput) {
    nameInput.required = state.authMode === "register";
  }
  els.authSubmit.textContent = state.authMode === "register" ? "注册" : "登录";
  setStatus(els.authStatus, "");
}

function setWorkspaceTab(tab) {
  const canUseCode = canUseCodeWorkspace();
  if (tab === "code" && !canUseCode) {
    state.workspaceTab = "competitions";
  } else {
    state.workspaceTab = tab === "competitions" ? "competitions" : "code";
  }
  renderWorkspace();
}

function renderWorkspace() {
  const canUseCode = canUseCodeWorkspace();
  if (!canUseCode && state.workspaceTab === "code") {
    state.workspaceTab = "competitions";
  }

  document.querySelectorAll("[data-workspace-tab]").forEach((button) => {
    const tab = button.dataset.workspaceTab;
    const enabled = tab !== "code" || canUseCode;
    button.disabled = !enabled;
    button.classList.toggle("is-active", tab === state.workspaceTab);
    button.classList.toggle("is-hidden", tab === "code" && !canUseCode && Boolean(state.currentUser));
  });

  els.codeWorkspace?.classList.toggle("is-hidden", state.workspaceTab !== "code");
  els.competitionsWorkspace?.classList.toggle("is-hidden", state.workspaceTab !== "competitions");

  const loggedIn = Boolean(state.currentUser);
  els.teamOnlyNotice?.classList.toggle("is-hidden", !loggedIn || canUseCode);
  els.teamWorkspace?.classList.toggle("is-hidden", !loggedIn || !canUseCode);

  const showCompetitionWorkspace = loggedIn;
  els.competitionAccessNotice?.classList.toggle("is-hidden", showCompetitionWorkspace);
  els.competitionWorkspace?.classList.toggle("is-hidden", !showCompetitionWorkspace);
  els.competitionForm?.classList.toggle("is-hidden", state.currentUser?.role !== "admin");
}

async function submitAuth(event) {
  event.preventDefault();
  const form = new FormData(els.authForm);
  const payload = {
    email: String(form.get("email") || "").trim(),
    password: String(form.get("password") || ""),
  };
  if (state.authMode === "register") {
    payload.name = String(form.get("name") || "").trim();
  }

  setStatus(els.authStatus, "请求中...");
  try {
    const json = await requestJson(state.authMode === "register" ? "/api/register" : "/api/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });
    state.accessToken = json.access_token;
    state.gameToken = json.game_token || "";
    localStorage.setItem("thuai9AccessToken", state.accessToken);
    localStorage.setItem("thuai9GameToken", state.gameToken);
    state.currentUser = buildCurrentUser(json);
    renderSession();
    renderWorkspace();
    setStatus(els.authStatus, "登录成功", "ok");

    if (!state.currentUser) {
      await loadCurrentUser();
    }
    if (canUseCodeWorkspace()) {
      await loadSubmissions();
    }
    if (state.currentUser?.role === "admin") {
      await loadAdminAccounts();
    } else {
      renderAdminAccounts([]);
    }
    await loadCompetitions({ refreshDetail: true });
  } catch (error) {
    setStatus(els.authStatus, error.message, "error");
  }
}

function buildCurrentUser(json) {
  if (!json || !json.role || !json.display_name || !json.email) {
    return null;
  }
  return {
    role: String(json.role),
    id: json.id ?? null,
    display_name: String(json.display_name),
    email: String(json.email),
    game_token: String(json.game_token || ""),
  };
}

async function loadCurrentUser() {
  const json = await requestJson("/api/me", {
    headers: { Authorization: `Bearer ${state.accessToken}` },
  });
  state.currentUser = {
    role: json.role,
    id: json.id ?? null,
    display_name: json.display_name,
    email: json.email,
    game_token: json.game_token || "",
  };
  state.gameToken = state.currentUser.game_token;
  localStorage.setItem("thuai9GameToken", state.gameToken);
  renderSession();
  renderWorkspace();
}

function renderSession() {
  const loggedIn = Boolean(state.accessToken && state.currentUser);
  els.sessionPanel?.classList.toggle("is-hidden", !loggedIn);
  els.sessionDisplayName.textContent = loggedIn ? state.currentUser.display_name : "-";
  els.sessionEmail.textContent = loggedIn ? state.currentUser.email : "-";
  els.sessionRole.textContent = loggedIn ? (state.currentUser.role === "admin" ? "管理员" : "队伍账号") : "-";
  els.gameToken.textContent = loggedIn ? (state.gameToken || "-") : "-";
  const showToken = loggedIn && canUseCodeWorkspace();
  els.gameTokenWrap?.classList.toggle("is-hidden", !showToken);
}

async function submitCode(event) {
  event.preventDefault();
  if (!state.accessToken || !canUseCodeWorkspace()) {
    setStatus(els.uploadStatus, "请先登录一个可提交代码的账号。", "error");
    return;
  }

  const form = new FormData(els.uploadForm);
  const file = form.get("file");
  if (!file || !file.name) {
    setStatus(els.uploadStatus, "请选择 .zip 文件。", "error");
    return;
  }
  if (!file.name.toLowerCase().endsWith(".zip")) {
    setStatus(els.uploadStatus, "只接受 .zip 文件。", "error");
    return;
  }
  if (file.size > 10 * 1024 * 1024) {
    setStatus(els.uploadStatus, "文件超过 10 MB 限制。", "error");
    return;
  }

  const rawName = String(form.get("name") || "").trim();
  if (rawName.length > 64) {
    setStatus(els.uploadStatus, "代码名称不能超过 64 个字符。", "error");
    return;
  }
  if (rawName) {
    form.set("name", rawName);
  } else {
    form.delete("name");
  }

  setStatus(els.uploadStatus, "上传中...");
  try {
    await requestJson("/api/submissions/upload", {
      method: "POST",
      headers: { Authorization: `Bearer ${state.accessToken}` },
      body: form,
    });
    els.uploadForm.reset();
    setStatus(els.uploadStatus, "上传成功，编译完成后可派遣或报名赛事。", "ok");
    await loadSubmissions();
    if (state.competitionDetail) {
      renderCompetitionDetail(state.competitionDetail);
    }
  } catch (error) {
    setStatus(els.uploadStatus, error.message, "error");
  }
}

async function loadSubmissions() {
  if (!state.accessToken || !canUseCodeWorkspace()) {
    stopAllLogPolling();
    state.submissions = [];
    renderSubmissions([]);
    return;
  }
  try {
    const submissions = await requestJson("/api/submissions/", {
      headers: { Authorization: `Bearer ${state.accessToken}` },
    });
    state.submissions = Array.isArray(submissions) ? submissions : [];
    renderSubmissions(state.submissions);
    if (state.competitionDetail) {
      renderCompetitionDetail(state.competitionDetail);
    }
  } catch (error) {
    els.submissionsList.className = "empty";
    els.submissionsList.textContent = error.message;
  }
}

function setBoardScope(scope) {
  state.boardScope = scope === "all" ? "all" : "active";
  document.querySelectorAll("[data-board-scope]").forEach((button) => {
    button.classList.toggle("is-active", button.dataset.boardScope === state.boardScope);
  });
  loadLeaderboard();
}

async function loadLeaderboard() {
  const scope = state.boardScope === "all" ? "all" : "active";
  try {
    els.leaderboardBody.innerHTML = `<tr><td colspan="7">加载中...</td></tr>`;
    const entries = await requestJson(`/api/leaderboard/?scope=${scope}`);
    renderLeaderboard(entries, scope);
  } catch (error) {
    els.leaderboardBody.innerHTML = `<tr><td colspan="7">${escapeHtml(error.message)}</td></tr>`;
  }
}

const MAX_LOG_DISPLAY = 8 * 1024;

function renderSubmissions(submissions) {
  stopAllLogPolling();
  if (!Array.isArray(submissions) || submissions.length === 0) {
    els.submissionsList.className = "empty";
    els.submissionsList.textContent = canUseCodeWorkspace() ? "暂无提交记录。" : "当前账号不能查看提交记录。";
    return;
  }

  els.submissionsList.className = "";
  els.submissionsList.innerHTML = `
    <ul class="submission-list">
      ${submissions.map((item) => {
        const canDispatch = item.status === "ready" && !item.is_dispatched;
        const dispatchLabel = item.is_dispatched
          ? "当前出战"
          : item.status === "ready"
            ? "派遣此代码"
            : "仅 ready 可派遣";
        return `
          <li data-submission-id="${item.id}">
            <div class="submission-row">
              <div class="submission-main">
                <div class="submission-title">
                  <strong>${escapeHtml(item.name || `代码 #${item.id}`)}</strong>
                  <span class="submission-id">#${item.id}</span>
                  ${item.is_dispatched ? '<span class="submission-badge is-dispatched">当前派遣</span>' : ""}
                </div>
                <div class="submission-meta">
                  <span>${formatTime(item.uploaded_at)}</span>
                  <span>${escapeHtml(item.language)}</span>
                  <span>${escapeHtml(item.status)}</span>
                </div>
              </div>
              <div class="submission-actions">
                <button
                  type="button"
                  class="link-button"
                  data-action="dispatch"
                  data-submission-id="${item.id}"
                  ${canDispatch ? "" : "disabled"}
                >
                  ${dispatchLabel}
                </button>
                <button type="button" class="link-button" data-action="toggle-logs" data-submission-id="${item.id}">查看日志</button>
                <button type="button" class="link-button" data-action="download-logs" data-submission-id="${item.id}">下载日志</button>
              </div>
            </div>
            <div class="submission-logs" data-logs-for="${item.id}" hidden></div>
          </li>
        `;
      }).join("")}
    </ul>
  `;

  els.submissionsList.querySelectorAll("[data-action='dispatch']").forEach((button) => {
    button.addEventListener("click", () => dispatchSubmission(button.dataset.submissionId));
  });
  els.submissionsList.querySelectorAll("[data-action='toggle-logs']").forEach((button) => {
    button.addEventListener("click", () => toggleLogs(button.dataset.submissionId));
  });
  els.submissionsList.querySelectorAll("[data-action='download-logs']").forEach((button) => {
    button.addEventListener("click", () => downloadSubmissionLogs(button.dataset.submissionId));
  });
}

async function dispatchSubmission(submissionId) {
  if (!state.accessToken) return;
  try {
    await requestJson(`/api/submissions/${submissionId}/dispatch`, {
      method: "POST",
      headers: { Authorization: `Bearer ${state.accessToken}` },
    });
    setStatus(els.uploadStatus, "已切换当前出战代码。", "ok");
    await loadSubmissions();
  } catch (error) {
    setStatus(els.uploadStatus, error.message, "error");
  }
}

async function toggleLogs(submissionId) {
  if (!state.accessToken) return;
  const panel = els.submissionsList.querySelector(`[data-logs-for="${submissionId}"]`);
  if (!panel) return;
  if (!panel.hidden) {
    panel.hidden = true;
    stopLogPolling(submissionId);
    return;
  }
  if (panel.dataset.loading === "1") return;
  panel.hidden = false;
  await loadSubmissionLogs(submissionId, panel);
}

async function loadSubmissionLogs(submissionId, panel) {
  stopLogPolling(submissionId);
  panel.dataset.loading = "1";
  panel.innerHTML = `<div class="logs-status">加载中…</div>`;
  try {
    const data = await requestJson(`/api/submissions/${submissionId}/logs`, {
      headers: { Authorization: `Bearer ${state.accessToken}` },
    });
    if (panel.hidden) return;
    panel.innerHTML = renderLogsBody(data);
    if (shouldPollLogs(data.status)) {
      const timerId = setTimeout(() => {
        const nextPanel = els.submissionsList.querySelector(`[data-logs-for="${submissionId}"]`);
        if (!nextPanel || nextPanel.hidden) {
          stopLogPolling(submissionId);
          return;
        }
        loadSubmissionLogs(submissionId, nextPanel);
      }, 1500);
      logPollers.set(String(submissionId), timerId);
    }
  } catch (error) {
    if (!panel.hidden) {
      panel.innerHTML = `<div class="logs-status is-error">${escapeHtml(error.message)}</div>`;
    }
  } finally {
    delete panel.dataset.loading;
  }
}

async function downloadSubmissionLogs(submissionId) {
  if (!state.accessToken) return;
  try {
    const response = await fetch(`/api/submissions/${submissionId}/logs/download`, {
      headers: { Authorization: `Bearer ${state.accessToken}` },
    });
    if (!response.ok) {
      let json = null;
      try {
        json = await response.json();
      } catch {
        json = null;
      }
      throw new Error(formatApiError(json, response.status));
    }

    const blob = await response.blob();
    const downloadUrl = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = downloadUrl;
    link.download = `submission-${submissionId}-logs.txt`;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(downloadUrl);
  } catch (error) {
    setStatus(els.uploadStatus, error.message, "error");
  }
}

function shouldPollLogs(status) {
  return status === "pending" || status === "compiling";
}

function stopLogPolling(submissionId) {
  const key = String(submissionId);
  const timerId = logPollers.get(key);
  if (timerId) {
    clearTimeout(timerId);
    logPollers.delete(key);
  }
}

function stopAllLogPolling() {
  for (const timerId of logPollers.values()) {
    clearTimeout(timerId);
  }
  logPollers.clear();
}

function renderLogsBody(data) {
  const sections = [];
  const compileLog = data.compile_log || fallbackCompileLog(data.status);
  if (compileLog) {
    sections.push(`
      <section class="log-section">
        <h4>编译输出</h4>
        <pre>${escapeHtml(truncate(compileLog))}</pre>
      </section>
    `);
  }
  if (Array.isArray(data.matches) && data.matches.length > 0) {
    sections.push(`
      <section class="log-section">
        <h4>对局日志（最近 ${data.matches.length} 场）</h4>
        ${data.matches.map((m) => `
          <article class="match-log">
            <header>
              <strong>对局 #${m.match_id}</strong>
              <span>${escapeHtml(m.status)}</span>
              <span>${formatTime(m.scheduled_at)}</span>
              ${m.score !== null && m.score !== undefined ? `<span>得分 ${escapeHtml(formatScore(m.score))}</span>` : ""}
            </header>
            <pre>${m.log ? escapeHtml(truncate(m.log)) : "（无输出）"}</pre>
          </article>
        `).join("")}
      </section>
    `);
  }
  if (sections.length === 0) {
    return `<div class="logs-status">暂无运行日志。</div>`;
  }
  return sections.join("");
}

function fallbackCompileLog(status) {
  if (status === "pending") return "等待评测器开始编译...";
  if (status === "compiling") return "编译中...";
  return "";
}

function truncate(text) {
  const str = String(text || "");
  if (str.length <= MAX_LOG_DISPLAY) return str;
  const dropped = str.length - MAX_LOG_DISPLAY;
  return `... [前 ${dropped} 字节已省略] ...\n` + str.slice(-MAX_LOG_DISPLAY);
}

async function loadAdminAccounts() {
  if (!state.accessToken || state.currentUser?.role !== "admin") {
    state.adminAccounts = [];
    renderAdminAccounts([]);
    return;
  }
  try {
    const accounts = await requestJson("/api/admin/accounts", {
      headers: { Authorization: `Bearer ${state.accessToken}` },
    });
    state.adminAccounts = Array.isArray(accounts) ? accounts : [];
    renderAdminAccounts(state.adminAccounts);
    if (state.competitionDetail) {
      renderCompetitionDetail(state.competitionDetail);
    }
  } catch (error) {
    els.eligibleTeamsList.className = "checkbox-grid empty";
    els.eligibleTeamsList.textContent = error.message;
  }
}

function renderAccountCheckboxCards(accounts, selectedIds = []) {
  const selectedSet = new Set(selectedIds);
  return accounts.map((account) => `
    <label class="checkbox-card">
      <input type="checkbox" name="eligible_team_ids" value="${account.id}" ${selectedSet.has(account.id) ? "checked" : ""}>
      <span>
        <strong>${escapeHtml(account.name)}</strong>
        <small>${escapeHtml(account.email)}</small>
      </span>
    </label>
  `).join("");
}

function renderAdminAccounts(accounts) {
  if (!els.eligibleTeamsList) return;
  if (!Array.isArray(accounts) || accounts.length === 0) {
    els.eligibleTeamsList.className = "checkbox-grid empty";
    els.eligibleTeamsList.textContent = state.currentUser?.role === "admin" ? "暂无可选账号。" : "";
    return;
  }

  els.eligibleTeamsList.className = "checkbox-grid";
  els.eligibleTeamsList.innerHTML = renderAccountCheckboxCards(accounts);
}

function collectEligibleTeamIds(formEl) {
  return new FormData(formEl)
    .getAll("eligible_team_ids")
    .map((value) => Number(value))
    .filter((value) => Number.isInteger(value));
}

async function submitCompetition(event) {
  event.preventDefault();
  if (!state.accessToken || state.currentUser?.role !== "admin") {
    setStatus(els.competitionFormStatus, "只有管理员可以创建赛事。", "error");
    return;
  }

  const form = new FormData(els.competitionForm);
  const eligibleTeamIds = collectEligibleTeamIds(els.competitionForm);
  const payload = {
    name: String(form.get("name") || "").trim(),
    description: String(form.get("description") || "").trim() || null,
    scheduled_at: toIsoFromLocalInput(String(form.get("scheduled_at") || "")),
    submission_deadline: toIsoFromLocalInput(String(form.get("submission_deadline") || ""), true),
    live_server_image: String(form.get("live_server_image") || "").trim(),
    eligible_team_ids: eligibleTeamIds,
  };

  if (!payload.scheduled_at) {
    setStatus(els.competitionFormStatus, "请填写赛事开始时间。", "error");
    return;
  }
  if (!payload.live_server_image) {
    setStatus(els.competitionFormStatus, "请填写游戏镜像。", "error");
    return;
  }
  if (eligibleTeamIds.length === 0) {
    setStatus(els.competitionFormStatus, "请至少选择一个可参赛账号。", "error");
    return;
  }

  setStatus(els.competitionFormStatus, "创建中...");
  try {
    const created = await requestJson("/api/competitions/admin", {
      method: "POST",
      headers: {
        Authorization: `Bearer ${state.accessToken}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify(payload),
    });
    els.competitionForm.reset();
    renderAdminAccounts(state.adminAccounts);
    state.selectedCompetitionId = created.id;
    setStatus(els.competitionFormStatus, "赛事已创建。", "ok");
    await loadCompetitions({ refreshDetail: true });
  } catch (error) {
    setStatus(els.competitionFormStatus, error.message, "error");
  }
}

async function loadCompetitions({ refreshDetail = false } = {}) {
  if (!state.accessToken || !state.currentUser) {
    state.competitions = [];
    state.competitionDetail = null;
    renderCompetitions([]);
    renderCompetitionDetail(null);
    return;
  }

  try {
    const competitions = await requestJson("/api/competitions/", {
      headers: { Authorization: `Bearer ${state.accessToken}` },
    });
    state.competitions = Array.isArray(competitions) ? competitions : [];

    if (state.competitions.length === 0) {
      state.selectedCompetitionId = null;
      state.competitionDetail = null;
      renderCompetitions([]);
      renderCompetitionDetail(null);
      return;
    }

    const selectedStillExists = state.competitions.some((competition) => competition.id === state.selectedCompetitionId);
    if (!selectedStillExists) {
      state.selectedCompetitionId = state.competitions[0].id;
    }

    renderCompetitions(state.competitions);
    if (refreshDetail || state.competitionDetail?.id !== state.selectedCompetitionId) {
      await loadCompetitionDetail(state.selectedCompetitionId);
    }
  } catch (error) {
    els.competitionsList.className = "empty";
    els.competitionsList.textContent = error.message;
    renderCompetitionDetail({ error: error.message });
  }
}

function renderCompetitions(competitions) {
  if (!Array.isArray(competitions) || competitions.length === 0) {
    els.competitionsList.className = "empty";
    els.competitionsList.textContent = "暂无赛事。";
    return;
  }

  els.competitionsList.className = "competition-list";
  els.competitionsList.innerHTML = competitions.map((competition) => `
    <button
      type="button"
      class="competition-item ${competition.id === state.selectedCompetitionId ? "is-active" : ""}"
      data-competition-id="${competition.id}"
    >
      <div class="competition-item-head">
        <strong>${escapeHtml(competition.name)}</strong>
        <span class="status-chip is-${escapeHtml(competition.status)}">${escapeHtml(formatCompetitionStatus(competition.status))}</span>
      </div>
      <div class="competition-item-meta">
        <span>开始 ${formatTime(competition.scheduled_at)}</span>
        <span>截止 ${formatTime(competition.effective_deadline)}</span>
        <span>报名 ${competition.participant_count}/${competition.eligible_team_count}</span>
        ${competition.is_eligible ? '<span class="competition-badge">你可参赛</span>' : ""}
      </div>
    </button>
  `).join("");

  els.competitionsList.querySelectorAll("[data-competition-id]").forEach((button) => {
    button.addEventListener("click", async () => {
      const competitionId = Number(button.dataset.competitionId);
      if (!Number.isInteger(competitionId)) return;
      state.selectedCompetitionId = competitionId;
      renderCompetitions(state.competitions);
      await loadCompetitionDetail(competitionId);
    });
  });
}

async function loadCompetitionDetail(competitionId) {
  if (!competitionId || !state.accessToken) {
    state.competitionDetail = null;
    renderCompetitionDetail(null);
    return;
  }
  try {
    const detail = await requestJson(`/api/competitions/${competitionId}`, {
      headers: { Authorization: `Bearer ${state.accessToken}` },
    });
    state.competitionDetail = detail;
    renderCompetitionDetail(detail);
  } catch (error) {
    state.competitionDetail = null;
    renderCompetitionDetail({ error: error.message });
  }
}

function renderCompetitionDetail(detail) {
  if (!detail) {
    els.competitionDetail.className = "empty";
    els.competitionDetail.textContent = "从左侧选择一个赛事查看详情。";
    return;
  }
  if (detail.error) {
    els.competitionDetail.className = "empty";
    els.competitionDetail.textContent = detail.error;
    return;
  }

  const canUseCode = canUseCodeWorkspace();
  const now = Date.now();
  const deadlineMs = detail.effective_deadline ? new Date(detail.effective_deadline).getTime() : Number.NaN;
  const beforeDeadline = Number.isFinite(deadlineMs) ? now < deadlineMs : false;
  const readySubmissions = (state.submissions || []).filter((submission) => submission.status === "ready");
  const canMutateEnrollment = canUseCode && detail.status === "scheduled" && beforeDeadline && detail.is_eligible;
  const canManageEligibility = state.currentUser?.role === "admin";

  const enrollSection = !canUseCode
    ? ""
    : !detail.is_eligible
      ? `<div class="empty compact">你不在该赛事的可参赛账号名单中。</div>`
      : `
        <section class="detail-card">
          <h4>报名状态</h4>
          <p class="hint">截止时间：${formatTime(detail.effective_deadline)}</p>
          ${canMutateEnrollment ? renderEnrollmentForm(detail, readySubmissions) : renderEnrollmentReadonly(detail, readySubmissions.length)}
        </section>
      `;
  const adminEligibilitySection = !canManageEligibility
    ? ""
    : `
      <section class="detail-card">
        <h4>编辑可参赛账号</h4>
        ${detail.status === "scheduled"
          ? renderCompetitionEligibilityEditor(detail)
          : `<div class="empty compact">赛事已进入 ${escapeHtml(formatCompetitionStatus(detail.status))} 阶段，不再支持修改可参赛名单。</div>`}
      </section>
    `;

  els.competitionDetail.className = "competition-detail";
  els.competitionDetail.innerHTML = `
    <section class="detail-card">
      <div class="detail-head">
        <div>
          <h3>${escapeHtml(detail.name)}</h3>
          <p class="hint">创建者：${escapeHtml(detail.created_by_name)} (${escapeHtml(detail.created_by_email)})</p>
        </div>
        <span class="status-chip is-${escapeHtml(detail.status)}">${escapeHtml(formatCompetitionStatus(detail.status))}</span>
      </div>
      <div class="detail-meta-grid">
        <div><span>开始时间</span><strong>${formatTime(detail.scheduled_at)}</strong></div>
        <div><span>报名截止</span><strong>${formatTime(detail.effective_deadline)}</strong></div>
        <div><span>游戏镜像</span><code>${escapeHtml(detail.live_server_image)}</code></div>
        <div><span>报名数</span><strong>${detail.participant_count} / ${detail.eligible_team_count}</strong></div>
      </div>
      ${detail.description ? `<p class="detail-description">${escapeHtml(detail.description)}</p>` : ""}
      ${detail.error_log ? `<pre class="detail-error">${escapeHtml(detail.error_log)}</pre>` : ""}
    </section>
    ${enrollSection}
    ${adminEligibilitySection}
    <section class="detail-card">
      <h4>参赛账号</h4>
      ${renderCompetitionSlots(detail.slots)}
    </section>
  `;

  const enrollForm = els.competitionDetail.querySelector("#competitionEnrollForm");
  if (enrollForm) {
    enrollForm.addEventListener("submit", (event) => submitCompetitionEnrollment(event, detail.id));
  }
  const withdrawButton = els.competitionDetail.querySelector("#competitionWithdrawButton");
  if (withdrawButton) {
    withdrawButton.addEventListener("click", () => withdrawCompetition(detail.id));
  }
  const eligibilityForm = els.competitionDetail.querySelector("#competitionEligibilityForm");
  if (eligibilityForm) {
    eligibilityForm.addEventListener("submit", (event) => submitCompetitionEligibilityUpdate(event, detail.id));
  }
}

function renderEnrollmentForm(detail, readySubmissions) {
  return `
    <form id="competitionEnrollForm" class="stack compact-stack">
      <label class="field">
        <span>选择参赛代码</span>
        <select name="submission_id" ${readySubmissions.length ? "" : "disabled"}>
          ${readySubmissions.length === 0
            ? '<option value="">当前没有 ready 状态的代码</option>'
            : readySubmissions.map((submission) => `
                <option value="${submission.id}" ${submission.id === detail.current_submission_id ? "selected" : ""}>
                  ${escapeHtml(submission.name || `代码 #${submission.id}`)} (#${submission.id})
                </option>
              `).join("")}
        </select>
      </label>
      <div class="inline-actions">
        <button class="primary" type="submit" ${readySubmissions.length === 0 ? "disabled" : ""}>保存参赛代码</button>
        <button id="competitionWithdrawButton" type="button">退出赛事</button>
      </div>
      <p id="competitionEnrollStatus" class="status" role="status"></p>
    </form>
  `;
}

function renderEnrollmentReadonly(detail, readyCount) {
  if (detail.status !== "scheduled") {
    return `<div class="empty compact">赛事已进入 ${escapeHtml(formatCompetitionStatus(detail.status))} 阶段，报名已锁定。</div>`;
  }
  if (!readyCount) {
    return `<div class="empty compact">你当前没有 ready 状态的代码可报名。</div>`;
  }
  return `<div class="empty compact">赛事报名已截止，当前参赛代码：${detail.current_submission_id ? `#${detail.current_submission_id}` : "未报名"}</div>`;
}

function renderCompetitionEligibilityEditor(detail) {
  if (!Array.isArray(state.adminAccounts) || state.adminAccounts.length === 0) {
    return '<div class="empty compact">暂无可选账号。</div>';
  }

  const selectedIds = Array.isArray(detail.slots)
    ? detail.slots.map((slot) => slot.team_id)
    : [];

  return `
    <form id="competitionEligibilityForm" class="stack compact-stack">
      <p class="hint">取消勾选会移除对应队伍的参赛资格和已保存的报名代码。</p>
      <div class="checkbox-grid">
        ${renderAccountCheckboxCards(state.adminAccounts, selectedIds)}
      </div>
      <div class="inline-actions">
        <button class="primary" type="submit">保存可参赛列表</button>
      </div>
      <p id="competitionEligibilityStatus" class="status" role="status"></p>
    </form>
  `;
}

function renderCompetitionSlots(slots) {
  if (!Array.isArray(slots) || slots.length === 0) {
    return '<div class="empty compact">暂无可参赛账号。</div>';
  }

  return `
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>队伍</th>
            <th>参赛代码</th>
            <th>状态</th>
            <th>得分</th>
            <th>更新时间</th>
          </tr>
        </thead>
        <tbody>
          ${slots.map((slot) => `
            <tr>
              <td>${escapeHtml(slot.team_name)}</td>
              <td>${slot.selected_submission_id ? `${escapeHtml(slot.selected_submission_name || `代码 #${slot.selected_submission_id}`)} (#${slot.selected_submission_id})` : "未报名"}</td>
              <td>${escapeHtml(slot.selected_submission_status || "-")}</td>
              <td>${slot.score ? escapeHtml(formatScore(slot.score)) : "-"}</td>
              <td>${formatTime(slot.updated_at)}</td>
            </tr>
          `).join("")}
        </tbody>
      </table>
    </div>
  `;
}

async function submitCompetitionEnrollment(event, competitionId) {
  event.preventDefault();
  const form = new FormData(event.currentTarget);
  const submissionId = Number(form.get("submission_id"));
  const statusEl = document.getElementById("competitionEnrollStatus");
  if (!Number.isInteger(submissionId)) {
    setStatus(statusEl, "请选择一个 ready 状态的代码。", "error");
    return;
  }

  setStatus(statusEl, "保存中...");
  try {
    const detail = await requestJson(`/api/competitions/${competitionId}/enroll`, {
      method: "POST",
      headers: {
        Authorization: `Bearer ${state.accessToken}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ submission_id: submissionId }),
    });
    state.competitionDetail = detail;
    setStatus(statusEl, "报名已更新。", "ok");
    await loadCompetitions({ refreshDetail: false });
    renderCompetitionDetail(detail);
  } catch (error) {
    setStatus(statusEl, error.message, "error");
  }
}

async function withdrawCompetition(competitionId) {
  const statusEl = document.getElementById("competitionEnrollStatus");
  setStatus(statusEl, "退出中...");
  try {
    const detail = await requestJson(`/api/competitions/${competitionId}/enroll`, {
      method: "DELETE",
      headers: {
        Authorization: `Bearer ${state.accessToken}`,
      },
    });
    state.competitionDetail = detail;
    setStatus(statusEl, "已退出赛事。", "ok");
    await loadCompetitions({ refreshDetail: false });
    renderCompetitionDetail(detail);
  } catch (error) {
    setStatus(statusEl, error.message, "error");
  }
}

async function submitCompetitionEligibilityUpdate(event, competitionId) {
  event.preventDefault();
  if (!state.accessToken || state.currentUser?.role !== "admin") {
    setStatus(document.getElementById("competitionEligibilityStatus"), "只有管理员可以修改可参赛列表。", "error");
    return;
  }

  const formEl = event.currentTarget;
  const statusEl = document.getElementById("competitionEligibilityStatus");
  const eligibleTeamIds = collectEligibleTeamIds(formEl);
  if (eligibleTeamIds.length === 0) {
    setStatus(statusEl, "请至少选择一个可参赛账号。", "error");
    return;
  }

  setStatus(statusEl, "保存中...");
  try {
    const detail = await requestJson(`/api/competitions/admin/${competitionId}/eligible-teams`, {
      method: "PUT",
      headers: {
        Authorization: `Bearer ${state.accessToken}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ eligible_team_ids: eligibleTeamIds }),
    });
    state.competitionDetail = detail;
    await loadCompetitions({ refreshDetail: false });
    renderCompetitionDetail(detail);
    setStatus(document.getElementById("competitionEligibilityStatus"), "可参赛列表已更新。", "ok");
  } catch (error) {
    setStatus(statusEl, error.message, "error");
  }
}

function renderLeaderboard(entries, scope = "active") {
  if (!Array.isArray(entries) || entries.length === 0) {
    const emptyMsg = scope === "active"
      ? "暂无出战中的代码对战数据。"
      : "暂无对战数据。";
    els.leaderboardBody.innerHTML = `<tr><td colspan="7">${emptyMsg}</td></tr>`;
    return;
  }

  els.leaderboardBody.innerHTML = entries.map((entry, index) => {
    return `
      <tr>
        <td>${index + 1}</td>
        <td>
          <strong>${escapeHtml(entry.submission_name || `代码 #${entry.submission_id}`)}</strong>
          <span class="submission-id">#${entry.submission_id}</span>
        </td>
        <td>${escapeHtml(entry.team_name)}</td>
        <td>${escapeHtml(formatScore(entry.total_score))}</td>
        <td>${escapeHtml(formatScore(entry.average_score))}</td>
        <td>${entry.best_score ? escapeHtml(formatScore(entry.best_score)) : "-"}</td>
        <td>${entry.total_matches}</td>
      </tr>
    `;
  }).join("");
}

function logout(clearStatus = true) {
  stopAllLogPolling();
  state.accessToken = "";
  state.gameToken = "";
  state.currentUser = null;
  state.submissions = [];
  state.competitions = [];
  state.competitionDetail = null;
  state.selectedCompetitionId = null;
  state.adminAccounts = [];
  localStorage.removeItem("thuai9AccessToken");
  localStorage.removeItem("thuai9GameToken");
  if (clearStatus) {
    setStatus(els.authStatus, "已退出。", "ok");
  }
  setStatus(els.uploadStatus, "");
  setStatus(els.competitionFormStatus, "");
  renderSession();
  renderWorkspace();
  renderSubmissions([]);
  renderAdminAccounts([]);
  renderCompetitions([]);
  renderCompetitionDetail(null);
}

async function requestJson(url, options = {}) {
  const headers = { ...(options.headers || {}) };
  if (state.accessToken && !headers.Authorization && url.startsWith("/api/")) {
    headers.Authorization = `Bearer ${state.accessToken}`;
  }
  const res = await fetch(url, { ...options, headers });
  let json = null;
  try {
    json = await res.json();
  } catch {
    json = null;
  }
  if (!res.ok) {
    throw new Error(formatApiError(json, res.status));
  }
  return json;
}

function formatApiError(json, status) {
  const detail = json?.detail;
  if (typeof detail === "string") {
    return detail;
  }
  if (Array.isArray(detail)) {
    return detail.map(formatValidationError).join("；");
  }
  if (detail && typeof detail === "object") {
    return detail.msg || JSON.stringify(detail);
  }
  return `请求失败 (${status})`;
}

function formatValidationError(error) {
  if (!error || typeof error !== "object") {
    return String(error);
  }
  const field = Array.isArray(error.loc) ? error.loc.filter((part) => part !== "body").join(".") : "";
  const message = error.msg || "输入不合法";
  return field ? `${field}: ${message}` : message;
}

function setStatus(el, message, type = "") {
  if (!el) return;
  el.textContent = message;
  el.classList.toggle("is-error", type === "error");
  el.classList.toggle("is-ok", type === "ok");
}

function formatTime(value) {
  if (!value) return "-";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString("zh-CN", { hour12: false });
}

function formatScore(value) {
  if (value === null || value === undefined || value === "") return "-";
  return String(value);
}

function formatCompetitionStatus(status) {
  switch (status) {
    case "scheduled":
      return "待开始";
    case "running":
      return "进行中";
    case "finished":
      return "已完成";
    case "error":
      return "异常";
    default:
      return status || "未知";
  }
}

function toIsoFromLocalInput(value, allowBlank = false) {
  const text = String(value || "").trim();
  if (!text) {
    return allowBlank ? null : "";
  }
  const date = new Date(text);
  if (Number.isNaN(date.getTime())) {
    return allowBlank ? null : "";
  }
  return date.toISOString();
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}