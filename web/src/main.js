import {
  activateSkillMessage,
  cancelOrderMessage,
  debugAdvanceStageMessage,
  debugGiveCardMessage,
  debugInjectNewsMessage,
  debugQueryMessage,
  debugSetPlayerMessage,
  helloMessage,
  limitBuyMessage,
  limitSellMessage,
  selectStrategyMessage,
  sendJson,
  submitReportMessage,
} from "./actions.js";
import { buildSampleMessages } from "./sample-data.js";
import { buildReplaySession } from "./replay.js";
import { describeObserverConnection, normalizeObserverMatch } from "./observer-status.js";
import { parseSocketMessageData, previewSocketPayload } from "./socket-message.js";
import {
  applyMessage,
  applyManagedObserverLiveState,
  applyPlayerMap,
  clearSettlement,
  createInitialState,
  markNewsAsRead,
  playerDisplayName,
  pushEvent,
  resetRuntimeState,
  resetUiCollections,
  routeFromLocation,
  setActiveView,
  setCandleOptions,
  setColorScheme,
  setConnectionPatch,
  setMode,
  setReplayPatch,
} from "./store.js";
import { buildServerUrl, normalizeLocalhostPort, openWebSocket, parseServerChoice } from "./connection.js";
import {
  handleMarketChartPointerDown,
  handleMarketChartPointerMove,
  handleMarketChartPointerUp,
  handleMarketChartWheel,
  renderApp,
  resetMarketChartViewport,
} from "./render.js";
import { applyColorScheme, loadColorScheme, saveColorScheme } from "./appearance.js";

const state = createInitialState(routeFromLocation(window.location));
setColorScheme(state, loadColorScheme());
applyColorScheme(state.ui.colorScheme);
let ws = null;
let reconnectTimer = null;
let manuallyClosed = false;
let marketCanvasHandlersBound = false;
let replaySession = null;
let replayTimer = null;
let appliedReplayIndex = -1;
let inboundMessageChain = Promise.resolve();
const PLAYER_MAP_POLL_MS = 15000;
let playerMapTimer = null;
// Tracks which match the accumulated live runtime state belongs to, so a new
// match (the evaluator restarts the game server per match) clears the previous
// match's candles/events/summaries before the new stream is applied.
let observedMatchId = null;
// These anchors must be initialized before any auto-connect path can receive
// GAME_STATE messages, otherwise managed spectator pages can hit a TDZ error
// during startup.
let lastLiveMatchMonth = null;
let lastLiveMatchTick = null;

initParticles();
bindControls();
bindMarketChartControls();
renderApp(state);
if (isPublicSpectatorPage()) {
  setMode(state, "observer");
  setConnectionPatch(state, { server: buildServerUrl("nginx", state.connection.localhostPort) });
  connect();
  startPlayerMapPolling();
}

