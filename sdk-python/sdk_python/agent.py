import asyncio
import json
import logging

from websockets.asyncio.client import connect

from .models import (
    CardOption,
    GameState,
    MarketState,
    News,
    OrderInfo,
    PlayerScore,
    PlayerState,
    Prediction,
    PriceLevel,
    ReportResult,
    SkillEffect,
    StrategyOptions,
    TradeNotification,
)

logger = logging.getLogger("thuai")


class Agent:
    def __init__(self, token: str, server_url: str = "ws://localhost:14514"):
        self.token = token
        self.server_url = server_url
        self._ws = None

        # Current state (updated automatically)
        self.game_state = GameState()
        self.market_state = MarketState()
        self.player_state = PlayerState()
        self.latest_news: News | None = None
        self.strategy_options: StrategyOptions | None = None

    async def connect(self):
        self._ws = await connect(self.server_url)
        logger.info(f"Connected to {self.server_url}")
        await self.cancel_order(-1)

    async def disconnect(self):
        if self._ws:
            await self._ws.close()

    # --- Actions ---

    async def limit_buy(self, price: int, quantity: int):
        await self._send({
            "messageType": "LIMIT_BUY",
            "token": self.token,
            "price": price,
            "quantity": quantity,
        })

    async def limit_sell(self, price: int, quantity: int):
        await self._send({
            "messageType": "LIMIT_SELL",
            "token": self.token,
            "price": price,
            "quantity": quantity,
        })

    async def cancel_order(self, order_id: int):
        await self._send({
            "messageType": "CANCEL_ORDER",
            "token": self.token,
            "orderId": order_id,
        })

    async def submit_report(self, news_id: int, prediction: Prediction):
        await self._send({
            "messageType": "SUBMIT_REPORT",
            "token": self.token,
            "newsId": news_id,
            "prediction": prediction.value,
        })

    async def select_strategy(self, card_name: str):
        await self._send({
            "messageType": "SELECT_STRATEGY",
            "token": self.token,
            "cardName": card_name,
        })

    async def activate_skill(self, skill_name: str, direction: str | None = None):
        msg: dict = {
            "messageType": "ACTIVATE_SKILL",
            "token": self.token,
            "skillName": skill_name,
        }
        if direction:
            msg["direction"] = direction
        await self._send(msg)

    # --- Event Loop ---

    async def run(self):
        """Main loop: connect, receive messages, dispatch to handlers."""
        await self.connect()
        try:
            async for raw in self._ws:
                try:
                    data = json.loads(raw)
                    msg_type = data.get("messageType", "")
                    self._update_state(msg_type, data)
                    await self._dispatch(msg_type, data)
                except json.JSONDecodeError:
                    logger.warning(f"Invalid JSON: {raw}")
                except Exception as e:
                    logger.error(f"Error handling message: {e}")
        except Exception as e:
            logger.error(f"Connection error: {e}")
        finally:
            await self.disconnect()

    # --- Override these in your agent ---

    async def on_game_state(self, state: GameState):
        pass

    async def on_market_state(self, state: MarketState):
        pass

    async def on_player_state(self, state: PlayerState):
        pass

    async def on_news(self, news: News):
        pass

    async def on_report_result(self, result: ReportResult):
        pass

    async def on_strategy_options(self, options: StrategyOptions):
        pass

    async def on_trade(self, trade: TradeNotification):
        pass

    async def on_skill_effect(self, effect: SkillEffect):
        pass

    async def on_error(self, code: int, message: str):
        pass

    # --- Internal ---

    async def _send(self, data: dict):
        if self._ws:
            await self._ws.send(json.dumps(data))

    def _update_state(self, msg_type: str, data: dict):
        if msg_type == "GAME_STATE":
            self.game_state = _parse_game_state(data)
        elif msg_type == "MARKET_STATE":
            self.market_state = _parse_market_state(data)
        elif msg_type == "PLAYER_STATE":
            self.player_state = _parse_player_state(data)
        elif msg_type == "NEWS_BROADCAST":
            self.latest_news = _parse_news(data)
        elif msg_type == "STRATEGY_OPTIONS":
            self.strategy_options = _parse_strategy_options(data)

    async def _dispatch(self, msg_type: str, data: dict):
        if msg_type == "GAME_STATE":
            await self.on_game_state(self.game_state)
        elif msg_type == "MARKET_STATE":
            await self.on_market_state(self.market_state)
        elif msg_type == "PLAYER_STATE":
            await self.on_player_state(self.player_state)
        elif msg_type == "NEWS_BROADCAST":
            await self.on_news(self.latest_news)
        elif msg_type == "REPORT_RESULT":
            await self.on_report_result(_parse_report_result(data))
        elif msg_type == "STRATEGY_OPTIONS":
            await self.on_strategy_options(self.strategy_options)
        elif msg_type == "TRADE_NOTIFICATION":
            await self.on_trade(_parse_trade(data))
        elif msg_type == "SKILL_EFFECT":
            await self.on_skill_effect(_parse_skill_effect(data))
        elif msg_type == "ERROR":
            await self.on_error(data.get("errorCode", 0), data.get("message", ""))


