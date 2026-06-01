#pragma once

#include <ixwebsocket/IXWebSocket.h>
#include <spdlog/spdlog.h>

#include <atomic>
#include <chrono>
#include <cstdint>
#include <mutex>
#include <nlohmann/json.hpp>
#include <optional>
#include <string>
#include <thread>
#include <utility>
#include <vector>

#include "models.hpp"
#include "protocol.hpp"

namespace thuai {

using json = nlohmann::json;

inline constexpr int kWaitingTimeMs = 100;

class Agent {
 public:
  explicit Agent(std::string token,
                 std::string serverUrl = "ws://localhost:14514",
                 std::optional<std::string> playerName = std::nullopt)
      : token_(std::move(token)),
        serverUrl_(std::move(serverUrl)),
        playerName_(std::move(playerName)) {}

  virtual ~Agent() = default;

  Agent(const Agent&) = delete;
  Agent(Agent&&) = delete;
  auto operator=(const Agent&) -> Agent& = delete;
  auto operator=(Agent&&) -> Agent& = delete;

  // --- Actions ---
  void limitBuy(std::int64_t price, int quantity) {
    spdlog::debug("Queueing limit buy price={} quantity={}", price, quantity);
    send(protocol::buildLimitBuyMessage(token_, price, quantity));
  }

  void limitSell(std::int64_t price, int quantity) {
    spdlog::debug("Queueing limit sell price={} quantity={}", price, quantity);
    send(protocol::buildLimitSellMessage(token_, price, quantity));
  }

  void cancelOrder(std::int64_t orderId) {
    spdlog::debug("Queueing cancel orderId={}", orderId);
    send(protocol::buildCancelOrderMessage(token_, orderId));
  }

  void submitReport(int newsId, Prediction prediction) {
    spdlog::debug("Queueing report newsId={} prediction={}", newsId,
                  predictionToString(prediction));
    send(protocol::buildSubmitReportMessage(token_, newsId, prediction));
  }

  void selectStrategy(const std::string& cardName) {
    spdlog::debug("Queueing strategy selection card={}", cardName);
    send(protocol::buildSelectStrategyMessage(token_, cardName));
  }

  void activateSkill(const std::string& skillName, int targetPlayerId = -1,
                     const std::string& variant = "") {
    spdlog::debug(
        "Queueing skill activation name={} targetPlayerId={} variant={}",
        skillName, targetPlayerId, variant.empty() ? "none" : variant);
    send(protocol::buildActivateSkillMessage(
        token_, skillName,
        targetPlayerId >= 0 ? std::make_optional(targetPlayerId) : std::nullopt,
        variant.empty() ? std::nullopt : std::make_optional(variant)));
  }

  auto getAllPlayerIds() const -> std::vector<int> {
    std::vector<int> ids;
    const std::lock_guard<std::mutex> lock(stateMutex_);
    for (const auto& score : gameState.scores) {
      ids.push_back(score.playerId);
    }
    return ids;
  }

  // --- State ---
  // Reassigned on the WebSocket callback thread; guarded by stateMutex_.
  // Read these only from inside the on* handlers (which receive a copy) or via
  // the locked accessors above, never directly from another thread.
  GameState gameState{};
  MarketState marketState{};
  PlayerState playerState{};
  std::optional<News> latestNews{};
  std::optional<StrategyOptions> strategyOptions{};
  std::optional<DaySettlement> latestDaySettlement{};

  // --- Event Handlers (override what you need) ---
  virtual void onGameState(const GameState&) {}
  virtual void onMarketState(const MarketState&) {}
  virtual void onPlayerState(const PlayerState&) {}
  virtual void onNews(const News&) {}
  virtual void onReportResult(const ReportResult&) {}
  virtual void onStrategyOptions(const StrategyOptions&) {}
  virtual void onTrade(const TradeNotification&) {}
  virtual void onSkillEffect(const SkillEffect&) {}
  virtual void onDaySettlement(const DaySettlement&) {}
  virtual void onError(int, const std::string&) {}

  // --- Run ---
  void run() {
    std::atomic_bool closed{false};

    ws_.setUrl(serverUrl_);
    ws_.setOnMessageCallback(
        [this, &closed](const ix::WebSocketMessagePtr& msg) {
          if (msg->type == ix::WebSocketMessageType::Message) {
            handleMessage(msg->str);
            return;
          }

          if (msg->type == ix::WebSocketMessageType::Open) {
            spdlog::info("Connected to {}", serverUrl_);
            send(protocol::buildHelloMessage(token_, "player", std::nullopt,
                                             playerName_));
            return;
          }

          if (msg->type == ix::WebSocketMessageType::Close) {
            spdlog::info("Disconnected from {}", serverUrl_);
            closed = true;
            return;
          }

          if (msg->type == ix::WebSocketMessageType::Error) {
            spdlog::error("WebSocket error: {}", msg->errorInfo.reason);
            closed = true;
          }
        });
    ws_.start();

    while (!closed) {
      std::this_thread::sleep_for(std::chrono::milliseconds(kWaitingTimeMs));
      bool finished = false;
      {
        const std::lock_guard<std::mutex> lock(stateMutex_);
        finished = gameState.stage == "Finished";
      }
      if (finished) {
        spdlog::info("Game stage is Finished, stopping agent loop");
        break;
      }
    }

    ws_.stop();
    spdlog::info("Agent loop stopped");
  }