function bindControls() {
  document.getElementById("modeTabs")?.addEventListener("click", (event) => {
    const button = event.target.closest("[data-mode]");
    if (!button) return;
    setMode(state, button.dataset.mode);
    updateRoute();
    renderApp(state);
  });

  document.querySelector(".menu-panel")?.addEventListener("click", (event) => {
    const button = event.target.closest("[data-view]");
    if (!button) return;
    if (state.connection.role !== "player" && (button.dataset.view === "info" || button.dataset.view === "debug")) {
      return;
    }
    if (button.dataset.view === "server-debug" && state.connection.role !== "admin") {
      return;
    }
    setActiveView(state, button.dataset.view);
    renderApp(state);
  });

  document.getElementById("dailySummary")?.addEventListener("pointerdown", handleSummaryInteraction, true);
  document.getElementById("dailySummary")?.addEventListener("click", handleSummaryInteraction, true);

  document.body.addEventListener("click", (event) => {
    const openButton = event.target.closest("[data-open-modal]");
    if (openButton) {
      if (openButton.dataset.openModal === "newsModal") {
        markNewsAsRead(state);
      }
      openModal(openButton.dataset.openModal);
      renderApp(state);
      return;
    }

    const closeButton = event.target.closest("[data-close-modal]");
    if (closeButton) {
      closeModal(closeButton.dataset.closeModal);
      return;
    }

    const playerButton = event.target.closest("[data-player-id]");
    if (playerButton) {
      showPlayerDetail(Number(playerButton.dataset.playerId));
      return;
    }

    const playerKeyButton = event.target.closest("[data-player-key]");
    if (playerKeyButton) {
      showPlayerDetail(playerKeyButton.dataset.playerKey);
    }
  });

  document.getElementById("serverPresetSelect")?.addEventListener("change", (event) => {
    const mode = event.target.value === "remote" ? "remote" : "localhost";
    setConnectionPatch(state, {
      server: buildServerUrl(mode, state.connection.localhostPort),
    });
    updateRoute();
    renderApp(state);
  });

  document.getElementById("localhostPortInput")?.addEventListener("change", (event) => {
    const localhostPort = normalizeLocalhostPort(event.target.value);
    const mode = currentServerMode();
    setConnectionPatch(state, {
      localhostPort,
      server: buildServerUrl(mode, localhostPort),
    });
    updateRoute();
    renderApp(state);
  });

  document.getElementById("tokenInput")?.addEventListener("change", (event) => {
    setConnectionPatch(state, { token: event.target.value.trim() || "player1" });
    updateRoute();
    renderApp(state);
  });

  document.getElementById("adminSecretInput")?.addEventListener("change", (event) => {
    setConnectionPatch(state, { adminSecret: event.target.value.trim() });
    updateRoute();
    renderApp(state);
  });

  document.getElementById("connectButton")?.addEventListener("click", connect);
  document.getElementById("disconnectButton")?.addEventListener("click", () => disconnect(true));
  document.getElementById("demoButton")?.addEventListener("click", loadDemo);
  document.getElementById("replayLoadButton")?.addEventListener("click", handleReplayLoad);
  document.getElementById("replayPlayButton")?.addEventListener("click", toggleReplayPlayback);
  document.getElementById("replayPrevButton")?.addEventListener("click", () => seekReplayFrame(state.replay.frameIndex - 1));
  document.getElementById("replayNextButton")?.addEventListener("click", () => seekReplayFrame(state.replay.frameIndex + 1));
  document.getElementById("replaySlider")?.addEventListener("input", (event) => {
    seekReplayFrame(Number(event.target.value));
  });
  document.getElementById("replaySpeedSelect")?.addEventListener("change", (event) => {
    setReplayPatch(state, { speed: Number(event.target.value) || 1 });
    if (state.replay.playing) {
      startReplayTimer();
    }
    renderApp(state);
  });

  document.getElementById("priceModeSelect")?.addEventListener("change", (event) => {
    setCandleOptions(state, { priceField: event.target.value });
    resetMarketChartViewport();
    renderApp(state);
  });

  document.getElementById("intervalSelect")?.addEventListener("change", (event) => {
    setCandleOptions(state, { interval: event.target.value });
    resetMarketChartViewport();
    renderApp(state);
  });

  document.getElementById("colorSchemeSelect")?.addEventListener("change", (event) => {
    setColorScheme(state, event.target.value);
    saveColorScheme(state.ui.colorScheme);
    renderApp(state);
  });

  document.getElementById("orderForm")?.addEventListener("submit", handleOrder);
  document.getElementById("cancelForm")?.addEventListener("submit", handleCancel);
  document.getElementById("reportForm")?.addEventListener("submit", handleReport);
  document.getElementById("quickReportForm")?.addEventListener("submit", handleQuickReport);
  document.getElementById("skillForm")?.addEventListener("submit", handleSkill);
  document.getElementById("debugQueryForm")?.addEventListener("submit", handleDebugQuery);
  document.getElementById("debugAdvanceForm")?.addEventListener("submit", handleDebugAdvance);
  document.getElementById("debugGiveCardForm")?.addEventListener("submit", handleDebugGiveCard);
  document.getElementById("debugInjectNewsForm")?.addEventListener("submit", handleDebugInjectNews);
  document.getElementById("debugSetPlayerForm")?.addEventListener("submit", handleDebugSetPlayer);
  document.getElementById("serverRawForm")?.addEventListener("submit", handleServerRaw);

  document.getElementById("strategyOptions")?.addEventListener("click", (event) => {
    const button = event.target.closest("[data-action='select-strategy']");
    if (!button) return;
    sendAction(selectStrategyMessage(state.connection.token, button.dataset.cardName));
  });

  document.getElementById("closeSettlementButton")?.addEventListener("click", () => {
    clearSettlement(state);
    renderApp(state);
  });

  window.addEventListener("resize", () => renderApp(state));
}

