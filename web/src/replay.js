const DEFAULT_REPLAY_TOTAL_TICKS = 30;
let jsZipPromise = null;

export async function buildReplaySession(replayInput, statInput = null) {
  const snapshots = await readJsonArchive(replayInput);
  const statEvents = statInput ? await readJsonArchive(statInput, { inferMonthFromFileName: true }) : [];
  const statIndex = buildStatIndex(statEvents);
  const usedStatEvents = new Set();
  const seenOrders = new Set();

  const frameEvents = snapshots.map((snapshot) => [
    ...extractOrderEvents(snapshot, seenOrders),
    ...arrayOf(readField(snapshot, "events")),
    ...statEventsForSnapshot(snapshot, statIndex, usedStatEvents),
  ]);
  attachUnmatchedStatEvents(snapshots, frameEvents, statIndex, usedStatEvents);

  const frames = snapshots.map((snapshot, index) => {
    return {
      index,
      snapshot,
      messages: snapshotToMessages(snapshot, frameEvents[index]),
    };
  });

  return {
    frames,
    frameCount: frames.length,
    label: inputLabel(replayInput, "replay.dat"),
    hasStats: statEvents.length > 0,
    statEventCount: statEvents.length,
  };
}

export function snapshotToMessages(snapshot, events = []) {
  const messages = [snapshotToGameState(snapshot)];
  const marketState = snapshotToMarketState(snapshot);
  if (marketState) {
    messages.push(marketState);
  }

  for (const player of arrayOf(readField(snapshot, "players"))) {
    messages.push(playerToSummaryState(player));
  }

  for (const event of events) {
    messages.push(...eventToMessages(event, snapshot));
  }

  return messages;
}

export async function readJsonArchive(input, options = {}) {
  if (!input) return [];
  const JSZip = await loadJsZip();
  const zip = await JSZip.loadAsync(await readBinaryInput(input));
  const files = Object.values(zip.files)
    .filter((file) => !file.dir && file.name.toLowerCase().endsWith(".json"))
    .sort((a, b) => compareArchiveNames(a.name, b.name));

  const records = [];
  for (const file of files) {
    const content = await file.async("string");
    if (!content.trim()) continue;
    const parsed = JSON.parse(content);
    const fileNumber = archiveFileNumber(file.name);
    if (Array.isArray(parsed)) {
      records.push(...parsed.map((item) => normalizeArchiveRecord(item, fileNumber, options)));
    } else {
      records.push(normalizeArchiveRecord(parsed, fileNumber, options));
    }
  }
  return records;
}

function snapshotToGameState(snapshot) {
  const dayTick = readNumber(snapshot, "tradingDayTick", readNumber(snapshot, "day", 0));
  return {
    messageType: "GAME_STATE",
    stage: String(readField(snapshot, "stage") || ""),
    currentMonth: readNumber(snapshot, "month", 0),
    currentDay: readNumber(snapshot, "day", 0),
    currentTick: readNumber(snapshot, "tick", 0),
    totalTicks: readNumber(snapshot, "totalTicks", DEFAULT_REPLAY_TOTAL_TICKS),
    dayTick,
    dayTickLimit: readNumber(snapshot, "dayTickLimit", DEFAULT_REPLAY_TOTAL_TICKS),
    scores: normalizeScores(readField(snapshot, "scores")),
  };
}

function snapshotToMarketState(snapshot) {
  const market = readField(snapshot, "marketState");
  if (!market || typeof market !== "object") return null;
  return {
    messageType: "MARKET_STATE",
    bids: normalizeLevels(readField(market, "bids")),
    asks: normalizeLevels(readField(market, "asks")),
    lastPrice: readNumber(market, "lastPrice", 0),
    midPrice: readNumber(market, "midPrice", 0),
    volume: readNumber(market, "volume", 0),
    tick: readNumber(snapshot, "tradingDayTick", readNumber(snapshot, "day", 0)),
  };
}

function playerToSummaryState(player) {
  const pendingOrders = arrayOf(readField(player, "pendingOrders"));
  return {
    messageType: "PLAYER_SUMMARY_STATE",
    playerId: optionalNumber(readField(player, "playerId")),
    token: String(readField(player, "token") || ""),
    playerName: String(readField(player, "playerName") || ""),
    mora: readNumber(player, "mora", 0),
    frozenMora: readNumber(player, "frozenMora", 0),
    gold: readNumber(player, "gold", 0),
    frozenGold: readNumber(player, "frozenGold", 0),
    lockedGold: readNumber(player, "lockedGold", 0),
    nav: readNumber(player, "nav", readNumber(player, "mora", 0)),
    monthlyTradeCount: readNumber(player, "monthlyTradeCount", 0),
    tradeCount: readNumber(player, "tradeCount", readNumber(player, "monthlyTradeCount", 0)),
    activeCards: arrayOf(readField(player, "activeCards")),
    pendingOrderCount: pendingOrders.length,
    pendingOrders,
  };
}

