# WebSocket API 协议规范

本文档定义 Web 前端和选手 SDK 使用的 WebSocket 协议。当前后端已经实现基础 player 动作与部分广播；标记为“建议新增”的消息是 Web 展示所需的 v1 目标协议。

## 概述

- 默认地址：`ws://localhost:14514`。
- 路径：根路径直连。
- 数据格式：JSON。
- 字段命名：camelCase。
- 分发字段：`messageType`。
- 空字段：服务端 C# 序列化会忽略 `null` 字段。

消息基类见 `server/src/thuai/Protocol/Messages/Message.cs`。

## 实现状态

| 能力 | 当前状态 | 说明 |
| --- | --- | --- |
| Player 动作消息 | 已有 | `LIMIT_BUY`、`LIMIT_SELL`、`CANCEL_ORDER`、`SUBMIT_REPORT`、`SELECT_STRATEGY`、`ACTIVATE_SKILL` |
| Player 懒绑定 | 已有 | 第一条带 `token` 的动作消息会绑定 socket |
| `GAME_STATE` | 已发送 | Tick 后广播给已绑定 player |
| `MARKET_STATE` | 已发送 | TradingDay 阶段按 player 视角发送 |
| `PLAYER_STATE` | 已发送 | TradingDay 阶段发给对应 player |
| `STRATEGY_OPTIONS` | 已发送 | StrategySelection 阶段广播给已绑定 player |
| `NEWS_BROADCAST` | 部分接线 | 主要用于内幕消息预览路径 |
| `REPORT_RESULT` | schema 已有 | 需要补真实发送链路 |
| `TRADE_NOTIFICATION` | schema 已有 | 需要补真实发送链路 |
| `SKILL_EFFECT` | schema 已有 | 需要补真实发送链路 |
| `ERROR` | schema 已有 | 需要补失败路径返回 |
| `HELLO` / `HELLO_ACK` | 建议新增 | 支持显式握手与 observer |
| Observer 连接 | 建议新增 | 需要独立 socket 管理 |
| `PLAYER_SUMMARY_STATE` | 建议新增 | 供 observer 展示摘要 |
| `DAY_SETTLEMENT` | 建议新增 | 供结算弹层展示 |

## 连接生命周期

目标 v1 生命周期：

1. Client 建立 WebSocket。
2. Client 发送 `HELLO`。
3. Server 返回 `HELLO_ACK`。
4. Server 立即补发当前快照。
5. Server 每 Tick 推送 snapshot。
6. Server 在领域事件发生时推送 event。
7. Client 断线后重连，并重新发送 `HELLO`。

兼容策略：

- 老 SDK 可以继续不发送 `HELLO`。
- 老 SDK 第一条动作消息带 `token` 时，服务端仍进行 player 懒绑定。
- 新 Web 前端应优先发送 `HELLO`。如果服务端未支持，会进入 legacy 状态并等待后续消息。

## 角色

### player

Player 连接代表一个选手。

- `HELLO.role = "player"`。
- `HELLO.token` 必填。
- 可以接收公共状态和自己的 `PLAYER_STATE`。
- 可以发送动作消息。

### observer

Observer 连接代表观战端。

- `HELLO.role = "observer"`。
- 不需要 token。
- 只接收公共状态、公共盘口、玩家摘要和事件。
- 不允许发送交易、研报、策略和技能动作。

## 通用字段

- `price`：整数摩拉价格。
- `quantity`：整数黄金数量。
- `tick`：交易日内 Tick，主要来自 `MARKET_STATE.tick`。
- `currentTick`：整场比赛全局 Tick，来自 `GAME_STATE.currentTick`。
- `totalTicks`：当前实现中 TradingDay 阶段等于日内 Tick，非 TradingDay 为 0；该字段语义有历史歧义，Web 端优先使用新增 `dayTick` / `stageTick`。
- `volume`：当日累计成交量，不是单 Tick 成交量。
- `prediction`：`Long`、`Short`、`Hold`。
- `side`：成交/订单方向，当前 SDK 直接透传字符串。
- `token`：选手标识。

## Client -> Server

### HELLO（建议新增）