function bindMarketChartControls() {
  if (marketCanvasHandlersBound) return;
  const canvas = document.getElementById("marketCanvas");
  if (!canvas) return;
  marketCanvasHandlersBound = true;

  canvas.addEventListener("pointerdown", (event) => {
    handleMarketChartPointerDown(canvas, event, state);
  });
  canvas.addEventListener("pointermove", (event) => {
    if (handleMarketChartPointerMove(canvas, event, state)) {
      renderApp(state);
    }
  });
  canvas.addEventListener("pointerup", (event) => {
    handleMarketChartPointerUp(canvas, event);
    renderApp(state);
  });
  canvas.addEventListener("pointercancel", (event) => {
    handleMarketChartPointerUp(canvas, event);
    renderApp(state);
  });
  canvas.addEventListener("wheel", (event) => {
    if (handleMarketChartWheel(canvas, event, state)) {
      renderApp(state);
    }
  }, { passive: false });
}

function handleSummaryInteraction(event) {
  const button = event.target.closest("[data-action='open-summary']");
  if (!button) return;
  if (event.type === "pointerdown") {
    event.preventDefault();
  }
  showSummaryDetail(button.dataset.summaryDay);
}

function currentServerMode() {
  const value = document.getElementById("serverPresetSelect")?.value;
  if (value === "remote" || value === "nginx") {
    return value;
  }
  if (value === "localhost") {
    return "localhost";
  }
  return parseServerChoice(state.connection.server).mode;
}

function isPublicSpectatorPage() {
  return window.location.pathname.startsWith("/spectator/");
}

function isManagedObserverSession() {
  return isPublicSpectatorPage() && state.connection.role === "observer" && !state.replay.enabled;
}

function syncManagedObserverConnection(options = {}) {
  if (!isManagedObserverSession()) return;
  const description = describeObserverConnection(
    {
      matchId: state.connection.currentMatchId,
      matchStatus: state.connection.currentMatchStatus,
    },
    {
      socketConnected: Boolean(options.socketConnected),
      manuallyDisconnected: Boolean(options.manuallyDisconnected),
      reconnectAttempt: state.connection.reconnectAttempt,
    },
  );
  setConnectionPatch(state, description);
}

function applyManagedObserverMatch(payload) {
  if (!isManagedObserverSession()) return;
  const match = normalizeObserverMatch(payload);
  if (match.matchId !== null && observedMatchId !== null && match.matchId !== observedMatchId) {
    resetLiveRuntimeState();
  }
  if (match.matchId !== null) {
    observedMatchId = match.matchId;
  }
  setConnectionPatch(state, {
    currentMatchId: match.matchId,
    currentMatchStatus: match.matchStatus,
  });
  syncManagedObserverConnection({
    socketConnected: state.connection.status === "connected",
  });
}

function resetLiveRuntimeState() {
  resetRuntimeState(state);
  resetMarketChartViewport();
  // Clear the match anchor too: the GAME_STATE-driven reset can run before the
  // player-map poll learns the new id, so the next poll should adopt that id
  // without triggering a second wipe of the freshly-started match's data.
  observedMatchId = null;
  lastLiveMatchMonth = null;
  lastLiveMatchTick = null;
}

function maybeResetForNewLiveMatch(message) {
  if (!isManagedObserverSession()) return;
  if (!message || message.messageType !== "GAME_STATE") return;
  const month = Number(message.currentMonth);
  const tick = Number(message.currentTick);
  if (!Number.isFinite(month) || !Number.isFinite(tick)) return;
  const wentBackward =
    lastLiveMatchMonth !== null &&
    (month < lastLiveMatchMonth || (month === lastLiveMatchMonth && tick < lastLiveMatchTick));
  if (wentBackward) {
    resetLiveRuntimeState();
  }
  lastLiveMatchMonth = month;
  lastLiveMatchTick = tick;
}

