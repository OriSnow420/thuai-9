import {
  activateSkillMessage,
  cancelOrderMessage,
  helloMessage,
  limitBuyMessage,
  limitSellMessage,
  selectStrategyMessage,
  sendJson,
  submitReportMessage,
} from "./actions.js";
import { buildSampleMessages } from "./sample-data.js";
import {
  applyMessage,
  clearSettlement,
  createInitialState,
  pushEvent,
  routeFromLocation,
  setCandleOptions,
  setColorScheme,
  setConnectionPatch,
  setMode,
} from "./store.js";
import { renderApp } from "./render.js";
import { applyColorScheme, loadColorScheme, saveColorScheme } from "./appearance.js";

const state = createInitialState(routeFromLocation(window.location));
setColorScheme(state, loadColorScheme());
applyColorScheme(state.ui.colorScheme);
let ws = null;
let reconnectTimer = null;
let manuallyClosed = false;

bindControls();
renderApp(state);

function bindControls() {
  document.getElementById("modeTabs")?.addEventListener("click", (event) => {
    const button = event.target.closest("[data-mode]");
    if (!button) return;
    setMode(state, button.dataset.mode);
    updateRoute();
    renderApp(state);
  });

  document.getElementById("serverInput")?.addEventListener("change", (event) => {
    setConnectionPatch(state, { server: event.target.value.trim() || "ws://localhost:14514" });
    updateRoute();
    renderApp(state);
  });

  document.getElementById("tokenInput")?.addEventListener("change", (event) => {
    setConnectionPatch(state, { token: event.target.value.trim() || "player1" });
    updateRoute();
    renderApp(state);
  });

  document.getElementById("connectButton")?.addEventListener("click", connect);
  document.getElementById("disconnectButton")?.addEventListener("click", () => disconnect(true));
  document.getElementById("demoButton")?.addEventListener("click", loadDemo);

  document.getElementById("priceModeSelect")?.addEventListener("change", (event) => {
    setCandleOptions(state, { priceField: event.target.value });
    renderApp(state);
  });

  document.getElementById("intervalSelect")?.addEventListener("change", (event) => {
    setCandleOptions(state, { interval: event.target.value });
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
  document.getElementById("skillForm")?.addEventListener("submit", handleSkill);

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

function connect() {
  clearTimeout(reconnectTimer);
  manuallyClosed = false;

  if (ws && ws.readyState <= 1) {
    ws.close();
  }

  const server = document.getElementById("serverInput")?.value.trim() || state.connection.server;
  const token = document.getElementById("tokenInput")?.value.trim() || state.connection.token;
  setConnectionPatch(state, {
    server,
    token,
    status: "connecting",
    lastError: "",
  });
  updateRoute();
  renderApp(state);

  try {
    ws = new WebSocket(server);
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
    sendAction(helloMessage(state.connection.role, state.connection.token), { silent: true });
    renderApp(state);
  });

  ws.addEventListener("message", (event) => {
    try {
      applyMessage(state, JSON.parse(event.data));
    } catch (error) {
      pushEvent(state, {
        kind: "error",
        title: "消息解析失败",
        detail: error.message,
      });
    }
    renderApp(state);
  });

  ws.addEventListener("error", () => {
    handleSocketError(new Error("WebSocket error"));
  });

  ws.addEventListener("close", () => {
    const nextStatus = manuallyClosed ? "disconnected" : "error";
    setConnectionPatch(state, { status: nextStatus });
    renderApp(state);
    if (!manuallyClosed) scheduleReconnect();
  });
}

function disconnect(byUser) {
  manuallyClosed = byUser;
  clearTimeout(reconnectTimer);
  if (ws && ws.readyState <= 1) {
    ws.close();
  }
  setConnectionPatch(state, { status: "disconnected" });
  renderApp(state);
}

function scheduleReconnect() {
  const attempt = state.connection.reconnectAttempt + 1;
  const delay = Math.min(10000, 500 * 2 ** Math.min(attempt, 5));
  setConnectionPatch(state, { reconnectAttempt: attempt });
  reconnectTimer = window.setTimeout(connect, delay);
}

function handleSocketError(error) {
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
      pushEvent(state, {
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

function handleSkill(event) {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  sendAction(activateSkillMessage(
    state.connection.token,
    String(data.get("skillName") || "").trim(),
    data.get("direction"),
  ));
}

function loadDemo() {
  for (const message of buildSampleMessages()) {
    applyMessage(state, message);
  }
  setConnectionPatch(state, {
    status: "connected",
    protocolVersion: "demo",
  });
  renderApp(state);
}

function updateRoute() {
  const params = new URLSearchParams();
  params.set("mode", state.connection.role);
  if (state.connection.role === "player") {
    params.set("token", state.connection.token);
  }
  if (state.connection.server !== "ws://localhost:14514") {
    params.set("server", state.connection.server);
  }
  window.history.replaceState(null, "", `${window.location.pathname}?${params.toString()}`);
}

function actionDetail(message) {
  switch (message.messageType) {
    case "LIMIT_BUY":
    case "LIMIT_SELL":
      return `price=${message.price} quantity=${message.quantity}`;
    case "CANCEL_ORDER":
      return `orderId=${message.orderId}`;
    case "SUBMIT_REPORT":
      return `newsId=${message.newsId} prediction=${message.prediction}`;
    case "SELECT_STRATEGY":
      return message.cardName;
    case "ACTIVATE_SKILL":
      return `${message.skillName}${message.direction ? ` ${message.direction}` : ""}`;
    default:
      return "";
  }
}
