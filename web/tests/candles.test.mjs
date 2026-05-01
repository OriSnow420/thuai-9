import assert from "node:assert/strict";
import { createCandleAccumulator, ingestMarketSnapshot, rebuildCandles } from "../src/candles.js";

testSameBucketOhlc();
testVolumeDelta();
testCrossDayBaseline();
testRebuildWithIntervals();

function testSameBucketOhlc() {
  const acc = createCandleAccumulator({ interval: 20 });
  ingestMarketSnapshot(acc, snapshot(1, 1, 100, 10));
  ingestMarketSnapshot(acc, snapshot(1, 2, 106, 13));
  ingestMarketSnapshot(acc, snapshot(1, 3, 97, 15));

  assert.equal(acc.candles.length, 1);
  assert.deepEqual(pickOhlc(acc.candles[0]), {
    open: 100,
    high: 106,
    low: 97,
    close: 97,
  });
}

function testVolumeDelta() {
  const acc = createCandleAccumulator({ interval: 20 });
  ingestMarketSnapshot(acc, snapshot(1, 1, 100, 10));
  ingestMarketSnapshot(acc, snapshot(1, 2, 101, 14));
  ingestMarketSnapshot(acc, snapshot(1, 3, 102, 19));

  assert.equal(acc.candles[0].volume, 9);
}

function testCrossDayBaseline() {
  const acc = createCandleAccumulator({ interval: 20 });
  ingestMarketSnapshot(acc, snapshot(1, 1, 100, 10));
  ingestMarketSnapshot(acc, snapshot(1, 2, 101, 15));
  ingestMarketSnapshot(acc, snapshot(2, 1, 120, 50));
  ingestMarketSnapshot(acc, snapshot(2, 2, 121, 54));

  assert.equal(acc.candles.length, 2);
  assert.equal(acc.candles[0].volume, 5);
  assert.equal(acc.candles[1].volume, 4);
}

function testRebuildWithIntervals() {
  const candles = rebuildCandles([
    snapshot(1, 1, 100, 1),
    snapshot(1, 10, 110, 2),
    snapshot(1, 11, 105, 3),
  ], { interval: 10 });

  assert.equal(candles.length, 2);
  assert.equal(candles[0].bucketStartTick, 1);
  assert.equal(candles[1].bucketStartTick, 11);
}

function snapshot(day, tick, midPrice, volume) {
  return {
    game: {
      stage: "TradingDay",
      currentDay: day,
    },
    market: {
      tick,
      midPrice,
      lastPrice: midPrice,
      volume,
    },
  };
}

function pickOhlc(candle) {
  return {
    open: candle.open,
    high: candle.high,
    low: candle.low,
    close: candle.close,
  };
}