async function handleSocketMessage(event) {
  let parsed;
  try {
    parsed = await parseSocketMessageData(event.data);
  } catch (error) {
    console.error("Failed to parse WebSocket message", error, event.data);
    pushEvent(state, {
      kind: "error",
      title: "消息解析失败",
      detail: formatSocketParseError(error, event.data),
    });
    renderApp(state);
    return;
  }

  try {
    maybeResetForNewLiveMatch(parsed.message);
    applyMessage(state, parsed.message);
  } catch (error) {
    console.error("Failed to apply WebSocket message", error, parsed.message);
    pushEvent(state, {
      kind: "error",
      title: "消息处理失败",
      detail: formatSocketProcessingError(parsed, error),
    });
  }

  renderApp(state);
}

function queueSocketMessage(event) {
  inboundMessageChain = inboundMessageChain
    .then(() => handleSocketMessage(event))
    .catch((error) => {
      console.error("Unexpected socket message pipeline failure", error);
    });
}

function formatSocketParseError(error, payload) {
  const detail = [error?.message || "unknown error"];
  const preview = previewSocketPayload(payload);
  if (preview) {
    detail.push(`payload=${preview}`);
  }
  return detail.join(" | ");
}

function formatSocketProcessingError(parsed, error) {
  const detail = [
    `messageType=${parsed?.message?.messageType || "-"}`,
    error?.message || "unknown error",
  ];
  const preview = previewSocketPayload(parsed?.raw);
  if (preview) {
    detail.push(`payload=${preview}`);
  }
  return detail.join(" | ");
}

async function fetchPlayerMap() {
  try {
    const liveStateResponse = await fetch("/api/matches/current/live-state", { credentials: "omit" });
    if (liveStateResponse.ok) {
      const liveState = await liveStateResponse.json();
      applyManagedObserverLiveState(state, liveState);
      applyManagedObserverMatch(liveState);
      renderApp(state);
      return;
    }

    const res = await fetch("/api/matches/current/player-map", { credentials: "omit" });
    if (!res.ok) return;
    const data = await res.json();
    applyPlayerMap(state, Array.isArray(data?.players) ? data.players : []);
    applyManagedObserverMatch(data);
    renderApp(state);
  } catch {
    // Silent — the spectator UI falls back to "选手 N" labels if the API is down.
  }
}

function startPlayerMapPolling() {
  if (playerMapTimer) return;
  fetchPlayerMap();
  playerMapTimer = window.setInterval(fetchPlayerMap, PLAYER_MAP_POLL_MS);
}

function connect() {
  stopReplayPlayback();
  replaySession = null;
  appliedReplayIndex = -1;
  inboundMessageChain = Promise.resolve();
  setReplayPatch(state, {
    enabled: false,
    loaded: false,
    playing: false,
    frameIndex: 0,
    frameCount: 0,
    label: "",
    error: "",
    hasStats: false,
    statEventCount: 0,
  });
  clearTimeout(reconnectTimer);
  manuallyClosed = false;

  if (ws && ws.readyState <= 1) {
    ws.close();
  }

  const localhostPort = normalizeLocalhostPort(
    document.getElementById("localhostPortInput")?.value || state.connection.localhostPort,
  );
  const server = buildServerUrl(currentServerMode(), localhostPort);
  const token = document.getElementById("tokenInput")?.value.trim() || state.connection.token;
  const adminSecret = document.getElementById("adminSecretInput")?.value.trim() || state.connection.adminSecret;
  const connectionPatch = {
    server,
    localhostPort,
    token,
    adminSecret,
    status: "connecting",
    lastError: "",
    statusLabel: "",
    statusDetail: "",
  };
  setConnectionPatch(state, connectionPatch);
  if (isManagedObserverSession()) {
    syncManagedObserverConnection({ socketConnected: false });
  }
  updateRoute();
  renderApp(state);

  try {
    ws = openWebSocket(server);
  } catch (error) {
    handleSocketError(error);
    return;
  }

  ws.addEventListener("open", () => {
    setConnectionPatch(state, {
      status: "connected",
      reconnectAttempt: 0,
      lastError: "",
    });
    syncManagedObserverConnection({ socketConnected: true });
    sendAction(
      helloMessage(state.connection.role, state.connection.token, state.connection.adminSecret),
      { silent: true },
    );
    if (state.connection.role === "player" && state.connection.token) {
      sendAction(cancelOrderMessage(state.connection.token, -1), { silent: true });
    }
    renderApp(state);
  });

  ws.addEventListener("message", queueSocketMessage);

  ws.addEventListener("error", () => {
    handleSocketError(new Error("WebSocket error"));
  });

  ws.addEventListener("close", () => {
    ws = null;
    if (state.replay.enabled) {
      renderApp(state);
      return;
    }
    if (isManagedObserverSession()) {
      syncManagedObserverConnection({
        socketConnected: false,
        manuallyDisconnected: manuallyClosed,
      });
    } else {
      const nextStatus = manuallyClosed ? "disconnected" : "error";
      setConnectionPatch(state, { status: nextStatus });
    }
    renderApp(state);
    if (!manuallyClosed) scheduleReconnect();
  });
}

