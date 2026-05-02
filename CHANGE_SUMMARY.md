# Change Summary

This document summarizes the local changes made on top of the upstream THUAI-9 project:

```text
https://github.com/thuasta/thuai-9
```

The current workspace has been realigned to the upstream project structure. The main source directories are:

```text
server/
web/
sdk-python/
sdk-cpp/
docs/
```

## 1. Project Structure Alignment

The workspace originally contained files from an unrelated `werewolf-agent` repository. Those files were removed.

Removed incorrect leftovers:

```text
client/
server/        # old unrelated Python skeleton, before server was restored
sdk/
docs/
tests/
README.md
LICENSE
```

Then the correct THUAI-9 upstream files were restored:

```text
README.md
LICENSE
docs/
sdk-cpp/
```

The C# backend directory was renamed from the temporary local name:

```text
thuai9-server/
```

back to the upstream name:

```text
server/
```

The root `.gitignore` was also replaced with the upstream THUAI-9 version.

## 2. Frontend Legacy Protocol Compatibility

The upstream frontend originally sent a `HELLO` message when a WebSocket connection opened. The current backend does not recognize `HELLO`; it only binds a socket to a player after receiving a legacy client action containing a `token`.

To make the browser UI receive live data from the current backend, the frontend now sends a harmless legacy action on connect:

```text
CANCEL_ORDER token=<token> orderId=-1
```

This is only used to bind the browser WebSocket to `player1` or `player2`. It does not affect game state.

Modified file:

```text
web/src/main.js
```

The frontend dev port was also changed to avoid local port conflicts:

```text
5173 -> 5175
```

Modified file:

```text
web/package.json
```

## 3. Python Demo Agent

The upstream Python example agent could connect and select strategy cards, but it did not actively trade.

The local Python demo was changed so a complete two-player demo match can run without writing additional strategy code.

Changes:

- Sends `cancel_order(-1)` immediately after connecting, so the backend binds the socket to the player's token.
- Selects the first available strategy card.
- Submits a `Long` research report when news is received.
- Places a minimal one-unit buy or sell order every 25 market ticks when possible.

Modified files:

```text
sdk-python/sdk_python/agent.py
sdk-python/main.py
```

## 4. C++ Demo Agent

The upstream C++ example had the same legacy binding issue and did not actively trade.

The local C++ demo was updated to match the Python demo behavior:

- Sends `cancelOrder(-1)` after WebSocket connection open.
- Places simple one-unit buy or sell orders every 25 market ticks when possible.

Modified files:

```text
sdk-cpp/src/agent.hpp
sdk-cpp/src/main.cpp
```

Note: the C++ version has not been fully built in this environment because `xmake` and required C++ dependencies are not installed here.

## 5. Daily Summary Feature

A new daily settlement summary feature was added.

### Backend

The backend now broadcasts a `DAY_SETTLEMENT` message during the `Settlement` stage.

The message includes:

- trading day
- winner token
- win reason
- each player's NAV
- Mora
- Gold
- frozen Mora
- frozen Gold
- locked Gold
- trade count
- active strategy cards

Modified files:

```text
server/src/thuai/Protocol/Messages/BroadcastMessages.cs
server/src/thuai/Program.cs
```

### Frontend

The frontend now has a new dashboard panel at the same level as `Market`, `Timeline`, `Book`, and other panels:

```text
Daily Summary / 每日总结
```

It stores and displays each trading day's settlement summary for `player1` and `player2`.

Modified files:

```text
web/index.html
web/src/store.js
web/src/render.js
web/styles.css
```

The panel remains empty at the start of a match and starts showing data after the first trading day enters settlement.

## 6. Verified Python Demo Startup

The currently verified Python demo startup sequence is:

Terminal 1:

```bash
cd /home/fan2006/THUAI/forward/web
npm run serve
```

Open:

```text
http://localhost:5175/?mode=player&token=player1
```

Terminal 2:

```bash
cd /home/fan2006/THUAI/forward/server
TOKENS=player1,player2 dotnet run --project src/thuai
```

Terminal 3:

```bash
cd /home/fan2006/THUAI/forward/sdk-python
TOKEN=player1 SERVER=ws://localhost:14514 python main.py
```

Terminal 4:

```bash
cd /home/fan2006/THUAI/forward/sdk-python
TOKEN=player2 SERVER=ws://localhost:14514 python main.py
```

Expected backend logs include:

```text
Player identified: player1
Player identified: player2
```

## 7. Validation Commands

Frontend tests:

```bash
cd /home/fan2006/THUAI/forward/web
npm test
```

Backend build:

```bash
cd /home/fan2006/THUAI/forward/server
dotnet build src/thuai
```

Known warning:

```text
NU1900: Error occurred while getting package vulnerability data
```

This warning is caused by restricted or unavailable network access to NuGet vulnerability metadata and does not prevent the backend from building or running.

## 8. Files Changed Compared With Upstream

Main changed source files:

```text
web/index.html
web/package.json
web/src/main.js
web/src/render.js
web/src/store.js
web/styles.css
server/src/thuai/Program.cs
server/src/thuai/Protocol/Messages/BroadcastMessages.cs
sdk-python/main.py
sdk-python/sdk_python/agent.py
sdk-cpp/src/agent.hpp
sdk-cpp/src/main.cpp
```

Local summary/documentation files:

```text
CHANGE_SUMMARY.md
cpp_sdk.md
py_sdk.md
server.md
```
