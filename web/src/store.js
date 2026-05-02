import {
  DEFAULT_CANDLE_INTERVAL,
  DEFAULT_PRICE_FIELD,
  ingestMarketSnapshot,
  createCandleAccumulator,
  normalizeInterval,
} from "./candles.js";
import { DEFAULT_COLOR_SCHEME, normalizeColorScheme } from "./appearance.js";

const MAX_EVENTS = 160;
const MAX_MARKET_HISTORY = 8000;

export function routeFromLocation(location) {
  const search = new URLSearchParams(location.search);
  const pathMode = location.pathname.includes("player") ? "player" : "observer";
  const mode = search.get("mode") || pathMode;
  return {
    role: mode === "player" ? "player" : "observer",
    token: search.get("token") || "player1",
    server: search.get("server") || "ws://localhost:14514",
  };
}

export function createInitialState(route = {}) {
  return {
    connection: {
      role: route.role || "observer",
      token: route.token || "player1",
      server: route.server || "ws://localhost:14514",
      status: "idle",
      protocolVersion: "legacy",
      capabilities: [],
      reconnectAttempt: 0,
      lastError: "",
      lastSent: "",
    },
    game: {
      stage: "",
      currentDay: 0,
      currentTick: 0,
      totalTicks: 0,
      stageTick: 0,
      stageTickLimit: 0,
      dayTick: 0,
      dayTickLimit: 2000,
      scores: [],
    },
    market: {
      bids: [],
      asks: [],
      lastPrice: 0,
      midPrice: 0,
      volume: 0,
      tick: 0,
      interval: DEFAULT_CANDLE_INTERVAL,
      priceField: DEFAULT_PRICE_FIELD,
      history: [],
      candles: [],
      candleAccumulator: createCandleAccumulator(),
    },
    player: emptyPlayerState(),
    playerSummaries: {},
    dailySummaries: [],
    strategy: {
      options: null,
    },
    events: [],
    settlement: null,
    ui: {
      eventCounter: 0,
      showSettlement: false,
      colorScheme: normalizeColorScheme(route.colorScheme || DEFAULT_COLOR_SCHEME),
    },
  };
}

export function setConnectionPatch(state, patch) {
  Object.assign(state.connection, patch);
}

export function setMode(state, role) {
  state.connection.role = role === "player" ? "player" : "observer";
}

export function setCandleOptions(state, options) {
  state.market.interval = normalizeInterval(options.interval ?? state.market.interval);
  state.market.priceField = options.priceField || state.market.priceField;
  const accumulator = createCandleAccumulator({
    interval: state.market.interval,
    priceField: state.market.priceField,
  });
  for (const snapshot of state.market.history) {
    ingestMarketSnapshot(accumulator, snapshot);
  }
  state.market.candleAccumulator = accumulator;
  state.market.candles = accumulator.candles;
}

export function setColorScheme(state, value) {
  state.ui.colorScheme = normalizeColorScheme(value);
}