function disconnect(byUser) {
  stopReplayPlayback();
  manuallyClosed = byUser;
  clearTimeout(reconnectTimer);
  if (ws && ws.readyState <= 1) {
    ws.close();
  }
  setConnectionPatch(state, { status: "disconnected" });
  if (isManagedObserverSession()) {
    syncManagedObserverConnection({
      socketConnected: false,
      manuallyDisconnected: true,
    });
  }
  renderApp(state);
}

function scheduleReconnect() {
  const attempt = state.connection.reconnectAttempt + 1;
  const delay = Math.min(10000, 500 * 2 ** Math.min(attempt, 5));
  setConnectionPatch(state, { reconnectAttempt: attempt });
  if (isManagedObserverSession()) {
    syncManagedObserverConnection({ socketConnected: false });
    renderApp(state);
  }
  reconnectTimer = window.setTimeout(connect, delay);
}

function handleSocketError(error) {
  if (isManagedObserverSession()) {
    setConnectionPatch(state, {
      lastError: error.message,
    });
    syncManagedObserverConnection({ socketConnected: false });
    renderApp(state);
    return;
  }
  setConnectionPatch(state, {
    status: "error",
    lastError: error.message,
  });
  pushEvent(state, {
    kind: "error",
    title: "连接错误",
    detail: error.message,
  });
  renderApp(state);
}

function sendAction(message, options = {}) {
  try {
    sendJson(ws, message);
    setConnectionPatch(state, { lastSent: message.messageType });
    if (!options.silent) {
      pushEvent(state, localActionEvent(message) || {
        kind: "system",
        title: `已发送 ${message.messageType}`,
        detail: actionDetail(message),
      });
    }
  } catch (error) {
    applyMessage(state, {
      messageType: "ERROR",
      errorCode: 1000,
      message: error.message,
    });
  }
  renderApp(state);
}

function handleOrder(event) {
  event.preventDefault();
  const submitter = event.submitter;
  const data = new FormData(event.currentTarget);
  const price = data.get("price");
  const quantity = data.get("quantity");
  const token = state.connection.token;
  const message = submitter?.value === "sell"
    ? limitSellMessage(token, price, quantity)
    : limitBuyMessage(token, price, quantity);
  sendAction(message);
}

function handleCancel(event) {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  sendAction(cancelOrderMessage(state.connection.token, data.get("orderId")));
}

function handleReport(event) {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  sendAction(submitReportMessage(
    state.connection.token,
    data.get("newsId"),
    data.get("prediction"),
  ));
}

function handleQuickReport(event) {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  const prediction = event.submitter?.value || data.get("prediction");
  sendAction(submitReportMessage(
    state.connection.token,
    data.get("newsId"),
    prediction,
  ));
}

function handleSkill(event) {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  sendAction(activateSkillMessage(
    state.connection.token,
    String(data.get("skillName") || "").trim(),
    data.get("targetPlayerId"),
  ));
}

function handleServerRaw(event) {
  event.preventDefault();
  const raw = document.getElementById("serverRawJson")?.value.trim();
  if (!raw) return;
  let message;
  try {
    message = JSON.parse(raw);
  } catch {
    applyMessage(state, { messageType: "ERROR", errorCode: 1000, message: "JSON 解析失败" });
    renderApp(state);
    return;
  }
  sendAction(message);
}

function handleDebugQuery(event) {
  event.preventDefault();
  sendAction(debugQueryMessage());
}

function handleDebugAdvance(event) {
  event.preventDefault();
  sendAction(debugAdvanceStageMessage());
}