用于显式声明连接角色。

Player：

```json
{
  "messageType": "HELLO",
  "role": "player",
  "token": "player1",
  "protocolVersion": "v1"
}
```

Observer：

```json
{
  "messageType": "HELLO",
  "role": "observer",
  "protocolVersion": "v1"
}
```

字段：

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `messageType` | string | 是 | 固定为 `HELLO` |
| `role` | string | 是 | `player` 或 `observer` |
| `token` | string | player 必填 | observer 不需要 |
| `protocolVersion` | string | 否 | 建议为 `v1` |

规则：

- `role = "player"` 时，服务端校验 token 并绑定 socket。
- `role = "observer"` 时，服务端加入 observer socket 集合。
- 重复 `HELLO` 应返回当前绑定状态或 `ERROR`，不要重复创建连接状态。

### LIMIT_BUY

提交限价买单。

```json
{
  "messageType": "LIMIT_BUY",
  "token": "player1",
  "price": 2000,
  "quantity": 10
}
```

### LIMIT_SELL

提交限价卖单。

```json
{
  "messageType": "LIMIT_SELL",
  "token": "player1",
  "price": 2010,
  "quantity": 10
}
```

### CANCEL_ORDER

撤销未完成挂单。

```json
{
  "messageType": "CANCEL_ORDER",
  "token": "player1",
  "orderId": 123
}
```

### SUBMIT_REPORT

提交新闻研报。

```json
{
  "messageType": "SUBMIT_REPORT",
  "token": "player1",
  "newsId": 5,
  "prediction": "Long"
}
```

`prediction` 取值：

- `Long`：做多。
- `Short`：做空。
- `Hold`：观望。

### SELECT_STRATEGY

选择策略卡。

```json
{
  "messageType": "SELECT_STRATEGY",
  "token": "player1",
  "cardName": "内幕消息"
}
```

### ACTIVATE_SKILL

激活主动技能。

```json
{
  "messageType": "ACTIVATE_SKILL",
  "token": "player1",
  "skillName": "暗池交易",
  "direction": "buy"
}
```

`direction` 当前主要用于“暗池交易”，取 `buy` 或 `sell`。其他技能可以省略。

## Server -> Client

### HELLO_ACK（建议新增）

握手成功响应。

```json
{
  "messageType": "HELLO_ACK",
  "role": "player",
  "token": "player1",
  "protocolVersion": "v1",
  "capabilities": ["gameState", "marketState", "playerState", "actions"]
}
```

### GAME_STATE

全局比赛状态。

```json
{
  "messageType": "GAME_STATE",
  "stage": "TradingDay",
  "currentDay": 1,
  "currentTick": 345,
  "totalTicks": 345,
  "scores": [
    { "token": "player1", "score": 1 },
    { "token": "player2", "score": 0 }
  ]
}
```

当前字段：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `stage` | string | `Waiting`、`PreparingGame`、`StrategySelection`、`TradingDay`、`Settlement`、`Finished` |
| `currentDay` | number | 当前交易日，1 起始 |
| `currentTick` | number | 全局 Tick |
| `totalTicks` | number | 当前实现中 TradingDay 阶段为日内 Tick |
| `scores` | array | 选手比分 |

建议补充字段：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `stageTick` | number | 当前阶段内 Tick |
| `stageTickLimit` | number | 当前阶段 Tick 上限 |
| `dayTick` | number | 当前交易日内 Tick |
| `dayTickLimit` | number | 交易日 Tick 上限，默认 2000 |

### MARKET_STATE

市场快照。

```json
{
  "messageType": "MARKET_STATE",
  "bids": [{ "price": 1998, "quantity": 30 }],
  "asks": [{ "price": 2002, "quantity": 25 }],
  "lastPrice": 2000,
  "midPrice": 2000,
  "volume": 152,
  "tick": 345
}
```

说明：

- `bids` 和 `asks` 是可见盘口档位。
- Player 视角可能混入对手技能造成的伪盘口。
- Observer 视角应使用公共真实盘口。
- `volume` 是当日累计成交量。

### PLAYER_STATE

私有玩家状态，只发给对应 player。