# --- Parsers ---


def _parse_game_state(data: dict) -> GameState:
    scores = [
        PlayerScore(s["token"], s["score"])
        for s in data.get("scores", []) or []
    ]
    return GameState(
        stage=data.get("stage", ""),
        current_day=data.get("currentDay", 0),
        current_tick=data.get("currentTick", 0),
        total_ticks=data.get("totalTicks", 0),
        scores=scores,
    )


def _parse_market_state(data: dict) -> MarketState:
    bids = [
        PriceLevel(b["price"], b["quantity"])
        for b in data.get("bids", []) or []
    ]
    asks = [
        PriceLevel(a["price"], a["quantity"])
        for a in data.get("asks", []) or []
    ]
    return MarketState(
        bids=bids,
        asks=asks,
        last_price=data.get("lastPrice", 0),
        mid_price=data.get("midPrice", 0),
        volume=data.get("volume", 0),
        tick=data.get("tick", 0),
    )


def _parse_player_state(data: dict) -> PlayerState:
    orders = [
        OrderInfo(
            o["orderId"],
            o["side"],
            o["price"],
            o["quantity"],
            o["remainingQuantity"],
            o["status"],
        )
        for o in data.get("pendingOrders", []) or []
    ]
    return PlayerState(
        mora=data.get("mora", 0),
        frozen_mora=data.get("frozenMora", 0),
        gold=data.get("gold", 0),
        frozen_gold=data.get("frozenGold", 0),
        locked_gold=data.get("lockedGold", 0),
        nav=data.get("nav", 0),
        active_cards=data.get("activeCards", []) or [],
        pending_orders=orders,
    )


def _parse_news(data: dict) -> News:
    return News(
        data.get("newsId", 0),
        data.get("content", ""),
        data.get("publishTick", 0),
    )


def _parse_report_result(data: dict) -> ReportResult:
    return ReportResult(
        data.get("newsId", 0),
        data.get("prediction", ""),
        data.get("isCorrect", False),
        data.get("reward", 0),
        data.get("actualChange", 0),
    )


def _parse_strategy_options(data: dict) -> StrategyOptions:
    def parse_card(d):
        if not d:
            return None
        return CardOption(d.get("name", ""), d.get("description", ""), d.get("category", ""))

    return StrategyOptions(
        parse_card(data.get("infrastructure")),
        parse_card(data.get("riskControl")),
        parse_card(data.get("finTech")),
    )


def _parse_trade(data: dict) -> TradeNotification:
    return TradeNotification(
        data.get("tradeId", 0),
        data.get("orderId", 0),
        data.get("price", 0),
        data.get("quantity", 0),
        data.get("side", ""),
        data.get("fee", 0),
    )


def _parse_skill_effect(data: dict) -> SkillEffect:
    return SkillEffect(
        data.get("skillName", ""),
        data.get("sourcePlayer", ""),
        data.get("description", ""),
    )