function handleDebugGiveCard(event) {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  sendAction(debugGiveCardMessage(
    String(data.get("targetPlayerId") || "").trim(),
    String(data.get("cardName") || "").trim(),
  ));
}

function handleDebugInjectNews(event) {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  sendAction(debugInjectNewsMessage(
    String(data.get("sentiment") || "Bullish"),
    String(data.get("content") || "").trim() || undefined,
  ));
}

function handleDebugSetPlayer(event) {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  sendAction(debugSetPlayerMessage(
    String(data.get("targetPlayerId") || "").trim(),
    {
      mora: data.get("mora"),
      gold: data.get("gold"),
    },
  ));
}

function loadDemo() {
  stopReplayPlayback();
  replaySession = null;
  appliedReplayIndex = -1;
  setReplayPatch(state, {
    enabled: false,
    loaded: false,
    playing: false,
    frameIndex: 0,
    frameCount: 0,
    label: "",
    error: "",
    hasStats: false,
    statEventCount: 0,
  });
  disconnect(true);
  resetUiCollections(state);
  clearSettlement(state);
  resetMarketChartViewport();
  for (const message of buildSampleMessages(state.connection.role)) {
    applyMessage(state, message);
  }
  renderApp(state);
}

async function handleReplayLoad() {
  const replayFile = document.getElementById("replayFileInput")?.files?.[0];
  const statFile = document.getElementById("statFileInput")?.files?.[0] || null;
  if (!replayFile) {
    setReplayPatch(state, { error: "请选择 replay.dat" });
    renderApp(state);
    return;
  }

  stopReplayPlayback();
  disconnect(true);
  setReplayPatch(state, {
    enabled: true,
    loaded: false,
    playing: false,
    frameIndex: 0,
    frameCount: 0,
    label: replayFile.name,
    error: "",
    hasStats: Boolean(statFile),
    statEventCount: 0,
  });
  setConnectionPatch(state, {
    status: "replay",
    lastError: "",
    statusLabel: "",
    statusDetail: "",
  });
  renderApp(state);

  try {
    replaySession = await buildReplaySession(replayFile, statFile);
    appliedReplayIndex = -1;
    resetRuntimeState(state, { preserveReplay: true });
    setReplayPatch(state, {
      enabled: true,
      loaded: true,
      playing: false,
      frameIndex: 0,
      frameCount: replaySession.frameCount,
      label: `${replaySession.label}${replaySession.hasStats ? " + stat" : ""}`,
      error: "",
      hasStats: replaySession.hasStats,
      statEventCount: replaySession.statEventCount,
    });
    setConnectionPatch(state, {
      status: "replay",
      lastError: "",
      statusLabel: "",
      statusDetail: "",
    });
    setActiveView(state, "logs");
    resetMarketChartViewport();
    seekReplayFrame(0);
  } catch (error) {
    replaySession = null;
    appliedReplayIndex = -1;
    setReplayPatch(state, {
      enabled: false,
      loaded: false,
      playing: false,
      error: error.message || "回放加载失败",
    });
    setConnectionPatch(state, {
      status: "error",
      lastError: error.message || "回放加载失败",
      statusLabel: "",
      statusDetail: "",
    });
    renderApp(state);
  }
}

function toggleReplayPlayback() {
  if (!replaySession || !state.replay.loaded) return;
  if (state.replay.playing) {
    stopReplayPlayback();
  } else {
    if (state.replay.frameIndex >= state.replay.frameCount - 1) {
      seekReplayFrame(0);
    }
    setReplayPatch(state, { playing: true, enabled: true });
    startReplayTimer();
  }
  renderApp(state);
}

function startReplayTimer() {
  window.clearInterval(replayTimer);
  const speed = Math.max(0.25, Number(state.replay.speed) || 1);
  const intervalMs = Math.max(60, Math.round(700 / speed));
  replayTimer = window.setInterval(() => {
    if (!replaySession || state.replay.frameIndex >= state.replay.frameCount - 1) {
      stopReplayPlayback();
      renderApp(state);
      return;
    }
    seekReplayFrame(state.replay.frameIndex + 1);
  }, intervalMs);
}

function stopReplayPlayback() {
  window.clearInterval(replayTimer);
  replayTimer = null;
  if (state.replay.playing) {
    setReplayPatch(state, { playing: false });
  }
}