```json
{
  "messageType": "PLAYER_STATE",
  "mora": 980000,
  "frozenMora": 20000,
  "gold": 1005,
  "frozenGold": 10,
  "lockedGold": 500,
  "nav": 1990000,
  "activeCards": ["内幕消息"],
  "pendingOrders": [
    {
      "orderId": 123,
      "side": "Buy",
      "price": 1998,
      "quantity": 10,
      "remainingQuantity": 4,
      "status": "Pending"
    }
  ]
}
```

建议补充 `skillStates` 或 `activeCardStates`，用于展示主动技能 CD 和持续时间。

### PLAYER_SUMMARY_STATE（建议新增）

Observer 玩家摘要。

```json
{
  "messageType": "PLAYER_SUMMARY_STATE",
  "token": "player1",
  "mora": 980000,
  "frozenMora": 20000,
  "gold": 1005,
  "frozenGold": 10,
  "lockedGold": 500,
  "nav": 1990000,
  "activeCards": ["内幕消息"],
  "pendingOrderCount": 1,
  "tradeCount": 17
}
```

### NEWS_BROADCAST

新闻广播。

```json
{
  "messageType": "NEWS_BROADCAST",
  "newsId": 5,
  "content": "璃月矿区运输恢复，黄金供给预期改善。",
  "publishTick": 320
}
```

建议对 fake news 补充：

- `isFake`。
- `sourcePlayer`。

公开新闻应发给所有 player 和 observer；内幕预览只能发给持有对应卡牌的 player。

### REPORT_RESULT

研报结算结果。

```json
{
  "messageType": "REPORT_RESULT",
  "newsId": 5,
  "prediction": "Long",
  "isCorrect": true,
  "reward": 12000,
  "actualChange": 8
}
```

Observer 版建议补充：

```json
{
  "messageType": "REPORT_RESULT",
  "playerToken": "player1",
  "newsId": 5,
  "prediction": "Long",
  "isCorrect": true,
  "reward": 12000,
  "actualChange": 8
}
```

### STRATEGY_OPTIONS

策略候选。

```json
{
  "messageType": "STRATEGY_OPTIONS",
  "infrastructure": {
    "name": "内幕消息",
    "description": "提前收到新闻预览。",
    "category": "Infrastructure"
  },
  "riskControl": {
    "name": "冰山订单",
    "description": "隐藏大部分挂单数量。",
    "category": "RiskControl"
  },
  "finTech": {
    "name": "暗池交易",
    "description": "按中间价与系统成交。",
    "category": "FinTech"
  }
}
```

### TRADE_NOTIFICATION

成交通知。

```json
{
  "messageType": "TRADE_NOTIFICATION",
  "tradeId": 1001,
  "orderId": 123,
  "price": 2000,
  "quantity": 5,
  "side": "Buy",
  "fee": 2
}
```

当前 schema 更适合 player 私有通知。Observer 简化成交事件建议新增：

```json
{
  "messageType": "TRADE_NOTIFICATION",
  "tradeId": 1001,
  "price": 2000,
  "quantity": 5,
  "tick": 345,
  "buyerToken": "player1",
  "sellerToken": "player2",
  "isWashTrade": false
}
```

### SKILL_EFFECT

技能效果。

```json
{
  "messageType": "SKILL_EFFECT",
  "skillName": "拔网线",
  "sourcePlayer": "player1",
  "description": "交易所进入熔断状态 20 Tick。"
}
```

### DAY_SETTLEMENT（建议新增）

单日结算。

```json
{
  "messageType": "DAY_SETTLEMENT",
  "day": 1,
  "winnerToken": "player1",
  "reason": "NAV",
  "scores": [
    { "token": "player1", "score": 1 },
    { "token": "player2", "score": 0 }
  ],
  "players": [
    { "token": "player1", "nav": 2034500, "tradeCount": 17 },
    { "token": "player2", "nav": 1992000, "tradeCount": 13 }
  ]
}
```

### ERROR

错误响应。

```json
{
  "messageType": "ERROR",
  "errorCode": 3002,
  "message": "Action is not allowed in the current stage."
}
```

建议错误码：

