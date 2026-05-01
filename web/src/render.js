import { applyColorScheme, COLOR_SCHEMES, readAppliedPalette } from "./appearance.js";

const PLACEHOLDER = '<p class="placeholder">暂无数据</p>';

export function renderApp(state) {
  document.body.dataset.mode = state.connection.role;
  applyColorScheme(state.ui.colorScheme);

  setText("stageValue", state.game.stage || "-");
  setText("dayValue", state.game.currentDay || "-");
  setText("tickValue", displayTick(state));
  renderConnection(state);
  renderControls(state);
  renderScoreboard(state);
  renderPrices(state);
  renderOrderBook(state);
  renderEvents(state);
  renderPlayerComparison(state);
  renderPortfolio(state);
  renderOrders(state);
  renderStrategy(state);
  renderSettlement(state);
  drawMarketChart(document.getElementById("marketCanvas"), state);
}

function renderConnection(state) {
  const badge = document.getElementById("connectionBadge");
  if (!badge) return;
  const labels = {
    idle: "未连接",
    connecting: "连接中",
    connected: "已连接",
    disconnected: "已断开",
    error: "连接错误",
  };
  badge.textContent = labels[state.connection.status] || state.connection.status;
  badge.dataset.status = state.connection.status;
}

function renderControls(state) {
  const serverInput = document.getElementById("serverInput");
  const tokenInput = document.getElementById("tokenInput");
  const priceModeSelect = document.getElementById("priceModeSelect");
  const intervalSelect = document.getElementById("intervalSelect");
  const colorSchemeSelect = document.getElementById("colorSchemeSelect");

  setInputValue(serverInput, state.connection.server);
  setInputValue(tokenInput, state.connection.token);
  setInputValue(priceModeSelect, state.market.priceField);
  setInputValue(intervalSelect, String(state.market.interval));
  fillColorSchemeOptions(colorSchemeSelect);
  setInputValue(colorSchemeSelect, state.ui.colorScheme);

  document.querySelectorAll("[data-mode]").forEach((button) => {
    button.classList.toggle("is-active", button.dataset.mode === state.connection.role);
  });
}

function renderScoreboard(state) {
  const node = document.getElementById("scoreboard");
  if (!node) return;
  if (!state.game.scores.length) {
    node.innerHTML = PLACEHOLDER;
    return;
  }

  node.innerHTML = state.game.scores
    .map((score) => `
      <div class="score-row">
        <span>${escapeHtml(score.token)}</span>
        <strong>${escapeHtml(score.score)}</strong>
      </div>
    `)
    .join("");
}

function renderPrices(state) {
  const bids = state.market.bids;
  const asks = state.market.asks;
  const bestBid = bids[0]?.price ?? 0;
  const bestAsk = asks[0]?.price ?? 0;
  setText("bestBidValue", formatNumber(bestBid));
  setText("bestAskValue", formatNumber(bestAsk));
  setText("spreadValue", bestBid && bestAsk ? formatNumber(bestAsk - bestBid) : "-");
  setText("midValue", formatNumber(state.market.midPrice));
  setText("lastValue", formatNumber(state.market.lastPrice));
  setText("volumeValue", formatNumber(state.market.volume));
}

function renderOrderBook(state) {
  const bids = state.market.bids.slice(0, 10);
  const asks = state.market.asks.slice(0, 10);
  const maxQty = Math.max(
    1,
    ...bids.map((level) => Number(level.quantity) || 0),
    ...asks.map((level) => Number(level.quantity) || 0),
  );

  renderBookList("bidsList", bids, "bid", maxQty);
  renderBookList("asksList", asks, "ask", maxQty);
}

function renderBookList(id, levels, side, maxQty) {
  const node = document.getElementById(id);
  if (!node) return;
  if (!levels.length) {
    node.innerHTML = PLACEHOLDER;
    return;
  }

  node.innerHTML = levels
    .map((level) => {
      const depth = Math.max(4, Math.round(((Number(level.quantity) || 0) / maxQty) * 100));
      return `
        <div class="book-row ${side}" style="--depth: ${depth}%">
          <span>${formatNumber(level.price)}</span>
          <span>${formatNumber(level.quantity)}</span>
        </div>
      `;
    })
    .join("");
}

function renderEvents(state) {
  const node = document.getElementById("eventFeed");
  if (!node) return;
  if (!state.events.length) {
    node.innerHTML = PLACEHOLDER;
    return;
  }

  node.innerHTML = state.events
    .slice(0, 80)
    .map((event) => `
      <article class="event-item" data-kind="${escapeHtml(event.kind)}">
        <strong>${escapeHtml(event.title)}</strong>
        <p>D${escapeHtml(event.day || "-")} T${escapeHtml(event.tick || "-")} ${escapeHtml(event.detail)}</p>
      </article>
    `)
    .join("");
}

