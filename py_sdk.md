# THUAI-9 Python SDK

Python SDK for building AI agents that connect to the THUAI-9 game server (璃月黄金交易所 — gold trading exchange).

---

## Quick Start

### Install

```bash
# Using PDM
pdm install

# Or pip
pip install websockets
```

### Run the example agent

```bash
TOKEN=player1 SERVER=ws://localhost:14514 python main.py
```

---

## Building Your Agent

Subclass `Agent` and override the event handlers you care about:

```python
import asyncio
import os
from sdk_python.agent import Agent
from sdk_python.models import GameState, MarketState, PlayerState, News, StrategyOptions, Prediction

class MyAgent(Agent):
    async def on_market_state(self, state: MarketState):
        if state.bids and state.asks:
            spread = state.asks[0].price - state.bids[0].price
            if spread > 5 and self.player_state.gold > 0:
                await self.limit_sell(state.asks[0].price, 1)

    async def on_news(self, news: News):
        await self.submit_report(news.news_id, Prediction.LONG)

    async def on_strategy_options(self, options: StrategyOptions):
        if options.fin_tech:
            await self.select_strategy(options.fin_tech.name)
        elif options.infrastructure:
            await self.select_strategy(options.infrastructure.name)

async def main():
    agent = MyAgent(
        token=os.environ.get("TOKEN", "player1"),
        server_url=os.environ.get("SERVER", "ws://localhost:14514"),
    )
    await agent.run()

if __name__ == "__main__":
    asyncio.run(main())
```

---

## API Reference

### `Agent(token, server_url)`

Base class. Connects to the server, receives messages, dispatches events.

#### Auto-Tracked State

| Property                 | Type                      | Description                                    |
| ------------------------ | ------------------------- | ---------------------------------------------- |
| `agent.game_state`       | `GameState`               | Stage, day, tick, scoreboard                   |
| `agent.market_state`     | `MarketState`             | Order book + prices                            |
| `agent.player_state`     | `PlayerState`             | Your assets, NAV, pending orders, active cards |
| `agent.latest_news`      | `News \| None`            | Last received news                             |
| `agent.strategy_options` | `StrategyOptions \| None` | Cards available during draft                   |

#### Action Methods

```python
await agent.limit_buy(price: int, quantity: int)
await agent.limit_sell(price: int, quantity: int)
await agent.cancel_order(order_id: int)
await agent.submit_report(news_id: int, prediction: Prediction)
await agent.select_strategy(card_name: str)
await agent.activate_skill(skill_name: str, direction: str | None = None)
```

#### Event Handlers (override these)

```python
async def on_game_state(self, state: GameState): ...
async def on_market_state(self, state: MarketState): ...
async def on_player_state(self, state: PlayerState): ...
async def on_news(self, news: News): ...
async def on_report_result(self, result: ReportResult): ...
async def on_strategy_options(self, options: StrategyOptions): ...
async def on_trade(self, trade: TradeNotification): ...
async def on_skill_effect(self, effect: SkillEffect): ...
async def on_error(self, code: int, message: str): ...
```

#### Run

```python
await agent.run()  # connect + event loop, blocks until disconnected
```

---

## Data Models

All models are dataclasses. Wire format uses camelCase, Python uses snake_case (auto-converted).

### `PlayerState` (private to you)

```python
mora: int               # available Mora
frozen_mora: int        # locked in pending buy orders
gold: int               # available gold
frozen_gold: int        # locked in pending sell orders
locked_gold: int        # gold from 定向增发, unlocks after 300 ticks
nav: int                # net asset value (mora + frozen_mora + (gold + ...) * mid_price)
active_cards: list[str]
pending_orders: list[OrderInfo]
```

### `MarketState`

```python
bids: list[PriceLevel]  # descending by price
asks: list[PriceLevel]  # ascending by price
last_price: int
mid_price: int
volume: int
tick: int
```

### `Prediction` (enum)

```python
Prediction.LONG     # predict price will go up
Prediction.SHORT    # predict price will go down
Prediction.HOLD     # no prediction (safe)
```

See `sdk_python/models.py` for all types.

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

Passive cards (高频专线, 低延迟主板, 内幕消息, 量化集群, 免流协议, 冰山订单, 止损名刀) take effect automatically.

---

## Layout

```plain
sdk-python/
├── sdk_python/
│   ├── __init__.py
│   ├── agent.py
│   └── models.py
├── main.py
├── pyproject.toml
└── Dockerfile
```

GPLv3