| 范围 | 类别 | 示例 |
| --- | --- | --- |
| `100x` | 握手/鉴权 | 未握手、未知 token、角色不允许 |
| `200x` | 参数/动作合法性 | 数量非法、订单不存在、技能参数缺失 |
| `300x` | 阶段/节流 | 非当前阶段、每 Tick 指令超限、研报窗口关闭 |
| `400x` | 服务端内部错误 | 未处理异常、序列化失败 |

具体建议：

| code | message |
| --- | --- |
| `1001` | `HELLO is required before this message.` |
| `1002` | `Unknown token.` |
| `1003` | `Observer cannot send player actions.` |
| `2001` | `Invalid price or quantity.` |
| `2002` | `Order does not exist or cannot be canceled.` |
| `2003` | `Insufficient balance or position.` |
| `2004` | `Invalid strategy card.` |
| `2005` | `Invalid skill or skill parameter.` |
| `3001` | `Action limit exceeded for this tick.` |
| `3002` | `Action is not allowed in the current stage.` |
| `3003` | `Research report window is closed.` |
| `3004` | `New orders are disabled during circuit breaker.` |
| `4001` | `Internal server error.` |

## 状态机

比赛阶段：

```text
Waiting -> PreparingGame -> StrategySelection -> TradingDay -> Settlement
         -> StrategySelection -> TradingDay -> Settlement
         -> StrategySelection -> TradingDay -> Settlement -> Finished
```

阶段说明：

- `Waiting`：等待玩家连接。
- `PreparingGame`：准备进入下一阶段。
- `StrategySelection`：策略选择，默认 40 Tick。
- `TradingDay`：交易日，默认 2000 Tick。
- `Settlement`：单日结算。
- `Finished`：整场结束。

## 快照建议

Player 完成 `HELLO` 后立即收到：

1. `HELLO_ACK`。
2. `GAME_STATE`。
3. 当前玩家视角 `MARKET_STATE`。
4. 当前玩家 `PLAYER_STATE`。
5. 当前 `STRATEGY_OPTIONS`，如果处于策略阶段。

Observer 完成 `HELLO` 后立即收到：

1. `HELLO_ACK`。
2. `GAME_STATE`。
3. 公共 `MARKET_STATE`。
4. 每个玩家一条 `PLAYER_SUMMARY_STATE`。
5. 当前 `STRATEGY_OPTIONS`，如果处于策略阶段。

## 时序示例

Player：

```text
client -> server: HELLO(role=player, token=player1)
server -> client: HELLO_ACK
server -> client: GAME_STATE
server -> client: MARKET_STATE
server -> client: PLAYER_STATE
server -> client: STRATEGY_OPTIONS
client -> server: SELECT_STRATEGY
server -> client: GAME_STATE
server -> client: MARKET_STATE
server -> client: PLAYER_STATE
client -> server: LIMIT_BUY
server -> client: TRADE_NOTIFICATION
```

Observer：

```text
client -> server: HELLO(role=observer)
server -> client: HELLO_ACK
server -> client: GAME_STATE
server -> client: MARKET_STATE
server -> client: PLAYER_SUMMARY_STATE
server -> client: NEWS_BROADCAST
server -> client: SKILL_EFFECT
server -> client: DAY_SETTLEMENT
```

## 服务端接线建议

- 在 `PerformMessages.cs` 新增 `HelloMessage`，但不要让它继承必须带 token 的 `PerformMessage`。
- 在 `BroadcastMessages.cs` 新增 `HelloAckMessage`、`PlayerSummaryStateMessage`、`DaySettlementMessage`。
- 在 `AgentServer.MessageReceiving.cs` 中优先解析 `HELLO`，完成 role/socket 绑定。
- 在 `AgentServer.MessageSending.cs` 中区分 `PublishToPlayers`、`PublishToObservers`、`PublishToAllConnections`。
- 在 `Program.cs` 或独立 publisher 中复用当前 `BroadcastGameState` 组包逻辑，支持 HELLO 后补快照。
- 将 `TradingDay.OnNewsPublished`、`OnTradeExecuted`、`OnReportSettled`、`OnSkillActivated` 接到对应广播消息。