function seekReplayFrame(rawIndex) {
  if (!replaySession || !state.replay.loaded) return;
  const maxFrame = Math.max(0, replaySession.frameCount - 1);
  const targetIndex = Math.min(maxFrame, Math.max(0, Number(rawIndex) || 0));
  if (targetIndex <= appliedReplayIndex || appliedReplayIndex < 0) {
    rebuildReplayTo(targetIndex);
  } else {
    for (let index = appliedReplayIndex + 1; index <= targetIndex; index += 1) {
      applyReplayFrame(index);
    }
  }
  appliedReplayIndex = targetIndex;
  setReplayPatch(state, {
    enabled: true,
    loaded: true,
    frameIndex: targetIndex,
    frameCount: replaySession.frameCount,
  });
  setConnectionPatch(state, { status: "replay" });
  renderApp(state);
}

function rebuildReplayTo(targetIndex) {
  resetRuntimeState(state, { preserveReplay: true });
  setConnectionPatch(state, { status: "replay" });
  resetMarketChartViewport();
  for (let index = 0; index <= targetIndex; index += 1) {
    applyReplayFrame(index);
  }
}

function applyReplayFrame(index) {
  const frame = replaySession?.frames[index];
  if (!frame) return;
  for (const message of frame.messages) {
    applyMessage(state, message);
  }
}

function showSummaryDetail(day) {
  const summary = state.dailySummaries.find((item) => String(item.day) === String(day));
  if (!summary) return;
  const body = document.getElementById("detailModalBody");
  const title = document.getElementById("detailModalTitle");
  const eyebrow = document.getElementById("detailModalEyebrow");
  if (!body || !title || !eyebrow) return;

  eyebrow.textContent = "每日总结";
  title.textContent = `第 ${summary.day} 日总结`;
  body.innerHTML = `
    <section class="detail-section">
      <h3>结算结果</h3>
      <p>胜者：${escapeHtml(summary.winnerPlayerId >= 0 ? playerDisplayName(state, summary.winnerPlayerId) : "Tie")}</p>
      <p>原因：${escapeHtml(summary.reason || "-")}</p>
    </section>
    <div class="detail-grid">
      ${(summary.players || []).map((player) => `
        <section class="detail-section">
          <h3>${escapeHtml(playerDisplayName(state, player.playerId, player.token, player.playerName))}</h3>
          <p>NAV：${escapeHtml(player.nav)}</p>
          <p>Mora：${escapeHtml(player.mora)}</p>
          <p>Gold：${escapeHtml(player.gold)}</p>
          <p>Trades：${escapeHtml(player.tradeCount)}</p>
          <p>Frozen Mora：${escapeHtml(player.frozenMora)}</p>
          <p>Frozen Gold：${escapeHtml(player.frozenGold)}</p>
          <p>Locked Gold：${escapeHtml(player.lockedGold)}</p>
        </section>
      `).join("")}
    </div>
  `;
  openModal("detailModal");
}

function showPlayerDetail(playerKey) {
  const key = String(playerKey);
  const player = state.playerSummaries[key];
  if (!player) return;
  const body = document.getElementById("detailModalBody");
  const title = document.getElementById("detailModalTitle");
  const eyebrow = document.getElementById("detailModalEyebrow");
  if (!body || !title || !eyebrow) return;

  eyebrow.textContent = "操盘手";
  title.textContent = `${playerDisplayName(state, player.playerId, player.token, player.playerName)} 摘要`;
  body.innerHTML = `
    <section class="detail-section">
      <h3>当前状态</h3>
      <p>NAV：${escapeHtml(player.nav)}</p>
      <p>Mora：${escapeHtml(player.mora)}</p>
      <p>Gold：${escapeHtml(player.gold)}</p>
      <p>Pending Orders：${escapeHtml(player.pendingOrderCount ?? 0)}</p>
      <p>Active Cards：${escapeHtml((player.activeCards || []).join("、") || "暂无")}</p>
    </section>
  `;
  openModal("detailModal");
}

function openModal(id) {
  document.getElementById(id)?.removeAttribute("hidden");
}

function closeModal(id) {
  document.getElementById(id)?.setAttribute("hidden", "");
}

