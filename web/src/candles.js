export const DEFAULT_CANDLE_INTERVAL = 20;
export const DEFAULT_PRICE_FIELD = "midPrice";

export function createCandleAccumulator(options = {}) {
  return {
    interval: normalizeInterval(options.interval),
    priceField: options.priceField || DEFAULT_PRICE_FIELD,
    candles: [],
    lastVolume: null,
    lastDay: null,
    lastTick: null,
  };
}

export function ingestMarketSnapshot(accumulator, snapshot) {
  const game = snapshot.game || {};
  const market = snapshot.market || {};
  const day = toNumber(game.currentDay, 0);
  const tick = toNumber(market.tick, 0);
  const volume = Math.max(0, toNumber(market.volume, 0));
  const price = toNumber(market[accumulator.priceField], 0);

  const resetVolume =
    accumulator.lastVolume === null ||
    accumulator.lastDay !== day ||
    tick < (accumulator.lastTick ?? 0);

  const volumeDelta = resetVolume
    ? 0
    : Math.max(0, volume - accumulator.lastVolume);

  accumulator.lastVolume = volume;
  accumulator.lastDay = day;
  accumulator.lastTick = tick;

  if (game.stage !== "TradingDay" || day <= 0 || tick <= 0 || price <= 0) {
    return accumulator.candles;
  }

  const bucketIndex = Math.floor((tick - 1) / accumulator.interval);
  const bucketStartTick = bucketIndex * accumulator.interval + 1;
  const bucketEndTick = bucketStartTick + accumulator.interval - 1;
  const last = accumulator.candles[accumulator.candles.length - 1];

  if (!last || last.day !== day || last.bucketStartTick !== bucketStartTick) {
    accumulator.candles.push({
      day,
      bucketStartTick,
      bucketEndTick,
      open: price,
      high: price,
      low: price,
      close: price,
      volume: volumeDelta,
    });
    return accumulator.candles;
  }

  last.high = Math.max(last.high, price);
  last.low = Math.min(last.low, price);
  last.close = price;
  last.volume += volumeDelta;
  return accumulator.candles;
}

export function rebuildCandles(snapshots, options = {}) {
  const accumulator = createCandleAccumulator(options);
  for (const snapshot of snapshots) {
    ingestMarketSnapshot(accumulator, snapshot);
  }
  return accumulator.candles;
}

export function normalizeInterval(value) {
  const interval = Number.parseInt(value, 10);
  return [10, 20, 50, 100].includes(interval) ? interval : DEFAULT_CANDLE_INTERVAL;
}

function toNumber(value, fallback) {
  const number = Number(value);
  return Number.isFinite(number) ? number : fallback;
}
