import assert from "node:assert/strict";
import {
  applyMessage,
  applyManagedObserverLiveState,
  applyPlayerMap,
  createInitialState,
  playerDisplayName,
} from "../src/store.js";

testApplyPlayerMapKeysByPlayerId();
testApplyPlayerMapIgnoresBadInput();
testObserverShowsTeamNameNotToken();
testObserverPrefersServerNickname();
testObserverFallsBackToGenericLabel();
testReplayNeverUsesLivePollLabels();
testPlayerMapDoesNotGetClobberedByIdentity();
testPlayerMapDoesNotOverrideServerNickname();
testLegacyPlayerDirectoryShapeDoesNotThrow();
testManagedObserverLiveStatePopulatesRankings();

function observerState() {
  const state = createInitialState({ role: "observer" });
  state.connection.role = "observer";
  return state;
}

function testApplyPlayerMapKeysByPlayerId() {
  const state = observerState();
  applyPlayerMap(state, [
    { player_id: 0, team_id: 7, team_name: "红队" },
    { player_id: 1, team_id: 9, team_name: "蓝队" },
  ]);
  assert.equal(state.playerDirectory.labelsById[0], "红队");
  assert.equal(state.playerDirectory.labelsById[1], "蓝队");
}

function testApplyPlayerMapIgnoresBadInput() {
  const state = observerState();
  applyPlayerMap(state, null);
  applyPlayerMap(state, undefined);
  applyPlayerMap(state, [
    { player_id: -1, team_name: "无效" }, // negative id dropped
    { player_id: 2, team_name: "" }, // empty name dropped
    { team_name: "缺 id" }, // missing id dropped
  ]);
  assert.deepEqual(state.playerDirectory.labelsById, {});
}

function testObserverShowsTeamNameNotToken() {
  const state = observerState();
  applyPlayerMap(state, [{ player_id: 0, team_id: 7, team_name: "红队" }]);
  // Even if a (secret) token is passed in, the observer view must surface the
  // team name and never echo the token.
  assert.equal(playerDisplayName(state, 0, "super-secret-token"), "红队");
}

function testObserverPrefersServerNickname() {
  const state = observerState();
  applyPlayerMap(state, [{ player_id: 0, team_id: 7, team_name: "红队" }]);
  applyMessage(state, {
    messageType: "GAME_STATE",
    stage: "TradingDay",
    currentMonth: 1,
    currentDay: 1,
    currentTick: 1,
    totalTicks: 30,
    scores: [{ playerId: 0, playerName: "代码A", score: 5 }],
  });
  assert.equal(playerDisplayName(state, 0, ""), "代码A");
}

function testObserverFallsBackToGenericLabel() {
  const state = observerState();
  assert.equal(playerDisplayName(state, 3, ""), "选手 3");
  assert.equal(playerDisplayName(state, -1, ""), "-");
}

function testReplayNeverUsesLivePollLabels() {
  const state = observerState();
  // Live poll labelled player 0 as 红队 for the *current* match...
  applyPlayerMap(state, [{ player_id: 0, team_id: 7, team_name: "红队" }]);
  // ...but we are now replaying an unrelated past match.
  state.replay.enabled = true;
  // The replay payload carries its own token — that wins, the live label is ignored.
  assert.equal(playerDisplayName(state, 0, "m5s12"), "m5s12");
  // With no token in the replay payload, fall back to a generic label, NOT 红队.
  assert.equal(playerDisplayName(state, 0, ""), "选手 0");
}

function testPlayerMapDoesNotGetClobberedByIdentity() {
  const state = observerState();
  applyPlayerMap(state, [{ player_id: 0, team_id: 7, team_name: "红队" }]);
  // A later PLAYER_STATE (token-based identity) must not overwrite the team name.
  applyMessage(state, { messageType: "PLAYER_STATE", playerId: 0, token: "tok0" });
  assert.equal(state.playerDirectory.labelsById[0], "红队");
  assert.equal(state.playerDirectory.idsByToken["tok0"], 0);
}

function testPlayerMapDoesNotOverrideServerNickname() {
  const state = observerState();
  applyMessage(state, {
    messageType: "GAME_STATE",
    stage: "TradingDay",
    currentMonth: 1,
    currentDay: 1,
    currentTick: 1,
    totalTicks: 30,
    scores: [{ playerId: 0, playerName: "代码A", score: 5 }],
  });
  applyPlayerMap(state, [{ player_id: 0, team_id: 7, team_name: "红队" }]);
  assert.equal(playerDisplayName(state, 0, ""), "代码A");
}

function testLegacyPlayerDirectoryShapeDoesNotThrow() {
  const state = observerState();
  delete state.playerDirectory.serverLabelsById;
  applyMessage(state, {
    messageType: "GAME_STATE",
    stage: "TradingDay",
    currentMonth: 1,
    currentDay: 1,
    currentTick: 1,
    totalTicks: 30,
    scores: [{ playerId: 0, playerName: "代码A", score: 5 }],
  });
  applyPlayerMap(state, [{ player_id: 1, team_id: 8, team_name: "蓝队" }]);
  assert.equal(playerDisplayName(state, 0, ""), "代码A");
  assert.equal(playerDisplayName(state, 1, ""), "蓝队");
}

function testManagedObserverLiveStatePopulatesRankings() {
  const state = observerState();
  applyManagedObserverLiveState(state, {
    players: [
      {
        player_id: 0,
        team_id: 7,
        team_name: "红队",
        submission_name: "量化一号",
        player_name: "",
        score: "12",
        current_nav: "1002300",
        mora: "900000",
        frozen_mora: "1000",
        gold: 1234,
        frozen_gold: 10,
        locked_gold: 3,
        monthly_trade_count: 5,
        active_cards: ["内幕消息"],
      },
    ],
  });
  assert.equal(playerDisplayName(state, 0, ""), "量化一号");
  assert.equal(state.game.scores[0].score, "12");
  assert.equal(state.playerSummaries["0"].nav, 1002300);
  assert.equal(state.playerSummaries["0"].tradeCount, 5);
}