 private:
  std::string token_;
  std::string serverUrl_;
  std::optional<std::string> playerName_;
  ix::WebSocket ws_;
  // Serializes the snapshot writes on the WS thread against the reads in run()
  // and getAllPlayerIds(); never held across an on* handler call so user code
  // can re-enter (e.g. send actions) without deadlocking.
  mutable std::mutex stateMutex_;

  void send(const json& data) {
    const auto payload = data.dump();
    spdlog::debug("Sending messageType={} payload={}",
                  data.value("messageType", std::string("UNKNOWN")), payload);
    ws_.send(payload);
  }

  void handleMessage(const std::string& raw) {
    try {
      spdlog::debug("Received raw payload={}", raw);

      const auto data = json::parse(raw);
      const std::string msgType = data.value("messageType", "");

      if (msgType.empty()) {
        spdlog::warn("Received message without messageType payload={}", raw);
        return;
      }

      if (msgType == "GAME_STATE") {
        auto parsed = protocol::parseGameState(data);
        {
          const std::lock_guard<std::mutex> lock(stateMutex_);
          gameState = parsed;
        }
        spdlog::debug(
            "Parsed game state stage={} month={} day={} tick={}/{} "
            "scoreEntries={}",
            parsed.stage, parsed.currentMonth, parsed.currentDay,
            parsed.currentTick, parsed.totalTicks, parsed.scores.size());
        onGameState(parsed);
      } else if (msgType == "MARKET_STATE") {
        auto parsed = protocol::parseMarketState(data);
        {
          const std::lock_guard<std::mutex> lock(stateMutex_);
          marketState = parsed;
        }
        spdlog::debug(
            "Parsed market state tick={} bids={} asks={} lastPrice={} "
            "midPrice={} volume={}",
            parsed.tick, parsed.bids.size(), parsed.asks.size(),
            parsed.lastPrice, parsed.midPrice, parsed.volume);
        onMarketState(parsed);
      } else if (msgType == "PLAYER_STATE") {
        auto parsed = protocol::parsePlayerState(data);
        {
          const std::lock_guard<std::mutex> lock(stateMutex_);
          playerState = parsed;
        }
        spdlog::debug(
            "Parsed player state mora={} frozenMora={} gold={} frozenGold={} "
            "lockedGold={} nav={} pendingOrders={} activeCards={}",
            parsed.mora, parsed.frozenMora, parsed.gold, parsed.frozenGold,
            parsed.lockedGold, parsed.nav, parsed.pendingOrders.size(),
            parsed.activeCards.size());
        onPlayerState(parsed);
      } else if (msgType == "NEWS_BROADCAST") {
        auto parsed = protocol::parseNews(data);
        {
          const std::lock_guard<std::mutex> lock(stateMutex_);
          latestNews = parsed;
        }
        spdlog::debug("Parsed news newsId={} month={} day={} publishTick={}",
                      parsed.newsId, parsed.month, parsed.day,
                      parsed.publishTick);
        onNews(parsed);
      } else if (msgType == "REPORT_RESULT") {
        const auto result = protocol::parseReportResult(data);
        spdlog::debug(
            "Parsed report result newsId={} prediction={} correct={} reward={}",
            result.newsId, result.prediction, result.isCorrect, result.reward);
        onReportResult(result);
      } else if (msgType == "STRATEGY_OPTIONS") {
        auto parsed = protocol::parseStrategyOptions(data);
        {
          const std::lock_guard<std::mutex> lock(stateMutex_);
          strategyOptions = parsed;
        }
        spdlog::debug(
            "Parsed strategy options infrastructure={} riskControl={} "
            "finTech={}",
            parsed.infrastructure.has_value(), parsed.riskControl.has_value(),
            parsed.finTech.has_value());
        onStrategyOptions(parsed);
      } else if (msgType == "TRADE_NOTIFICATION") {
        const auto trade = protocol::parseTrade(data);
        spdlog::debug(
            "Parsed trade tradeId={} orderId={} side={} price={} quantity={} "
            "fee={} tick={}",
            trade.tradeId, trade.orderId, trade.side, trade.price,
            trade.quantity, trade.fee, trade.tick);
        onTrade(trade);
      } else if (msgType == "SKILL_EFFECT") {
        const auto effect = protocol::parseSkillEffect(data);
        spdlog::debug(
            "Parsed skill effect skill={} sourcePlayerId={} targetPlayerId={} "
            "description={}",
            effect.skillName, effect.sourcePlayerId,
            effect.targetPlayerId.has_value()
                ? std::to_string(*effect.targetPlayerId)
                : "none",
            effect.description);
        onSkillEffect(effect);
      } else if (msgType == "DAY_SETTLEMENT") {
        auto parsed = protocol::parseDaySettlement(data);
        {
          const std::lock_guard<std::mutex> lock(stateMutex_);
          latestDaySettlement = parsed;
        }
        spdlog::debug(
            "Parsed day settlement month={} day={} winnerPlayerId={} "
            "players={}",
            parsed.month, parsed.day, parsed.winnerPlayerId,
            parsed.players.size());
        onDaySettlement(parsed);
      } else if (msgType == "ERROR") {
        const int code = data.value("errorCode", 0);
        const std::string message = data.value("message", std::string(""));
        spdlog::warn("Server error message code={} message={}", code, message);
        onError(code, message);
      } else {
        spdlog::warn("Ignoring unknown messageType={} payload={}", msgType,
                     raw);
      }
    } catch (const std::exception& e) {
      spdlog::error("Failed to parse inbound message: {} payload={}", e.what(),
                    raw);
    }
  }
};

}  // namespace thuai