function eventToMessages(event, snapshot) {
  if (!event || typeof event !== "object") return [];
  const messageType = readField(event, "messageType");
  if (messageType) {
    return [{ ...event }];
  }

  const type = String(readField(event, "type") || "").toLowerCase();
  const month = readNumber(event, "month", readNumber(snapshot, "month", 0));
  const day = readNumber(snapshot, "day", readNumber(event, "publishTick", 0));
  const currentTick = readNumber(snapshot, "tick", readNumber(event, "tick", 0));
  const tradingTick = readNumber(snapshot, "tradingDayTick", readNumber(snapshot, "day", 0));

  if (type === "news") {
    return [{
      messageType: "NEWS_BROADCAST",
      month,
      day: readNumber(event, "publishTick", day),
      newsId: readNumber(event, "newsId", 0),
      content: String(readField(event, "content") || ""),
      publishTick: readNumber(event, "publishTick", tradingTick),
      sentiment: String(readField(event, "sentiment") || ""),
      isFake: Boolean(readField(event, "isFake")),
      sourcePlayer: String(readField(event, "sourcePlayer") || ""),
    }];
  }

  if (type === "report") {
    return [{
      messageType: "REPORT_RESULT",
      playerId: optionalNumber(readField(event, "playerId")),
      playerToken: String(readField(event, "playerToken") || ""),
      playerName: String(readField(event, "playerName") || ""),
      newsId: readNumber(event, "newsId", 0),
      submissionRank: readNumber(event, "submissionRank", 0),
      submitTick: readNumber(event, "submitTick", 0),
      settlementTick: readNumber(event, "settlementTick", tradingTick),
      prediction: String(readField(event, "prediction") || ""),
      isCorrect: Boolean(readField(event, "isCorrect")),
      reward: readNumber(event, "reward", 0),
      actualChange: readNumber(event, "actualChange", 0),
    }];
  }

  if (type === "trade") {
    return [{
      messageType: "REPLAY_TRADE",
      month,
      tick: readNumber(event, "tick", tradingTick),
      tradeId: optionalNumber(readField(event, "tradeId")),
      buyOrderId: optionalNumber(readField(event, "buyOrderId")),
      sellOrderId: optionalNumber(readField(event, "sellOrderId")),
      buyerPlayerId: optionalNumber(readField(event, "buyerPlayerId")),
      sellerPlayerId: optionalNumber(readField(event, "sellerPlayerId")),
      buyerToken: String(readField(event, "buyerToken") || ""),
      sellerToken: String(readField(event, "sellerToken") || ""),
      buyerPlayerName: String(readField(event, "buyerPlayerName") || ""),
      sellerPlayerName: String(readField(event, "sellerPlayerName") || ""),
      price: readNumber(event, "price", 0),
      quantity: readNumber(event, "quantity", 0),
      buyerFee: readNumber(event, "buyerFee", 0),
      sellerFee: readNumber(event, "sellerFee", 0),
    }];
  }

  if (type === "order") {
    return [{
      messageType: "REPLAY_ORDER",
      playerId: optionalNumber(readField(event, "playerId")),
      playerToken: String(readField(event, "playerToken") || ""),
      playerName: String(readField(event, "playerName") || ""),
      orderId: optionalNumber(readField(event, "orderId")),
      side: String(readField(event, "side") || ""),
      price: readNumber(event, "price", 0),
      quantity: readNumber(event, "quantity", 0),
      remainingQuantity: readNumber(event, "remainingQuantity", readNumber(event, "quantity", 0)),
      status: String(readField(event, "status") || ""),
      tick: readNumber(event, "tick", tradingTick),
      action: String(readField(event, "action") || "submit"),
    }];
  }

  if (type === "skill") {
    return [{
      messageType: "SKILL_EFFECT",
      skillName: String(readField(event, "skillName") || "技能触发"),
      sourcePlayerId: optionalNumber(readField(event, "sourcePlayerId")),
      targetPlayerId: optionalNumber(readField(event, "targetPlayerId")),
      description: String(readField(event, "description") || ""),
      tick: readNumber(event, "tick", currentTick),
    }];
  }

  if (type === "draft") {
    return [{
      messageType: "REPLAY_DRAFT",
      month,
      offerings: arrayOf(readField(event, "offerings")),
      selections: readField(event, "selections") || {},
      tick: tradingTick,
    }];
  }

  return [{
    messageType: "REPLAY_EVENT",
    kind: type || "system",
    title: String(readField(event, "title") || type || "回放事件"),
    detail: JSON.stringify(event),
    tick: tradingTick,
  }];
}

