#pragma once
#include <string>
#include <vector>
#include <optional>

namespace thuai {

struct PriceLevel {
    long price = 0;
    int quantity = 0;
};

struct OrderInfo {
    long orderId = 0;
    std::string side;
    long price = 0;
    int quantity = 0;
    int remainingQuantity = 0;
    std::string status;
};

struct CardOption {
    std::string name;
    std::string description;
    std::string category;
};

struct PlayerScore {
    std::string token;
    int score = 0;
};

struct GameState {
    std::string stage;
    int currentDay = 0;
    int currentTick = 0;
    int totalTicks = 0;
    std::vector<PlayerScore> scores;
};

struct MarketState {
    std::vector<PriceLevel> bids;
    std::vector<PriceLevel> asks;
    long lastPrice = 0;
    long midPrice = 0;
    int volume = 0;
    int tick = 0;
};

struct PlayerState {
    long mora = 0;
    long frozenMora = 0;
    int gold = 0;
    int frozenGold = 0;
    int lockedGold = 0;
    long nav = 0;
    std::vector<std::string> activeCards;
    std::vector<OrderInfo> pendingOrders;
};

struct News {
    int newsId = 0;
    std::string content;
    int publishTick = 0;
};

struct ReportResult {
    int newsId = 0;
    std::string prediction;
    bool isCorrect = false;
    long reward = 0;
    long actualChange = 0;
};

struct StrategyOptions {
    std::optional<CardOption> infrastructure;
    std::optional<CardOption> riskControl;
    std::optional<CardOption> finTech;
};

struct TradeNotification {
    long tradeId = 0;
    long orderId = 0;
    long price = 0;
    int quantity = 0;
    std::string side;
    long fee = 0;
};

struct SkillEffect {
    std::string skillName;
    std::string sourcePlayer;
    std::string description;
};

enum class Prediction { Long, Short, Hold };

inline std::string predictionToString(Prediction p) {
    switch (p) {
        case Prediction::Long: return "Long";
        case Prediction::Short: return "Short";
        case Prediction::Hold: return "Hold";
    }
    return "Hold";
}

} // namespace thuai
