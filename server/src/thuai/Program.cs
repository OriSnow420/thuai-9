namespace Thuai;

using Serilog;
using Serilog.Events;
using Thuai.Utility;
using Thuai.Connection;
using Thuai.GameLogic;
using Thuai.Protocol.Messages;

public class Program
{
    public static void Main(string[] args)
    {
        Config config;
        try
        {
            config = Tools.LoadOrCreateConfig("config/config.json");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load config: {ex.Message}");
            return;
        }

        // Setup logging
        SetupLogger(config.Log);
        Log.Information("THUAI-9 Server starting...");

        try
        {
            // Create components
            var agentServer = new AgentServer { Port = config.Server.Port };
            var gameController = new GameController.GameController(config.Game);
            using var recorder = new Recorder.Recorder("./data", config.Recorder.KeepRecord);

            // Load tokens and add players
            var tokens = Tools.LoadTokens(config.Token);
            Log.Information("Loaded {Count} player tokens", tokens.Length);
            foreach (var token in tokens)
            {
                gameController.Game.AddPlayer(token);
                Log.Information("Added player: {Token}", token);
            }

            // Wire events
            // AgentServer -> GameController: player messages
            agentServer.AfterMessageReceiveEvent += gameController.HandleAfterMessageReceiveEvent;
            // AgentServer -> GameController: player connections
            agentServer.AfterPlayerConnectEvent += gameController.HandleAfterPlayerConnectEvent;
            // GameController -> AgentServer: link socket to token
            gameController.AfterPlayerConnectEvent += (sender, e) =>
            {
                agentServer.HandleAfterPlayerConnectEvent(sender,
                    new AgentServer.AfterPlayerConnectEventArgs
                    {
                        SocketId = e.SocketId,
                        Token = e.Token
                    });
            };

            // Game -> Broadcast + Record: after each tick
            gameController.Game.AfterGameTickEvent += (sender, e) =>
            {
                BroadcastGameState(agentServer, e.Game);
                RecordGameState(recorder, e.Game);
            };

            // Start server
            agentServer.Start();
            gameController.Start();

            Log.Information("Server running. Waiting for game to finish...");

            // Poll until game finishes
            while (gameController.IsRunning)
            {
                Task.Delay(1000).Wait();
            }

            // Game finished - save results
            Log.Information("Game complete. Saving results...");
            recorder.Flush();

            var scores = gameController.Game.Scoreboard;
            recorder.SaveResults(scores);

            foreach (var (token, score) in scores)
            {
                Log.Information("Player {Token}: {Score} points", token, score);
            }

            agentServer.Stop();
            gameController.Stop();
            Log.Information("Server shut down");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Server crashed");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void SetupLogger(LogSettings logSettings)
    {
        var levelSwitch = logSettings.MinimumLevel.ToLower() switch
        {
            "verbose" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "information" => LogEventLevel.Information,
            "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "fatal" => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };

        var logConfig = new LoggerConfiguration()
            .MinimumLevel.Is(levelSwitch);

        if (logSettings.Target.ToLower() == "console" || logSettings.Target.ToLower() == "both")
        {
            logConfig = logConfig.WriteTo.Console();
        }

        if (logSettings.Target.ToLower() == "file" || logSettings.Target.ToLower() == "both")
        {
            var interval = logSettings.RollingInterval.ToLower() switch
            {
                "hour" => RollingInterval.Hour,
                "day" => RollingInterval.Day,
                "month" => RollingInterval.Month,
                _ => RollingInterval.Day
            };

            logConfig = logConfig.WriteTo.File(
                Path.Combine(logSettings.TargetDirectory, "thuai-.log"),
                rollingInterval: interval);
        }

        Log.Logger = logConfig.CreateLogger();
    }

    private static void BroadcastGameState(AgentServer agentServer, Game game)
    {
        // 1. Broadcast global game state to all
        var gameState = new GameStateMessage
        {
            Stage = game.Stage.ToString(),
            CurrentDay = game.CurrentDayNumber,
            CurrentTick = game.CurrentTick,
            TotalTicks = game.Stage == GameStage.TradingDay
                ? game.CurrentTradingDay?.CurrentTick ?? 0
                : 0,
            Scores = game.Scoreboard.Select(kv => new PlayerScore
            {
                Token = kv.Key,
                Score = kv.Value
            }).ToList()
        };
        agentServer.PublishToAll(gameState);

        if (game.Stage == GameStage.Settlement && game.CurrentTradingDay != null)
        {
            agentServer.PublishToAll(BuildDaySettlementMessage(game));
        }

        // 2. During trading day, broadcast market state and per-player state
        if (game.Stage == GameStage.TradingDay && game.CurrentTradingDay != null)
        {
            var day = game.CurrentTradingDay;
            var orderBook = day.OrderBook;

            // Build base market data
            var baseBids = orderBook.GetVisibleBids().Select(l => new PriceLevel
            {
                Price = l.Price,
                Quantity = l.Quantity
            }).ToList();
            var baseAsks = orderBook.GetVisibleAsks().Select(l => new PriceLevel
            {
                Price = l.Price,
                Quantity = l.Quantity
            }).ToList();

            // Pre-compute fake sell levels by owner for malicious shorting
            var fakeLevelsByOwner = day.FakeSellLevels
                .GroupBy(f => f.OwnerToken)
                .ToDictionary(g => g.Key, g => g.Select(f => new PriceLevel
                {
                    Price = f.Price,
                    Quantity = f.Quantity
                }).ToList());

            // Per-player: market state (with possible fake levels) + private state
            foreach (var player in game.Players.Values)
            {
                // Build per-player asks (merge fake sell levels from opponents)
                var playerAsks = new List<PriceLevel>(baseAsks);
                foreach (var (ownerToken, fakeLevels) in fakeLevelsByOwner)
                {
                    if (ownerToken != player.Token)
                        playerAsks.AddRange(fakeLevels);
                }
                if (playerAsks.Count != baseAsks.Count)
                    playerAsks.Sort((a, b) => a.Price.CompareTo(b.Price));

                var marketState = new MarketStateMessage
                {
                    Bids = baseBids,
                    Asks = playerAsks,
                    LastPrice = orderBook.LastPrice,
                    MidPrice = orderBook.MidPrice,
                    Volume = orderBook.TotalVolume,
                    Tick = day.CurrentTick
                };
                agentServer.Publish(marketState, player.Token);

                var pendingOrders = day.GetPlayerPendingOrders(player.Token);
                var playerState = new PlayerStateMessage
                {
                    Mora = player.Mora,
                    FrozenMora = player.FrozenMora,
                    Gold = player.Gold,
                    FrozenGold = player.FrozenGold,
                    LockedGold = player.LockedGold,
                    Nav = player.CalculateNAV(orderBook.MidPrice),
                    ActiveCards = player.ActiveCards.Select(c => c.Name).ToList(),
                    PendingOrders = pendingOrders.Select(o => new OrderInfo
                    {
                        OrderId = o.OrderId,
                        Side = o.Side.ToString(),
                        Price = o.Price,
                        Quantity = o.Quantity,
                        RemainingQuantity = o.RemainingQuantity,
                        Status = o.Status.ToString()
                    }).ToList()
                };
                agentServer.Publish(playerState, player.Token);
            }

            // Insider news previews: send early news content to players with InsiderInfo card.
            foreach (var (playerToken, preview) in day.PendingInsiderPreviews)
            {
                var previewMsg = new NewsBroadcastMessage
                {
                    NewsId = preview.NewsId,
                    Content = preview.Content,
                    PublishTick = preview.PublishTick
                };
                agentServer.Publish(previewMsg, playerToken);
            }

            // (Malicious shorting fake levels are already merged into per-player market state above)
        }

        // 3. During strategy selection, broadcast options
        if (game.Stage == GameStage.StrategySelection)
        {
            var cardManager = game.CardManager;
            var options = new StrategyOptionsMessage
            {
                Infrastructure = cardManager.CurrentInfrastructure != null
                    ? new CardOption
                    {
                        Name = cardManager.CurrentInfrastructure.Name,
                        Description = cardManager.CurrentInfrastructure.Description,
                        Category = "Infrastructure"
                    }
                    : null,
                RiskControl = cardManager.CurrentRiskControl != null
                    ? new CardOption
                    {
                        Name = cardManager.CurrentRiskControl.Name,
                        Description = cardManager.CurrentRiskControl.Description,
                        Category = "RiskControl"
                    }
                    : null,
                FinTech = cardManager.CurrentFinTech != null
                    ? new CardOption
                    {
                        Name = cardManager.CurrentFinTech.Name,
                        Description = cardManager.CurrentFinTech.Description,
                        Category = "FinTech"
                    }
                    : null
            };
            agentServer.PublishToAll(options);
        }
    }

    private static DaySettlementMessage BuildDaySettlementMessage(Game game)
    {
        var day = game.CurrentTradingDay!;
        var midPrice = day.OrderBook.MidPrice;
        var players = game.Players.Values
            .Select(player => new DaySettlementPlayer
            {
                Token = player.Token,
                Nav = player.CalculateNAV(midPrice),
                Mora = player.Mora,
                Gold = player.Gold,
                FrozenMora = player.FrozenMora,
                FrozenGold = player.FrozenGold,
                LockedGold = player.LockedGold,
                TradeCount = player.TotalTradeCount,
                ActiveCards = player.ActiveCards.Select(card => card.Name).ToList()
            })
            .OrderByDescending(player => player.Nav)
            .ThenByDescending(player => player.TradeCount)
            .ToList();

        var winner = "";
        var reason = "tie";
        if (players.Count >= 2)
        {
            if (players[0].Nav > players[1].Nav)
            {
                winner = players[0].Token;
                reason = "higher NAV";
            }
            else if (players[0].Nav == players[1].Nav
                && players[0].TradeCount > players[1].TradeCount)
            {
                winner = players[0].Token;
                reason = "trade-count tiebreaker";
            }
        }

        return new DaySettlementMessage
        {
            Day = game.CurrentDayNumber,
            WinnerToken = winner,
            Reason = reason,
            Players = players
        };
    }

    private static void RecordGameState(Recorder.Recorder recorder, Game game)
    {
        var snapshot = new
        {
            Tick = game.CurrentTick,
            Stage = game.Stage.ToString(),
            Day = game.CurrentDayNumber,
            Scores = game.Scoreboard,
            TradingDayTick = game.CurrentTradingDay?.CurrentTick,
            MarketState = game.CurrentTradingDay != null ? new
            {
                LastPrice = game.CurrentTradingDay.OrderBook.LastPrice,
                MidPrice = game.CurrentTradingDay.OrderBook.MidPrice,
                Volume = game.CurrentTradingDay.OrderBook.TotalVolume
            } : null,
            Players = game.Players.Values.Select(p => new
            {
                Token = p.Token,
                Mora = p.Mora,
                Gold = p.Gold,
                FrozenMora = p.FrozenMora,
                FrozenGold = p.FrozenGold,
                Nav = game.CurrentTradingDay != null
                    ? p.CalculateNAV(game.CurrentTradingDay.OrderBook.MidPrice)
                    : p.Mora
            }).ToList()
        };

        recorder.Record(snapshot);
    }

    // Keep this for backward compatibility with tests
    public static int Add(int a, int b) => a + b;
}