export function applyMessage(state, message) {
  if (!message || typeof message !== "object") return;

  switch (message.messageType) {
    case "HELLO_ACK":
      state.connection.protocolVersion = message.protocolVersion || "v1";
      state.connection.capabilities = Array.isArray(message.capabilities)
        ? message.capabilities
        : [];
      state.connection.role = message.role || state.connection.role;
      state.connection.token = message.token || state.connection.token;
      pushEvent(state, {
        kind: "system",
        title: "握手完成",
        detail: `protocol=${state.connection.protocolVersion}`,
      });
      break;

    case "GAME_STATE":
      state.game = {
        ...state.game,
        stage: message.stage || "",
        currentDay: numberOr(message.currentDay, 0),
        currentTick: numberOr(message.currentTick, 0),
        totalTicks: numberOr(message.totalTicks, 0),
        stageTick: numberOr(message.stageTick, state.game.stageTick),
        stageTickLimit: numberOr(message.stageTickLimit, state.game.stageTickLimit),
        dayTick: numberOr(message.dayTick, state.market.tick || state.game.dayTick),
        dayTickLimit: numberOr(message.dayTickLimit, state.game.dayTickLimit),
        scores: Array.isArray(message.scores) ? message.scores : [],
      };
      break;

    case "MARKET_STATE":
      state.market.bids = Array.isArray(message.bids) ? message.bids : [];
      state.market.asks = Array.isArray(message.asks) ? message.asks : [];
      state.market.lastPrice = numberOr(message.lastPrice, 0);
      state.market.midPrice = numberOr(message.midPrice, 0);
      state.market.volume = numberOr(message.volume, 0);
      state.market.tick = numberOr(message.tick, 0);
      state.game.dayTick = state.market.tick;
      appendMarketSnapshot(state);
      break;

    case "PLAYER_STATE":
      state.player = normalizePlayerState(message);
      break;

    case "PLAYER_SUMMARY_STATE":
      if (message.token) {
        state.playerSummaries[message.token] = { ...message };
      }
      break;

    case "NEWS_BROADCAST":
      pushEvent(state, {
        kind: "news",
        title: `新闻 #${message.newsId ?? "-"}`,
        detail: message.content || "",
        tick: numberOr(message.publishTick, state.market.tick),
      });
      break;

    case "REPORT_RESULT":
      pushEvent(state, {
        kind: "report",
        title: `研报 ${message.isCorrect ? "正确" : "错误"}`,
        detail: `news=${message.newsId ?? "-"} prediction=${message.prediction ?? "-"} reward=${message.reward ?? 0} change=${message.actualChange ?? 0}`,
      });
      break;

    case "STRATEGY_OPTIONS":
      state.strategy.options = {
        infrastructure: message.infrastructure || null,
        riskControl: message.riskControl || null,
        finTech: message.finTech || null,
      };
      break;

    case "TRADE_NOTIFICATION":
      pushEvent(state, {
        kind: "trade",
        title: `成交 #${message.tradeId ?? "-"}`,
        detail: `price=${message.price ?? 0} qty=${message.quantity ?? 0} side=${message.side ?? "-"}`,
        tick: numberOr(message.tick, state.market.tick),
      });
      break;

    case "SKILL_EFFECT":
      pushEvent(state, {
        kind: "skill",
        title: message.skillName || "技能触发",
        detail: `${message.sourcePlayer || "-"} ${message.description || ""}`.trim(),
      });
      break;

    case "DAY_SETTLEMENT":
      state.settlement = { ...message };
      upsertDailySummary(state, message);
      state.ui.showSettlement = true;
      pushEvent(state, {
        kind: "settlement",
        title: `第 ${message.day ?? state.game.currentDay} 日结算`,
        detail: `winner=${message.winnerToken || "-"} reason=${message.reason || "-"}`,
      });
      break;

    case "ERROR":
      state.connection.lastError = message.message || "";
      pushEvent(state, {
        kind: "error",
        title: `错误 ${message.errorCode ?? ""}`.trim(),
        detail: message.message || "",
      });
      break;

    default:
      pushEvent(state, {
        kind: "system",
        title: message.messageType || "未知消息",
        detail: "未识别的 messageType",
      });
      break;
  }
}

function upsertDailySummary(state, message) {
  const day = numberOr(message.day, state.game.currentDay);
  const summary = {
    day,
    winnerToken: message.winnerToken || "",
    reason: message.reason || "",
    players: Array.isArray(message.players) ? message.players : [],
  };
  const index = state.dailySummaries.findIndex((item) => item.day === day);
  if (index >= 0) {
    state.dailySummaries[index] = summary;
  } else {
    state.dailySummaries.push(summary);
  }
  state.dailySummaries.sort((a, b) => a.day - b.day);
}

export function pushEvent(state, event) {
  state.ui.eventCounter += 1;
  state.events.unshift({
    id: state.ui.eventCounter,
    day: event.day ?? state.game.currentDay,
    tick: event.tick ?? state.market.tick ?? state.game.currentTick,
    kind: event.kind || "system",
    title: event.title || "事件",
    detail: event.detail || "",
  });

  if (state.events.length > MAX_EVENTS) {
    state.events.length = MAX_EVENTS;
  }
}

export function clearSettlement(state) {
  state.ui.showSettlement = false;
}

function appendMarketSnapshot(state) {
  const snapshot = {
    game: { ...state.game },
    market: {
      bids: state.market.bids,
      asks: state.market.asks,
      lastPrice: state.market.lastPrice,
      midPrice: state.market.midPrice,
      volume: state.market.volume,
      tick: state.market.tick,
    },
  };

  state.market.history.push(snapshot);
  if (state.market.history.length > MAX_MARKET_HISTORY) {
    state.market.history.shift();
  }

  ingestMarketSnapshot(state.market.candleAccumulator, snapshot);
  state.market.candles = state.market.candleAccumulator.candles;
}

function normalizePlayerState(message) {
  return {
    mora: numberOr(message.mora, 0),
    frozenMora: numberOr(message.frozenMora, 0),
    gold: numberOr(message.gold, 0),
    frozenGold: numberOr(message.frozenGold, 0),
    lockedGold: numberOr(message.lockedGold, 0),
    nav: numberOr(message.nav, 0),
    activeCards: Array.isArray(message.activeCards) ? message.activeCards : [],
    pendingOrders: Array.isArray(message.pendingOrders) ? message.pendingOrders : [],
  };
}

function emptyPlayerState() {
  return {
    mora: 0,
    frozenMora: 0,
    gold: 0,
    frozenGold: 0,
    lockedGold: 0,
    nav: 0,
    activeCards: [],
    pendingOrders: [],
  };
}

function numberOr(value, fallback) {
  const number = Number(value);
  return Number.isFinite(number) ? number : fallback;
}