function renderPlayerComparison(state) {
  const node = document.getElementById("playerComparison");
  if (!node) return;
  const summaries = Object.values(state.playerSummaries);
  if (!summaries.length) {
    node.innerHTML = PLACEHOLDER;
    return;
  }

  node.innerHTML = summaries
    .map((player) => `
      <article class="comparison-card">
        <h3>${escapeHtml(player.token)}</h3>
        <div class="comparison-stats">
          ${statCell("NAV", player.nav)}
          ${statCell("Mora", player.mora)}
          ${statCell("Gold", player.gold)}
          ${statCell("Orders", player.pendingOrderCount ?? 0)}
        </div>
        <div class="tag-row">${renderTags(player.activeCards || [])}</div>
      </article>
    `)
    .join("");
}

function renderPortfolio(state) {
  const node = document.getElementById("portfolioGrid");
  if (!node) return;
  const player = state.player;
  node.innerHTML = [
    statCell("NAV", player.nav),
    statCell("Mora", player.mora),
    statCell("Frozen Mora", player.frozenMora),
    statCell("Gold", player.gold),
    statCell("Frozen Gold", player.frozenGold),
    statCell("Locked Gold", player.lockedGold),
  ].join("");
}

function renderOrders(state) {
  const node = document.getElementById("ordersTable");
  if (!node) return;
  const orders = state.player.pendingOrders || [];
  if (!orders.length) {
    node.innerHTML = '<tr><td colspan="6" class="placeholder">暂无挂单</td></tr>';
    return;
  }

  node.innerHTML = orders
    .map((order) => `
      <tr>
        <td>${escapeHtml(order.orderId)}</td>
        <td><span class="side-pill ${sideClass(order.side)}">${escapeHtml(order.side)}</span></td>
        <td>${formatNumber(order.price)}</td>
        <td>${formatNumber(order.quantity)}</td>
        <td>${formatNumber(order.remainingQuantity)}</td>
        <td>${escapeHtml(order.status)}</td>
      </tr>
    `)
    .join("");
}

function renderStrategy(state) {
  const optionsNode = document.getElementById("strategyOptions");
  const activeNode = document.getElementById("activeCards");
  if (!optionsNode || !activeNode) return;

  const options = state.strategy.options;
  const cards = options
    ? [
        ["基建", options.infrastructure],
        ["风控", options.riskControl],
        ["金融科技", options.finTech],
      ].filter(([, card]) => card)
    : [];

  optionsNode.innerHTML = cards.length
    ? cards.map(([label, card]) => renderStrategyCard(label, card, state.connection.role)).join("")
    : PLACEHOLDER;

  activeNode.innerHTML = renderTags(state.player.activeCards || []);
}

function renderStrategyCard(label, card, role) {
  const action = role === "player"
    ? `<button type="button" data-action="select-strategy" data-card-name="${escapeAttribute(card.name)}">选择</button>`
    : "";

  return `
    <article class="strategy-card">
      <strong>${escapeHtml(label)} · ${escapeHtml(card.name)}</strong>
      <p>${escapeHtml(card.description || card.category || "")}</p>
      ${action}
    </article>
  `;
}

function renderSettlement(state) {
  const modal = document.getElementById("settlementModal");
  const title = document.getElementById("settlementTitle");
  const body = document.getElementById("settlementBody");
  if (!modal || !title || !body) return;

  modal.hidden = !state.ui.showSettlement || !state.settlement;
  if (!state.settlement) return;

  title.textContent = `第 ${state.settlement.day ?? state.game.currentDay} 日结算`;
  const rows = Array.isArray(state.settlement.players) ? state.settlement.players : [];
  body.innerHTML = `
    <div class="settlement-row">
      <span>胜者</span>
      <strong>${escapeHtml(state.settlement.winnerToken || "-")}</strong>
      <span>${escapeHtml(state.settlement.reason || "-")}</span>
    </div>
    ${rows
      .map((player) => `
        <div class="settlement-row">
          <span>${escapeHtml(player.token)}</span>
          <strong>NAV ${formatNumber(player.nav)}</strong>
          <span>${formatNumber(player.tradeCount)} trades</span>
        </div>
      `)
      .join("")}
  `;
}

