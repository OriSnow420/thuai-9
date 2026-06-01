namespace Thuai;

using Serilog;
using Serilog.Events;
using Utility;
using Connection;
using GameLogic;
using Protocol.Messages;
using ServerRuntime = Thuai.Runtime;

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

        bool infiniteMode = config.Game.InfiniteMode
            || args.Any(arg => string.Equals(arg, "--infinite", StringComparison.OrdinalIgnoreCase));
        if (infiniteMode)
        {
            config = config with { Game = config.Game with { InfiniteMode = true } };
        }

        // Setup logging
        SetupLogger(config.Log);
        Log.Information("THUAI-9 Server starting...");
        if (infiniteMode)
            Log.Information("Infinite mode enabled: month numbers will continue past month 3");

        try
        {
            // Create components
            var agentServer = new AgentServer
            {
                Port = config.Server.Port,
                AdminSecret = Environment.GetEnvironmentVariable("THUAI_ADMIN_SECRET"),
                AcceptAnyToken = config.Server.AcceptAnyToken
            };
            if (!string.IsNullOrEmpty(agentServer.AdminSecret))
                Log.Information("Admin debug interface enabled (THUAI_ADMIN_SECRET set)");
            if (agentServer.AcceptAnyToken)
                Log.Warning("Open token mode enabled: any non-empty player token will be accepted");
            var gameController = new GameController.GameController(config.Game);
            using var recorder = new Recorder.Recorder("./data", config.Recorder.KeepRecord, config.Recorder.FlushEveryRecords);
            using var statRecorder = new Recorder.StatRecorder("./data", config.Recorder.EnableStatRecording, config.Recorder.StatFlushEveryRecords);
            var disconnectedPlayerRetentionTicks = config.Game.DisconnectedPlayerRetentionTicks;
            if (infiniteMode && disconnectedPlayerRetentionTicks <= 0)
                disconnectedPlayerRetentionTicks = Math.Max(config.Game.TicksPerSecond * 300, 1);
            var sessionTracker = new ServerRuntime.PlayerSessionTracker(disconnectedPlayerRetentionTicks);
            var statisticsWriter = new Recorder.PlayerStatisticsWriter(
                "./data",
                saveIntervalTicks: config.Recorder.StatisticsSaveIntervalTicks);
            if (infiniteMode)
                Log.Information("Disconnected players will be removed after {Ticks} ticks", disconnectedPlayerRetentionTicks);

            var playerNames = Tools.LoadPlayerNames(config.Token);
            if (playerNames.Count > 0)
            {
                foreach (var (token, displayName) in playerNames)
                    gameController.Game.SetPlayerDisplayName(token, displayName);

                Log.Information("Loaded {Count} player display names from {EnvVar}",
                    playerNames.Count, config.Token.PlayerNameLocation);
            }

            // Load tokens and add players
            var tokens = Tools.LoadTokens(config.Token);
            Log.Information("Loaded {Count} player tokens", tokens.Length);
            foreach (var token in tokens)
            {
                gameController.Game.AddPlayer(token);
                agentServer.RegisterValidToken(token);
                if (infiniteMode)
                    sessionTracker.SeedDisconnected(token, currentTick: 0);
                Log.Information("Added player: {Token} as {PlayerName}",
                    token, gameController.Game.FindPlayer(token)?.DisplayName ?? token);
            }

            // Wire events
            // AgentServer -> GameController: player messages
            agentServer.AfterMessageReceiveEvent += gameController.HandleAfterMessageReceiveEvent;
            // AgentServer -> GameController: player connections
            agentServer.AfterPlayerConnectEvent += (sender, e) =>
            {
                sessionTracker.MarkConnected(e.Token, gameController.Game.CurrentTick);
                gameController.HandleAfterPlayerConnectEvent(sender, e);
            };
            agentServer.AfterPlayerDisconnectEvent += (sender, e) =>
            {
                sessionTracker.MarkDisconnected(e.Token, gameController.Game.CurrentTick);
            };
            // AgentServer -> AdminCommandHandler: debug commands from admin sockets
            agentServer.AfterAdminMessageEvent += (sender, e) =>
            {
                var response = AdminCommandHandler.Handle(gameController.Game, e.Message);
                agentServer.PublishToSocket(response, e.SocketId);
            };

            // Game -> Broadcast + Record: after each tick
            gameController.Game.AfterGameTickEvent += (sender, e) =>
            {
                ExpireDisconnectedPlayers(e.Game, sessionTracker);
                statRecorder.RecordFromGame(e.Game);
                RecordGameState(recorder, e.Game);
                BroadcastGameState(agentServer, e.Game);

                if (e.Game.Stage == GameStage.Settlement)
                {
                    recorder.SaveResults(e.Game.GetScoreboardSnapshot());
                }

                // Persist periodic player statistics, but do NOT rewrite
                // result.json here: mid-game scoreboards are all-zero until
                // settlement, and an external reader (the evaluator) treats any
                // result.json with all tokens present as the final result. The
                // authoritative result.json is written at each Settlement (above)
                // and once more at game completion below.
                var sessions = sessionTracker.GetSnapshots(e.Game.CurrentTick);
                statisticsWriter.MaybeSave(e.Game, sessions);
            };

            // Start server
            agentServer.Start();
            gameController.Start();

            Log.Information(infiniteMode
                ? "Server running in infinite mode."
                : "Server running. Waiting for game to finish...");

            // Poll until game finishes
            while (gameController.IsRunning)
            {
                Task.Delay(1000).Wait();
            }

            // Game finished - save results
            Log.Information("Game complete. Saving results...");
            recorder.Flush();
            statRecorder.Flush();

            var scores = gameController.Game.GetScoreboardSnapshot();
            statisticsWriter.Save(gameController.Game, sessionTracker.GetSnapshots(gameController.Game.CurrentTick));
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

    private static void ExpireDisconnectedPlayers(Game game, ServerRuntime.PlayerSessionTracker sessionTracker)
    {
        foreach (var token in sessionTracker.CollectExpiredTokens(game.CurrentTick))
        {
            if (game.QueuePlayerRemoval(token))
                Log.Information("Player {Token} queued for removal after disconnect timeout", token);
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
        var gameState = new GameStateMessage
        {
            Stage = game.Stage.ToString(),
            CurrentMonth = game.CurrentMonthNumber,
            CurrentDay = game.CurrentDayNumber,
            CurrentTick = game.CurrentTick,
            TotalTicks = game.TradingDayTicks,
            Scores = game.Players.Values
                .OrderBy(player => player.PlayerId)
                .Select(player => new PlayerScore
                {
                    PlayerId = player.PlayerId,
                    PlayerName = player.DisplayName,
                    Score = game.Scoreboard.GetValueOrDefault(player.Token)
                })
                .ToList()
        };
        agentServer.PublishToAll(gameState);

        if (game.CurrentTradingDay is { HasPendingNotifications: true })
        {
            PublishTradingDayNotifications(agentServer, game);
            game.CurrentTradingDay.MarkNotificationsPublished();
        }

        if (game is { HasPendingSettlementNotification: true, CurrentTradingDay: not null })
        {
            agentServer.PublishToAll(BuildDaySettlementMessage(game));
            game.MarkSettlementNotificationPublished();
        }

        if ((game.Stage == GameStage.TradingDay || game.Stage == GameStage.Settlement) && game.CurrentTradingDay != null)
        {
            var day = game.CurrentTradingDay;
            // Snapshot order-book state under the trading-day lock: socket-thread
            // order handlers mutate these collections concurrently with this read.
            var market = day.SnapshotMarket();

            var baseBids = market.Bids.Select(l => new PriceLevel
            {
                Price = l.Price,
                Quantity = l.Quantity
            }).ToList();
            var baseAsks = market.Asks.Select(l => new PriceLevel
            {
                Price = l.Price,
                Quantity = l.Quantity
            }).ToList();

            // Public market snapshot — identical for everyone, broadcast once.
            var marketState = new MarketStateMessage
            {
                Bids = baseBids,
                Asks = baseAsks,
                LastPrice = market.LastPrice,
                MidPrice = market.MidPrice,
                Volume = market.Volume,
                Tick = day.CurrentTick
            };
            agentServer.PublishToAll(marketState);

            foreach (var player in game.Players.Values)
            {
                var pendingOrders = day.GetPlayerPendingOrders(player.Token);
                var playerState = new PlayerStateMessage
                {
                    PlayerId = player.PlayerId,
                    Mora = player.Mora,
                    FrozenMora = player.FrozenMora,
                    Gold = player.Gold,
                    FrozenGold = player.FrozenGold,
                    LockedGold = player.LockedGold,
                    Nav = player.CalculateNAV(market.MidPrice),
                    NetworkDelay = player.NetworkDelay,
                    ImmediateOrdersUsedToday = player.ImmediateOrdersUsedToday,
                    RestingOrdersUsedToday = player.RestingOrdersUsedToday,
                    BonusImmediateOrdersToday = player.BonusImmediateOrdersToday,
                    MonthlyTradeCount = player.MonthlyTradeCount,
                    ActiveCards = player.ActiveCards.Select(c => c.Name).ToList(),
                    PendingOrders = pendingOrders.Select(o => new OrderInfo
                    {
                        OrderId = o.OrderId,
                        ArrivalTick = o.ArrivalTick,
                        Side = o.Side.ToString(),
                        Price = o.Price,
                        Quantity = o.Quantity,
                        RemainingQuantity = o.RemainingQuantity,
                        Status = o.Status.ToString(),
                        Intent = o.Intent?.ToString() ?? ""
                    }).ToList()
                };
                agentServer.Publish(playerState, player.Token);
            }
        }

        // Strategy options are broadcast once per selection phase (on entry, or
        // when a player joins mid-phase) rather than every tick — re-broadcasting
        // each tick spammed clients and a card a player already owns is silently
        // unselectable, so a naive agent would re-pick it on every broadcast.
        if (game.Stage == GameStage.StrategySelection && game.HasPendingStrategyOptions)
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
            game.MarkStrategyOptionsPublished();
        }
    }

    private static void PublishTradingDayNotifications(AgentServer agentServer, Game game)
    {
        var tradingDay = game.CurrentTradingDay!;

        foreach (var news in tradingDay.PublishedNewsThisDay)
        {
            // Each player may receive a spoofed view of the news due to skill cards,
            // so the broadcast happens per-player. Observers and admins receive the
            // un-spoofed real news via a separate PublishToAll call below — admins
            // already get it through the per-player fan-out, so we only fan out to
            // the role-broadcast for the un-bound observers.
            foreach (var player in game.Players.Values)
            {
                News delivered = news;
                if (!news.IsFake && player.ConsumePendingFakeBroadcast())
                {
                    delivered = tradingDay.NewsSystem.CreateSpoofedView(news);
                }

                agentServer.Publish(new NewsBroadcastMessage
                {
                    Month = game.CurrentMonthNumber,
                    Day = delivered.PublishTick,
                    NewsId = delivered.NewsId,
                    Content = delivered.Content,
                    PublishTick = delivered.PublishTick
                }, player.Token);
            }

            agentServer.PublishToObservers(new NewsBroadcastMessage
            {
                Month = game.CurrentMonthNumber,
                Day = news.PublishTick,
                NewsId = news.NewsId,
                Content = news.Content,
                PublishTick = news.PublishTick
            });
        }

        foreach (var (playerToken, preview) in tradingDay.PendingInsiderPreviews)
        {
            agentServer.Publish(new NewsBroadcastMessage
            {
                Month = game.CurrentMonthNumber,
                Day = preview.PublishTick,
                NewsId = preview.NewsId,
                Content = preview.Content,
                PublishTick = preview.PublishTick
            }, playerToken);
        }

        foreach (var report in tradingDay.SettledReportsThisDay)
        {
            var playerId = game.FindPlayer(report.PlayerToken)?.PlayerId ?? -1;
            agentServer.Publish(new ReportResultMessage
            {
                PlayerId = playerId,
                NewsId = report.NewsId,
                SubmissionRank = report.SubmissionRank,
                SubmitTick = report.SubmitTick,
                SettlementTick = report.SettlementDay,
                Prediction = report.Prediction.ToString(),
                IsCorrect = report.IsCorrect ?? false,
                Reward = report.Reward,
                ActualChange = report.ActualChange
            }, report.PlayerToken);
        }

        foreach (var trade in tradingDay.TradesThisDay)
        {
            if (trade.BuyerToken != "SYSTEM")
            {
                var buyerId = game.FindPlayer(trade.BuyerToken)?.PlayerId ?? -1;
                agentServer.Publish(new TradeNotificationMessage
                {
                    PlayerId = buyerId,
                    TradeId = trade.TradeId,
                    OrderId = trade.BuyOrderId,
                    Price = trade.Price,
                    Quantity = trade.Quantity,
                    Side = "Buy",
                    Fee = trade.BuyerFee,
                    Tick = trade.Tick
                }, trade.BuyerToken);
            }

            if (trade.SellerToken != "SYSTEM")
            {
                var sellerId = game.FindPlayer(trade.SellerToken)?.PlayerId ?? -1;
                agentServer.Publish(new TradeNotificationMessage
                {
                    PlayerId = sellerId,
                    TradeId = trade.TradeId,
                    OrderId = trade.SellOrderId,
                    Price = trade.Price,
                    Quantity = trade.Quantity,
                    Side = "Sell",
                    Fee = trade.SellerFee,
                    Tick = trade.Tick
                }, trade.SellerToken);
            }
        }

        foreach (var effect in tradingDay.SkillEffectsThisDay)
        {
            agentServer.PublishToAll(new SkillEffectMessage
            {
                SkillName = effect.SkillName,
                SourcePlayerId = effect.SourcePlayerId,
                TargetPlayerId = effect.TargetPlayerId,
                Description = effect.Description
            });
        }
    }

    private static DaySettlementMessage BuildDaySettlementMessage(Game game)
    {
        var settlement = game.LatestSettlement!;
        var day = game.CurrentTradingDay!;
        var midPrice = day.OrderBook.MidPrice;
        var players = game.Players.Values
            .Select(player => new DaySettlementPlayer
            {
                PlayerId = player.PlayerId,
                Nav = settlement.MonthNavs.GetValueOrDefault(player.Token, player.CalculateNAV(midPrice)),
                Mora = player.Mora,
                Gold = player.Gold,
                FrozenMora = player.FrozenMora,
                FrozenGold = player.FrozenGold,
                LockedGold = player.LockedGold,
                TradeCount = player.MonthlyTradeCount,
                ActiveCards = player.ActiveCards.Select(card => card.Name).ToList()
            })
            .OrderByDescending(player => player.Nav)
            .ThenByDescending(player => player.TradeCount)
            .ToList();

        var winnerPlayerId = game.FindPlayer(settlement.WinnerToken)?.PlayerId ?? -1;
        var finalBonusWinnerPlayerId = game.FindPlayer(settlement.FinalBonusWinnerToken)?.PlayerId ?? -1;
        var cumulativeNavsById = new Dictionary<int, long>();
        foreach (var (token, nav) in settlement.CumulativeNavs)
        {
            var player = game.FindPlayer(token);
            if (player != null)
                cumulativeNavsById[player.PlayerId] = nav;
        }

        return new DaySettlementMessage
        {
            Day = settlement.Month,
            Month = settlement.Month,
            WinnerPlayerId = winnerPlayerId,
            Reason = settlement.Reason,
            Players = players,
            CumulativeNavs = cumulativeNavsById,
            FinalBonusWinnerPlayerId = finalBonusWinnerPlayerId,
            FinalBonusPoints = settlement.FinalBonusPoints
        };
    }

    private static void RecordGameState(Recorder.Recorder recorder, Game game)
    {
        var replayEvents = new List<object>();
        if (game.CurrentTradingDay != null)
        {
            var tradingDay = game.CurrentTradingDay;
            foreach (var news in tradingDay.PublishedNewsThisDay)
            {
                replayEvents.Add(new
                {
                    Type = "news",
                    Month = game.CurrentMonthNumber,
                    NewsId = news.NewsId,
                    PublishTick = news.PublishTick,
                    Content = news.Content,
                    Sentiment = news.Sentiment.ToString(),
                    IsFake = news.IsFake,
                    SourcePlayer = news.SourcePlayer
                });
            }

            foreach (var report in tradingDay.SettledReportsThisDay)
            {
                var reportPlayer = game.FindPlayer(report.PlayerToken);
                replayEvents.Add(new
                {
                    Type = "report",
                    Month = game.CurrentMonthNumber,
                    PlayerId = reportPlayer?.PlayerId ?? -1,
                    PlayerToken = report.PlayerToken,
                    PlayerName = reportPlayer?.DisplayName ?? report.PlayerToken,
                    NewsId = report.NewsId,
                    Prediction = report.Prediction.ToString(),
                    SubmitTick = report.SubmitTick,
                    SettlementTick = report.SettlementDay,
                    SubmissionRank = report.SubmissionRank,
                    IsCorrect = report.IsCorrect ?? false,
                    ActualChange = report.ActualChange,
                    Reward = report.Reward
                });
            }

            foreach (var trade in tradingDay.TradesThisDay)
            {
                var buyer = game.FindPlayer(trade.BuyerToken);
                var seller = game.FindPlayer(trade.SellerToken);
                replayEvents.Add(new
                {
                    Type = "trade",
                    Month = game.CurrentMonthNumber,
                    Tick = trade.Tick,
                    TradeId = trade.TradeId,
                    BuyOrderId = trade.BuyOrderId,
                    SellOrderId = trade.SellOrderId,
                    BuyerPlayerId = buyer?.PlayerId ?? -1,
                    SellerPlayerId = seller?.PlayerId ?? -1,
                    BuyerToken = trade.BuyerToken,
                    SellerToken = trade.SellerToken,
                    BuyerPlayerName = buyer?.DisplayName ?? trade.BuyerToken,
                    SellerPlayerName = seller?.DisplayName ?? trade.SellerToken,
                    Price = trade.Price,
                    Quantity = trade.Quantity,
                    BuyerFee = trade.BuyerFee,
                    SellerFee = trade.SellerFee
                });
            }

            foreach (var skill in tradingDay.SkillEffectsThisDay)
            {
                replayEvents.Add(new
                {
                    Type = "skill",
                    Month = game.CurrentMonthNumber,
                    Tick = game.CurrentTick,
                    SourcePlayerId = skill.SourcePlayerId,
                    TargetPlayerId = skill.TargetPlayerId,
                    SkillName = skill.SkillName,
                    Description = skill.Description
                });
            }
        }

        if (game.HasPendingSettlementNotification && game.CurrentTradingDay != null)
        {
            replayEvents.Add(BuildDaySettlementMessage(game));
        }

        // Snapshot order-book state under the trading-day lock; the recorder runs
        // on the game-loop thread while socket threads mutate these collections.
        var recMarket = game.CurrentTradingDay?.SnapshotMarket();

        var snapshot = new
        {
            Tick = game.CurrentTick,
            Stage = game.Stage.ToString(),
            Month = game.CurrentMonthNumber,
            Day = game.CurrentDayNumber,
            Scores = game.Players.Values
                .OrderBy(player => player.PlayerId)
                .Select(player => new
                {
                    PlayerId = player.PlayerId,
                    PlayerToken = player.Token,
                    PlayerName = player.DisplayName,
                    Score = game.Scoreboard.GetValueOrDefault(player.Token)
                })
                .ToList(),
            TradingDayTick = game.CurrentTradingDay?.CurrentTick,
            MarketState = recMarket != null ? new
            {
                recMarket.LastPrice,
                MidPrice = recMarket.MidPrice,
                Volume = recMarket.Volume
            } : null,
            Players = game.Players.Values.Select(p => new
            {
                PlayerId = p.PlayerId,
                Token = p.Token,
                PlayerName = p.DisplayName,
                Mora = p.Mora,
                Gold = p.Gold,
                FrozenMora = p.FrozenMora,
                FrozenGold = p.FrozenGold,
                LockedGold = p.LockedGold,
                MonthlyTradeCount = p.MonthlyTradeCount,
                ActiveCards = p.ActiveCards.Select(card => card.Name).ToList(),
                PendingOrders = game.CurrentTradingDay?.GetPlayerPendingOrders(p.Token).Select(o => new
                {
                    o.OrderId,
                    ArrivalTick = o.ArrivalTick,
                    Side = o.Side.ToString(),
                    o.Price,
                    o.Quantity,
                    o.RemainingQuantity,
                    Status = o.Status.ToString(),
                    Intent = o.Intent?.ToString() ?? ""
                }).ToList(),
                Nav = recMarket != null
                    ? p.CalculateNAV(recMarket.MidPrice)
                    : p.Mora
            }).ToList(),
            Events = replayEvents
        };

        recorder.Record(snapshot);
    }
}
