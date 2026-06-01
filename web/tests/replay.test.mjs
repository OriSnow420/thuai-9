import assert from "node:assert/strict";
import JSZip from "jszip";
import { buildReplaySession, snapshotToMessages } from "../src/replay.js";

await testReplaySessionBuildsFramesWithStatEvents();
testEmbeddedReplayEventsNormalizeToMessages();

async function testReplaySessionBuildsFramesWithStatEvents() {
  const replay = await zipPages([
    [
      {
        tick: 1,
        stage: "StrategySelection",
        month: 1,
        day: 0,
        scores: { alpha: 1 },
        players: [],
      },
      {
        tick: 2,
        stage: "TradingDay",
        month: 1,
        day: 1,
        tradingDayTick: 1,
        scores: { alpha: 1 },
        marketState: {
          bids: [{ price: 999, quantity: 2 }],
          asks: [{ price: 1001, quantity: 3 }],
          lastPrice: 1000,
          midPrice: 1000,
          volume: 0,
        },
        players: [
          {
            playerId: 0,
            token: "alpha",
            playerName: "代码A",
            mora: 1000,
            gold: 1,
            nav: 2000,
            monthlyTradeCount: 0,
            activeCards: ["内幕消息"],
            pendingOrders: [
              {
                orderId: 7,
                side: "Buy",
                price: 999,
                quantity: 2,
                remainingQuantity: 2,
                status: "Pending",
              },
            ],
          },
        ],
      },
    ],
    [
      {
        tick: 3,
        stage: "TradingDay",
        month: 1,
        day: 2,
        tradingDayTick: 2,
        scores: { alpha: 1 },
        marketState: {
          bids: [],
          asks: [],
          lastPrice: 1003,
          midPrice: 1003,
          volume: 2,
        },
        players: [
          {
            playerId: 0,
            token: "alpha",
            playerName: "代码A",
            mora: 1000,
            gold: 1,
            nav: 2003,
            monthlyTradeCount: 1,
            activeCards: ["内幕消息"],
            pendingOrders: [
              {
                orderId: 7,
                side: "Buy",
                price: 999,
                quantity: 2,
                remainingQuantity: 1,
                status: "Active",
              },
            ],
          },
        ],
      },
    ],
  ]);

  const stat = await zipPages([
    [
      { type: "draft", month: 1, offerings: ["内幕消息", "闪电交易"], selections: {} },
      { type: "news", month: 1, newsId: 9, publishTick: 1, content: "supply shock", isFake: false },
      {
        type: "report",
        playerToken: "alpha",
        newsId: 9,
        prediction: "Long",
        submitTick: 1,
        settlementTick: 2,
        submissionRank: 1,
        isCorrect: true,
        reward: 50,
        actualChange: 3,
      },
      {
        type: "skill",
        month: 1,
        tick: 3,
        sourcePlayerId: 0,
        skillName: "闪电交易",
        description: "bonus action",
      },
    ],
  ]);

  const session = await buildReplaySession(replay, stat);
  assert.equal(session.frameCount, 3);
  assert.equal(session.hasStats, true);

  assert.deepEqual(messageTypes(session.frames[0]), ["GAME_STATE", "REPLAY_DRAFT"]);
  assert.ok(messageTypes(session.frames[1]).includes("MARKET_STATE"));
  assert.ok(messageTypes(session.frames[1]).includes("PLAYER_SUMMARY_STATE"));
  assert.ok(messageTypes(session.frames[1]).includes("NEWS_BROADCAST"));
  assert.ok(messageTypes(session.frames[1]).includes("REPLAY_ORDER"));
  assert.equal(
    session.frames[1].messages.find((message) => message.messageType === "PLAYER_SUMMARY_STATE")?.playerName,
    "代码A",
  );
  assert.ok(messageTypes(session.frames[2]).includes("REPORT_RESULT"));
  assert.ok(messageTypes(session.frames[2]).includes("SKILL_EFFECT"));
  assert.equal(messageTypes(session.frames[2]).filter((type) => type === "REPLAY_ORDER").length, 0);
}

function testEmbeddedReplayEventsNormalizeToMessages() {
  const messages = snapshotToMessages({
    tick: 4,
    stage: "TradingDay",
    month: 1,
    day: 3,
    tradingDayTick: 3,
    marketState: { lastPrice: 1001, midPrice: 1001, volume: 1 },
    events: [
      {
        type: "trade",
        tick: 3,
        tradeId: 10,
        buyerPlayerId: 0,
        sellerPlayerId: 1,
        price: 1001,
        quantity: 2,
      },
    ],
  }, [
    {
      type: "trade",
      tick: 3,
      tradeId: 10,
      buyerPlayerId: 0,
      sellerPlayerId: 1,
      price: 1001,
      quantity: 2,
    },
  ]);

  assert.ok(messages.some((message) => message.messageType === "REPLAY_TRADE"));
}

async function zipPages(pages) {
  const zip = new JSZip();
  pages.forEach((page, index) => {
    zip.file(`${index + 1}.json`, JSON.stringify(page));
  });
  return zip.generateAsync({ type: "nodebuffer" });
}

function messageTypes(frame) {
  return frame.messages.map((message) => message.messageType);
}
