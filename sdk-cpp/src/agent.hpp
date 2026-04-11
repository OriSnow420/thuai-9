#pragma once
#include <string>
#include <functional>
#include <iostream>
#include <thread>
#include <chrono>
#include <nlohmann/json.hpp>
#include <ixwebsocket/IXWebSocket.h>
#include "models.hpp"

namespace thuai {

using json = nlohmann::json;

class Agent {
public:
    Agent(const std::string& token, const std::string& serverUrl = "ws://localhost:14514")
        : token_(token), serverUrl_(serverUrl) {}

    virtual ~Agent() = default;

    // --- Actions ---
    void limitBuy(long price, int quantity) {
        send({{"messageType", "LIMIT_BUY"}, {"token", token_}, {"price", price}, {"quantity", quantity}});
    }

    void limitSell(long price, int quantity) {
        send({{"messageType", "LIMIT_SELL"}, {"token", token_}, {"price", price}, {"quantity", quantity}});
    }

    void cancelOrder(long orderId) {
        send({{"messageType", "CANCEL_ORDER"}, {"token", token_}, {"orderId", orderId}});
    }

    void submitReport(int newsId, Prediction prediction) {
        send({{"messageType", "SUBMIT_REPORT"}, {"token", token_}, {"newsId", newsId}, {"prediction", predictionToString(prediction)}});
    }

    void selectStrategy(const std::string& cardName) {
        send({{"messageType", "SELECT_STRATEGY"}, {"token", token_}, {"cardName", cardName}});
    }

    void activateSkill(const std::string& skillName, const std::string& direction = "") {
        json msg = {{"messageType", "ACTIVATE_SKILL"}, {"token", token_}, {"skillName", skillName}};
        if (!direction.empty()) msg["direction"] = direction;
        send(msg);
    }

    // --- State ---
    GameState gameState;
    MarketState marketState;
    PlayerState playerState;
    std::optional<News> latestNews;
    std::optional<StrategyOptions> strategyOptions;

    // --- Event Handlers (override these) ---
    virtual void onGameState(const GameState&) {}
    virtual void onMarketState(const MarketState&) {}
    virtual void onPlayerState(const PlayerState&) {}
    virtual void onNews(const News&) {}
    virtual void onReportResult(const ReportResult&) {}
    virtual void onStrategyOptions(const StrategyOptions&) {}
    virtual void onTrade(const TradeNotification&) {}
    virtual void onSkillEffect(const SkillEffect&) {}
    virtual void onError(int code, const std::string& message) {}

    // --- Run ---
    void run() {
        ws_.setUrl(serverUrl_);
        ws_.setOnMessageCallback([this](const ix::WebSocketMessagePtr& msg) {
            if (msg->type == ix::WebSocketMessageType::Message) {
                handleMessage(msg->str);
            } else if (msg->type == ix::WebSocketMessageType::Open) {
                std::cout << "[Agent] Connected to " << serverUrl_ << std::endl;
            } else if (msg->type == ix::WebSocketMessageType::Close) {
                std::cout << "[Agent] Disconnected" << std::endl;
            } else if (msg->type == ix::WebSocketMessageType::Error) {
                std::cerr << "[Agent] Error: " << msg->errorInfo.reason << std::endl;
            }
        });
        ws_.start();

        // Block until disconnected or game ends
        while (ws_.getReadyState() != ix::ReadyState::Closed) {
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
            if (gameState.stage == "Finished") break;
        }
        ws_.stop();
    }

private:
    std::string token_;
    std::string serverUrl_;
    ix::WebSocket ws_;

    void send(const json& data) {
        ws_.send(data.dump());
    }

    void handleMessage(const std::string& raw) {
        try {
            auto data = json::parse(raw);
            std::string msgType = data.value("messageType", "");

            if (msgType == "GAME_STATE") {
                gameState = parseGameState(data);
                onGameState(gameState);
            } else if (msgType == "MARKET_STATE") {
                marketState = parseMarketState(data);
                onMarketState(marketState);
            } else if (msgType == "PLAYER_STATE") {
                playerState = parsePlayerState(data);
                onPlayerState(playerState);
            } else if (msgType == "NEWS_BROADCAST") {
                latestNews = parseNews(data);
                onNews(*latestNews);
            } else if (msgType == "REPORT_RESULT") {
                onReportResult(parseReportResult(data));
            } else if (msgType == "STRATEGY_OPTIONS") {
                strategyOptions = parseStrategyOptions(data);
                onStrategyOptions(*strategyOptions);
            } else if (msgType == "TRADE_NOTIFICATION") {
                onTrade(parseTrade(data));
            } else if (msgType == "SKILL_EFFECT") {
                onSkillEffect(parseSkillEffect(data));
            } else if (msgType == "ERROR") {
                onError(data.value("errorCode", 0), data.value("message", std::string("")));
            }
        } catch (const std::exception& e) {
            std::cerr << "[Agent] Parse error: " << e.what() << std::endl;
        }
    }

