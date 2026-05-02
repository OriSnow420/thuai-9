# THUAI-9 C++ SDK

C++ SDK for building AI agents that connect to the THUAI-9 game server (璃月黄金交易所 — gold trading exchange).

---

## Quick Start

### Build (XMake required)

```bash
cd sdk-cpp
xmake
```

The binary is built to `bin/agent`.

### Run

```bash
TOKEN=player1 SERVER=ws://localhost:14514 ./bin/agent
```

### Docker

```bash
docker build -t thuai9-agent .
docker run -e TOKEN=player1 -e SERVER=ws://server:14514 thuai9-agent
```

---

## Building Your Agent

Subclass `thuai::Agent` and override the event handlers:

```cpp
#include "agent.hpp"
#include <cstdlib>

class MyAgent : public thuai::Agent {
public:
    using Agent::Agent;

    void onMarketState(const thuai::MarketState& state) override {
        if (!state.bids.empty() && !state.asks.empty()) {
            long spread = state.asks[0].price - state.bids[0].price;
            if (spread > 5 && playerState.gold > 0) {
                limitSell(state.asks[0].price, 1);
            }
        }
    }

    void onNews(const thuai::News& news) override {
        std::cout << "News: " << news.content << std::endl;
        submitReport(news.newsId, thuai::Prediction::Long);
    }

    void onStrategyOptions(const thuai::StrategyOptions& options) override {
        if (options.finTech) {
            selectStrategy(options.finTech->name);
        } else if (options.infrastructure) {
            selectStrategy(options.infrastructure->name);
        }
    }
};

int main() {
    const char* token = std::getenv("TOKEN");
    const char* server = std::getenv("SERVER");

    MyAgent agent(
        token ? token : "player1",
        server ? server : "ws://localhost:14514"
    );
    agent.run();
    return 0;
}
```

---

## API Reference

### `thuai::Agent`

Header-only base class. Connects via WebSocket, dispatches events.

#### Auto-Tracked State

| Member            | Type                             | Description                     |
| ----------------- | -------------------------------- | ------------------------------- |
| `gameState`       | `GameState`                      | Stage, day, tick, scores        |
| `marketState`     | `MarketState`                    | Order book + prices             |
| `playerState`     | `PlayerState`                    | Your assets, NAV, orders, cards |
| `latestNews`      | `std::optional<News>`            | Last received news              |
| `strategyOptions` | `std::optional<StrategyOptions>` | Available cards during draft    |

#### Action Methods

```cpp
void limitBuy(long price, int quantity);
void limitSell(long price, int quantity);
void cancelOrder(long orderId);
void submitReport(int newsId, Prediction prediction);
void selectStrategy(const std::string& cardName);
void activateSkill(const std::string& skillName, const std::string& direction = "");
```

#### Event Handlers (virtual, override)

```cpp
virtual void onGameState(const GameState&);
virtual void onMarketState(const MarketState&);
virtual void onPlayerState(const PlayerState&);
virtual void onNews(const News&);
virtual void onReportResult(const ReportResult&);
virtual void onStrategyOptions(const StrategyOptions&);
virtual void onTrade(const TradeNotification&);
virtual void onSkillEffect(const SkillEffect&);
virtual void onError(int code, const std::string& message);
```

#### Run

```cpp
agent.run();  // connect + blocking event loop until game ends
```

---

## Data Structures

All structs are POD-style with default values. See `src/models.hpp`.

### `PlayerState` (private to you)

```cpp
struct PlayerState {
    long mora = 0;          // available
    long frozenMora = 0;    // locked in buy orders
    int gold = 0;           // available
    int frozenGold = 0;     // locked in sell orders
    int lockedGold = 0;     // from 定向增发, locked 300 ticks
    long nav = 0;           // net asset value
    std::vector<std::string> activeCards;
    std::vector<OrderInfo> pendingOrders;
};
```

### `MarketState`

```cpp
struct MarketState {
    std::vector<PriceLevel> bids;  // descending by price
    std::vector<PriceLevel> asks;  // ascending by price
    long lastPrice = 0;
    long midPrice = 0;
    int volume = 0;
    int tick = 0;
};
```

### `Prediction` (enum)

```cpp
enum class Prediction { Long, Short, Hold };
```

---

## Strategy Cards

| Card Name | Type             | Direction          | Notes                                         |
| --------- | ---------------- | ------------------ | --------------------------------------------- |
| 闪电交易  | Active (1/day)   | —                  | Network delay → 0 for 50 ticks                |
| 定向增发  | Active (1/day)   | —                  | Buy 500 gold at 2% discount, locked 300 ticks |
| 恶意做空  | Active (CD 600)  | —                  | Fake sell orders for 10 ticks                 |
| 拔网线    | Active (CD 1000) | —                  | Exchange freeze 20 ticks                      |
| 暗池交易  | Active (CD 800)  | `"buy"` / `"sell"` | 100 units at mid-price                        |
| 舆情干预  | Active (CD 1200) | —                  | Inject fake news                              |

Passive cards take effect automatically.

---

## Dependencies

- **C++17**
- **ixwebsocket** — WebSocket client
- **nlohmann/json** — JSON parsing

Both are pulled in automatically by XMake.

---

## Layout

```plain
sdk-cpp/
├── src/
│   ├── main.cpp        # Example agent
│   ├── agent.hpp       # Agent base class (header-only)
│   └── models.hpp      # Data structures
├── xmake.lua
└── Dockerfile
```

GPLv3
