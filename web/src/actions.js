export function helloMessage(role, token) {
  const message = {
    messageType: "HELLO",
    role,
    protocolVersion: "v1",
  };
  if (role === "player") {
    message.token = token;
  }
  return message;
}

export function limitBuyMessage(token, price, quantity) {
  return {
    messageType: "LIMIT_BUY",
    token,
    price: toInteger(price),
    quantity: toInteger(quantity),
  };
}

export function limitSellMessage(token, price, quantity) {
  return {
    messageType: "LIMIT_SELL",
    token,
    price: toInteger(price),
    quantity: toInteger(quantity),
  };
}

export function cancelOrderMessage(token, orderId) {
  return {
    messageType: "CANCEL_ORDER",
    token,
    orderId: toInteger(orderId),
  };
}

export function submitReportMessage(token, newsId, prediction) {
  return {
    messageType: "SUBMIT_REPORT",
    token,
    newsId: toInteger(newsId),
    prediction,
  };
}

export function selectStrategyMessage(token, cardName) {
  return {
    messageType: "SELECT_STRATEGY",
    token,
    cardName,
  };
}

export function activateSkillMessage(token, skillName, direction) {
  const message = {
    messageType: "ACTIVATE_SKILL",
    token,
    skillName,
  };
  if (direction) {
    message.direction = direction;
  }
  return message;
}

export function sendJson(ws, message) {
  if (!ws || ws.readyState !== 1) {
    throw new Error("WebSocket is not connected.");
  }
  ws.send(JSON.stringify(message));
}

function toInteger(value) {
  const number = Number.parseInt(value, 10);
  return Number.isFinite(number) ? number : 0;
}