function extractOrderEvents(snapshot, seenOrders) {
  const events = [];
  const tick = readNumber(snapshot, "tradingDayTick", readNumber(snapshot, "day", 0));
  for (const player of arrayOf(readField(snapshot, "players"))) {
    const playerId = optionalNumber(readField(player, "playerId"));
    const playerToken = String(readField(player, "token") || "");
    const playerName = String(readField(player, "playerName") || "");
    for (const order of arrayOf(readField(player, "pendingOrders"))) {
      const orderId = readField(order, "orderId");
      if (orderId === undefined || orderId === null || orderId === "") continue;
      const key = `${playerId ?? playerToken}:${orderId}`;
      if (seenOrders.has(key)) continue;
      seenOrders.add(key);
      events.push({
        type: "order",
        action: "submit",
        playerId,
        playerToken,
        playerName,
        orderId,
        tick,
        side: String(readField(order, "side") || ""),
        price: readNumber(order, "price", 0),
        quantity: readNumber(order, "quantity", 0),
        remainingQuantity: readNumber(order, "remainingQuantity", readNumber(order, "quantity", 0)),
        status: String(readField(order, "status") || ""),
      });
    }
  }
  return events;
}

function buildStatIndex(events) {
  const byMonthTick = new Map();
  const byGlobalTick = new Map();
  const drafts = new Map();
  const all = [];

  events.forEach((event, index) => {
    if (!event || typeof event !== "object") return;
    const type = String(readField(event, "type") || "").toLowerCase();
    const key = statEventKey(event, index);
    all.push({ event, key });
    const month = optionalNumber(readField(event, "month"));
    if (type === "draft" && month !== undefined) {
      drafts.set(month, { event, key });
      return;
    }

    if (type === "news" && month !== undefined) {
      addIndexed(byMonthTick, `${month}:${readNumber(event, "publishTick", 0)}`, event, key);
      return;
    }

    if (type === "report" && month !== undefined) {
      addIndexed(byMonthTick, `${month}:${readNumber(event, "settlementTick", 0)}`, event, key);
      return;
    }

    if (type === "skill") {
      const tick = optionalNumber(readField(event, "tick"));
      if (tick !== undefined) {
        addIndexed(byGlobalTick, String(tick), event, key);
      } else if (month !== undefined) {
        addIndexed(byMonthTick, `${month}:0`, event, key);
      }
    }
  });

  return { byMonthTick, byGlobalTick, drafts, all };
}

function statEventsForSnapshot(snapshot, index, used) {
  const events = [];
  const month = optionalNumber(readField(snapshot, "month"));
  if (month === undefined) return events;

  const stage = String(readField(snapshot, "stage") || "");
  const draft = index.drafts.get(month);
  if (draft && stage === "StrategySelection" && !used.has(draft.key)) {
    used.add(draft.key);
    events.push(draft.event);
  }

  const dayTick = readNumber(snapshot, "tradingDayTick", readNumber(snapshot, "day", 0));
  pushUnused(events, index.byMonthTick.get(`${month}:${dayTick}`), used);
  pushUnused(events, index.byGlobalTick.get(String(readNumber(snapshot, "tick", 0))), used);
  return events;
}

function addIndexed(map, key, event, eventKey) {
  if (!map.has(key)) {
    map.set(key, []);
  }
  map.get(key).push({ event, key: eventKey });
}

function pushUnused(target, entries, used) {
  if (!entries) return;
  for (const entry of entries) {
    if (used.has(entry.key)) continue;
    used.add(entry.key);
    target.push(entry.event);
  }
}

function attachUnmatchedStatEvents(snapshots, frameEvents, index, used) {
  if (!snapshots.length) return;
  const framesByMonth = new Map();
  snapshots.forEach((snapshot, frameIndex) => {
    const month = optionalNumber(readField(snapshot, "month"));
    if (month === undefined) return;
    if (!framesByMonth.has(month)) {
      framesByMonth.set(month, []);
    }
    framesByMonth.get(month).push(frameIndex);
  });

  for (const entry of index.all) {
    if (used.has(entry.key)) continue;
    const frameIndex = fallbackFrameIndex(entry.event, snapshots, framesByMonth);
    frameEvents[frameIndex].push(entry.event);
    used.add(entry.key);
  }
}