export function drawMarketChart(canvas, state) {
  if (!canvas) return;
  const palette = readAppliedPalette();
  const rect = canvas.getBoundingClientRect();
  const dpr = window.devicePixelRatio || 1;
  const width = Math.max(320, Math.floor(rect.width));
  const height = Math.max(260, Math.floor(rect.height));

  if (canvas.width !== width * dpr || canvas.height !== height * dpr) {
    canvas.width = width * dpr;
    canvas.height = height * dpr;
  }

  const ctx = canvas.getContext("2d");
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, width, height);
  ctx.fillStyle = "#fbfcfc";
  ctx.fillRect(0, 0, width, height);

  const candles = state.market.candles.slice(-90);
  if (!candles.length) {
    ctx.fillStyle = "#65717a";
    ctx.font = "14px system-ui, sans-serif";
    ctx.fillText("等待 MARKET_STATE", 24, 40);
    return;
  }

  const margin = { top: 18, right: 58, bottom: 42, left: 44 };
  const chartHeight = height - margin.top - margin.bottom;
  const volumeHeight = Math.max(54, chartHeight * 0.22);
  const priceHeight = chartHeight - volumeHeight - 12;
  const plotWidth = width - margin.left - margin.right;

  const prices = candles.flatMap((candle) => [candle.high, candle.low]);
  const minPrice = Math.min(...prices);
  const maxPrice = Math.max(...prices);
  const pricePadding = Math.max(1, (maxPrice - minPrice) * 0.08);
  const priceMin = minPrice - pricePadding;
  const priceMax = maxPrice + pricePadding;
  const maxVolume = Math.max(1, ...candles.map((candle) => candle.volume));
  const slot = plotWidth / candles.length;

  drawGrid(ctx, margin, width, height, priceHeight, priceMin, priceMax);

  candles.forEach((candle, index) => {
    const x = margin.left + index * slot + slot / 2;
    const openY = mapRange(candle.open, priceMin, priceMax, margin.top + priceHeight, margin.top);
    const closeY = mapRange(candle.close, priceMin, priceMax, margin.top + priceHeight, margin.top);
    const highY = mapRange(candle.high, priceMin, priceMax, margin.top + priceHeight, margin.top);
    const lowY = mapRange(candle.low, priceMin, priceMax, margin.top + priceHeight, margin.top);
    const up = candle.close >= candle.open;
    const color = up ? palette.up : palette.down;
    const bodyWidth = Math.max(3, Math.min(12, slot * 0.58));
    const bodyTop = Math.min(openY, closeY);
    const bodyHeight = Math.max(2, Math.abs(closeY - openY));

    ctx.strokeStyle = color;
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(x, highY);
    ctx.lineTo(x, lowY);
    ctx.stroke();

    ctx.fillStyle = color;
    ctx.fillRect(x - bodyWidth / 2, bodyTop, bodyWidth, bodyHeight);

    const volumeTop = margin.top + priceHeight + 12;
    const barHeight = (candle.volume / maxVolume) * volumeHeight;
    ctx.globalAlpha = 0.22;
    ctx.fillRect(x - bodyWidth / 2, volumeTop + volumeHeight - barHeight, bodyWidth, barHeight);
    ctx.globalAlpha = 1;
  });

  const last = candles[candles.length - 1];
  ctx.fillStyle = "#20262d";
  ctx.font = "12px system-ui, sans-serif";
  ctx.fillText(`${Math.round(priceMax)}`, width - margin.right + 10, margin.top + 6);
  ctx.fillText(`${Math.round(priceMin)}`, width - margin.right + 10, margin.top + priceHeight);
  ctx.fillText(`D${last.day} T${last.bucketEndTick}`, margin.left, height - 14);
}

function drawGrid(ctx, margin, width, height, priceHeight, priceMin, priceMax) {
  ctx.strokeStyle = "#d8dfdc";
  ctx.lineWidth = 1;
  ctx.beginPath();
  for (let i = 0; i <= 4; i += 1) {
    const y = margin.top + (priceHeight / 4) * i;
    ctx.moveTo(margin.left, y);
    ctx.lineTo(width - margin.right, y);
  }
  ctx.moveTo(margin.left, height - margin.bottom);
  ctx.lineTo(width - margin.right, height - margin.bottom);
  ctx.stroke();

  ctx.fillStyle = "#65717a";
  ctx.font = "12px system-ui, sans-serif";
  ctx.fillText("OHLC", margin.left, margin.top - 4);
  ctx.fillText(`${Math.round((priceMin + priceMax) / 2)}`, width - margin.right + 10, margin.top + priceHeight / 2);
}

function statCell(label, value) {
  return `<div><span>${escapeHtml(label)}</span><strong>${formatNumber(value)}</strong></div>`;
}

function fillColorSchemeOptions(select) {
  if (!select || select.options.length) return;
  select.innerHTML = COLOR_SCHEMES
    .map((scheme) => `<option value="${escapeAttribute(scheme.value)}">${escapeHtml(scheme.label)}</option>`)
    .join("");
}

function sideClass(value) {
  return String(value || "").toLowerCase() === "sell" ? "sell" : "buy";
}

function renderTags(tags) {
  if (!tags.length) {
    return '<span class="placeholder">暂无策略</span>';
  }
  return tags.map((tag) => `<span class="tag">${escapeHtml(tag)}</span>`).join("");
}

function displayTick(state) {
  const dayTick = state.market.tick || state.game.dayTick || state.game.totalTicks;
  const globalTick = state.game.currentTick;
  if (dayTick && globalTick) return `${dayTick} / ${globalTick}`;
  return dayTick || globalTick || "-";
}

function mapRange(value, inMin, inMax, outMin, outMax) {
  if (inMax === inMin) return (outMin + outMax) / 2;
  return outMin + ((value - inMin) / (inMax - inMin)) * (outMax - outMin);
}

function setText(id, value) {
  const node = document.getElementById(id);
  if (node) node.textContent = value;
}

function setInputValue(node, value) {
  if (node && document.activeElement !== node) {
    node.value = value ?? "";
  }
}

function formatNumber(value) {
  const number = Number(value);
  if (!Number.isFinite(number) || number === 0) return number === 0 ? "0" : "-";
  return new Intl.NumberFormat("en-US").format(number);
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function escapeAttribute(value) {
  return escapeHtml(value).replaceAll("`", "&#096;");
}