    // --- Parsers ---
    static GameState parseGameState(const json& d) {
        GameState state;
        state.stage = d.value("stage", "");
        state.currentDay = d.value("currentDay", 0);
        state.currentTick = d.value("currentTick", 0);
        state.totalTicks = d.value("totalTicks", 0);
        if (d.contains("scores") && d["scores"].is_array()) {
            for (const auto& s : d["scores"]) {
                PlayerScore ps;
                ps.token = s.value("token", "");
                ps.score = s.value("score", 0);
                state.scores.push_back(ps);
            }
        }
        return state;
    }

    static MarketState parseMarketState(const json& d) {
        MarketState state;
        if (d.contains("bids") && d["bids"].is_array()) {
            for (const auto& b : d["bids"]) {
                PriceLevel pl;
                pl.price = b.value("price", 0L);
                pl.quantity = b.value("quantity", 0);
                state.bids.push_back(pl);
            }
        }
        if (d.contains("asks") && d["asks"].is_array()) {
            for (const auto& a : d["asks"]) {
                PriceLevel pl;
                pl.price = a.value("price", 0L);
                pl.quantity = a.value("quantity", 0);
                state.asks.push_back(pl);
            }
        }
        state.lastPrice = d.value("lastPrice", 0L);
        state.midPrice = d.value("midPrice", 0L);
        state.volume = d.value("volume", 0);
        state.tick = d.value("tick", 0);
        return state;
    }

    static PlayerState parsePlayerState(const json& d) {
        PlayerState state;
        state.mora = d.value("mora", 0L);
        state.frozenMora = d.value("frozenMora", 0L);
        state.gold = d.value("gold", 0);
        state.frozenGold = d.value("frozenGold", 0);
        state.lockedGold = d.value("lockedGold", 0);
        state.nav = d.value("nav", 0L);
        if (d.contains("activeCards") && d["activeCards"].is_array()) {
            for (const auto& c : d["activeCards"]) {
                if (c.is_string()) {
                    state.activeCards.push_back(c.get<std::string>());
                }
            }
        }
        if (d.contains("pendingOrders") && d["pendingOrders"].is_array()) {
            for (const auto& o : d["pendingOrders"]) {
                OrderInfo oi;
                oi.orderId = o.value("orderId", 0L);
                oi.side = o.value("side", "");
                oi.price = o.value("price", 0L);
                oi.quantity = o.value("quantity", 0);
                oi.remainingQuantity = o.value("remainingQuantity", 0);
                oi.status = o.value("status", "");
                state.pendingOrders.push_back(oi);
            }
        }
        return state;
    }

    static News parseNews(const json& d) {
        News news;
        news.newsId = d.value("newsId", 0);
        news.content = d.value("content", "");
        news.publishTick = d.value("publishTick", 0);
        return news;
    }

    static ReportResult parseReportResult(const json& d) {
        ReportResult result;
        result.newsId = d.value("newsId", 0);
        result.prediction = d.value("prediction", "");
        result.isCorrect = d.value("isCorrect", false);
        result.reward = d.value("reward", 0L);
        result.actualChange = d.value("actualChange", 0L);
        return result;
    }

    static StrategyOptions parseStrategyOptions(const json& d) {
        StrategyOptions opts;
        auto parseCard = [](const json& c) -> std::optional<CardOption> {
            if (c.is_null() || !c.is_object()) return std::nullopt;
            CardOption card;
            card.name = c.value("name", "");
            card.description = c.value("description", "");
            card.category = c.value("category", "");
            return card;
        };
        if (d.contains("infrastructure")) opts.infrastructure = parseCard(d["infrastructure"]);
        if (d.contains("riskControl")) opts.riskControl = parseCard(d["riskControl"]);
        if (d.contains("finTech")) opts.finTech = parseCard(d["finTech"]);
        return opts;
    }

    static TradeNotification parseTrade(const json& d) {
        TradeNotification trade;
        trade.tradeId = d.value("tradeId", 0L);
        trade.orderId = d.value("orderId", 0L);
        trade.price = d.value("price", 0L);
        trade.quantity = d.value("quantity", 0);
        trade.side = d.value("side", "");
        trade.fee = d.value("fee", 0L);
        return trade;
    }

    static SkillEffect parseSkillEffect(const json& d) {
        SkillEffect effect;
        effect.skillName = d.value("skillName", "");
        effect.sourcePlayer = d.value("sourcePlayer", "");
        effect.description = d.value("description", "");
        return effect;
    }
};

} // namespace thuai