function fallbackFrameIndex(event, snapshots, framesByMonth) {
  const month = optionalNumber(readField(event, "month"));
  const monthFrames = month !== undefined ? framesByMonth.get(month) : null;
  if (!monthFrames || !monthFrames.length) {
    return snapshots.length - 1;
  }

  const preferredTick = preferredEventTick(event);
  let candidate = monthFrames[monthFrames.length - 1];
  for (const frameIndex of monthFrames) {
    const snapshot = snapshots[frameIndex];
    const tick = readNumber(snapshot, "tradingDayTick", readNumber(snapshot, "day", 0));
    if (tick >= preferredTick) {
      candidate = frameIndex;
      break;
    }
  }
  return candidate;
}

function preferredEventTick(event) {
  const type = String(readField(event, "type") || "").toLowerCase();
  if (type === "news") return readNumber(event, "publishTick", 0);
  if (type === "report") return readNumber(event, "settlementTick", 0);
  return readNumber(event, "tick", 0);
}

function statEventKey(event, index) {
  return [
    index,
    readField(event, "type") || "event",
    readField(event, "month") ?? "",
    readField(event, "tick") ?? readField(event, "publishTick") ?? readField(event, "settlementTick") ?? "",
    readField(event, "newsId") ?? "",
    readField(event, "playerToken") ?? "",
  ].join(":");
}

function normalizeScores(scores) {
  if (Array.isArray(scores)) {
    return scores.map((score) => ({
      playerId: optionalNumber(readField(score, "playerId")),
      playerToken: String(readField(score, "playerToken") || readField(score, "token") || ""),
      playerName: String(readField(score, "playerName") || ""),
      score: readNumber(score, "score", 0),
    }));
  }

  if (scores && typeof scores === "object") {
    return Object.entries(scores).map(([playerToken, score]) => ({
      playerToken,
      playerName: "",
      score: Number(score) || 0,
    }));
  }

  return [];
}

function normalizeLevels(levels) {
  return arrayOf(levels).map((level) => ({
    price: readNumber(level, "price", 0),
    quantity: readNumber(level, "quantity", 0),
  }));
}

async function loadJsZip() {
  if (globalThis.JSZip) return globalThis.JSZip;
  if (!jsZipPromise) {
    jsZipPromise = loadJsZipModule();
  }
  return jsZipPromise;
}

async function loadJsZipModule() {
  try {
    const module = await import("jszip");
    return module.default || module;
  } catch (error) {
    if (typeof document === "undefined") {
      throw error;
    }
  }

  await new Promise((resolve, reject) => {
    const script = document.createElement("script");
    script.src = "./node_modules/jszip/dist/jszip.min.js";
    script.async = true;
    script.onload = resolve;
    script.onerror = () => reject(new Error("JSZip 加载失败"));
    document.head.appendChild(script);
  });

  if (!globalThis.JSZip) {
    throw new Error("JSZip 未初始化");
  }
  return globalThis.JSZip;
}

async function readBinaryInput(input) {
  if (input instanceof ArrayBuffer) return input;
  if (ArrayBuffer.isView(input)) {
    return input.buffer.slice(input.byteOffset, input.byteOffset + input.byteLength);
  }
  if (typeof input.arrayBuffer === "function") {
    return input.arrayBuffer();
  }
  return input;
}

function compareArchiveNames(a, b) {
  const aNumber = archiveFileNumber(a);
  const bNumber = archiveFileNumber(b);
  if (Number.isFinite(aNumber) && Number.isFinite(bNumber) && aNumber !== bNumber) {
    return aNumber - bNumber;
  }
  return String(a).localeCompare(String(b));
}

function normalizeArchiveRecord(item, fileNumber, options) {
  if (!options.inferMonthFromFileName || !item || typeof item !== "object") {
    return item;
  }
  if (readField(item, "month") !== undefined || !Number.isFinite(fileNumber)) {
    return item;
  }
  return { ...item, month: fileNumber };
}

function archiveFileNumber(name) {
  return Number.parseInt(String(name).match(/(\d+)(?=\.json$)/)?.[1] || "", 10);
}

function readField(object, key) {
  if (!object || typeof object !== "object") return undefined;
  if (Object.prototype.hasOwnProperty.call(object, key)) return object[key];
  const pascalKey = `${key.slice(0, 1).toUpperCase()}${key.slice(1)}`;
  return object[pascalKey];
}

function readNumber(object, key, fallback) {
  return numberOr(readField(object, key), fallback);
}

function optionalNumber(value) {
  const number = Number(value);
  return Number.isFinite(number) ? number : undefined;
}

function numberOr(value, fallback) {
  const number = Number(value);
  return Number.isFinite(number) ? number : fallback;
}

function arrayOf(value) {
  return Array.isArray(value) ? value : [];
}

function inputLabel(input, fallback) {
  return String(input?.name || fallback);
}