function updateRoute() {
  const url = new URL(window.location.href);
  url.searchParams.set("mode", state.connection.role);
  url.searchParams.set("server", state.connection.server);
  if (state.connection.role === "player") {
    url.searchParams.set("token", state.connection.token);
  } else {
    url.searchParams.delete("token");
  }
  if (state.connection.role === "admin" && state.connection.adminSecret) {
    url.searchParams.set("secret", state.connection.adminSecret);
  } else {
    url.searchParams.delete("secret");
  }
  window.history.replaceState({}, "", url);
}

function actionDetail(message) {
  if (message.messageType === "LIMIT_BUY" || message.messageType === "LIMIT_SELL") {
    return `price=${message.price} qty=${message.quantity}`;
  }
  if (message.messageType === "CANCEL_ORDER") {
    return `orderId=${message.orderId}`;
  }
  if (message.messageType === "SUBMIT_REPORT") {
    return `news=${message.newsId} ${message.prediction}`;
  }
  if (message.messageType === "SELECT_STRATEGY") {
    return String(message.cardName || "");
  }
  if (message.messageType === "ACTIVATE_SKILL") {
    const parts = [message.skillName || ""];
    if (message.targetPlayerId !== undefined && message.targetPlayerId !== null && message.targetPlayerId !== "") {
      parts.push(`target=选手 ${message.targetPlayerId}`);
    }
    return parts.filter(Boolean).join(" ");
  }
  return message.messageType;
}

function localActionEvent(message) {
  const token = state.connection.role === "player" ? state.connection.token : "";
  if (message.messageType === "LIMIT_BUY" || message.messageType === "LIMIT_SELL") {
    const side = message.messageType === "LIMIT_SELL" ? "Sell" : "Buy";
    return {
      kind: "order",
      title: `${side === "Sell" ? "卖出" : "买入"}挂单`,
      detail: `price=${message.price} qty=${message.quantity}`,
      playerToken: token,
      side,
      price: Number(message.price) || 0,
      quantity: Number(message.quantity) || 0,
      isPrivate: true,
    };
  }
  if (message.messageType === "CANCEL_ORDER") {
    return {
      kind: "order",
      title: "撤单",
      detail: `order=${message.orderId}`,
      playerToken: token,
      isPrivate: true,
    };
  }
  if (message.messageType === "SUBMIT_REPORT") {
    return {
      kind: "report",
      title: "提交研报",
      detail: `news=${message.newsId} prediction=${message.prediction}`,
      playerToken: token,
      isPrivate: true,
    };
  }
  if (message.messageType === "ACTIVATE_SKILL") {
    return {
      kind: "skill",
      title: message.skillName || "激活技能",
      detail: actionDetail(message),
      playerToken: token,
      targetPlayerId: Number(message.targetPlayerId),
      isPrivate: true,
    };
  }
  if (message.messageType === "SELECT_STRATEGY") {
    return {
      kind: "strategy",
      title: "选择策略",
      detail: String(message.cardName || ""),
      playerToken: token,
      isPrivate: true,
    };
  }
  return null;
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function initParticles() {
  const canvas = document.getElementById("particles-bg");
  if (!canvas) return;
  const ctx = canvas.getContext("2d");
  if (!ctx) return;

  function resize() {
    canvas.width = window.innerWidth;
    canvas.height = window.innerHeight;
  }
  resize();
  window.addEventListener("resize", resize);

  const COUNT = 50;
  const particles = Array.from({ length: COUNT }, () => ({
    x: Math.random() * canvas.width,
    y: Math.random() * canvas.height,
    r: 0.8 + Math.random() * 2,
    vx: (Math.random() - 0.5) * 0.3,
    vy: (Math.random() - 0.5) * 0.3,
    alpha: 0.1 + Math.random() * 0.3,
  }));

  function draw() {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    for (const p of particles) {
      p.x += p.vx;
      p.y += p.vy;
      if (p.x < 0) p.x = canvas.width;
      if (p.x > canvas.width) p.x = 0;
      if (p.y < 0) p.y = canvas.height;
      if (p.y > canvas.height) p.y = 0;
      ctx.beginPath();
      ctx.arc(p.x, p.y, p.r, 0, Math.PI * 2);
      ctx.fillStyle = `rgba(200, 168, 75, ${p.alpha})`;
      ctx.fill();
    }
    requestAnimationFrame(draw);
  }
  requestAnimationFrame(draw);
}
