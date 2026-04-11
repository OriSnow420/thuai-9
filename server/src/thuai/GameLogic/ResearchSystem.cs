namespace Thuai.GameLogic;

public class ResearchSystem
{
    private readonly NewsSystem _newsSystem;
    private readonly long _baseReward;
    private readonly int _researchWindow;
    private readonly int _settlementDelay;
    private readonly List<ResearchReport> _pendingReports = new();
    private readonly List<ResearchReport> _settledReports = new();

    public ResearchSystem(NewsSystem newsSystem, long baseReward = 10000,
                          int researchWindow = 50, int settlementDelay = 100)
    {
        _newsSystem = newsSystem;
        _baseReward = baseReward;
        _researchWindow = researchWindow;
        _settlementDelay = settlementDelay;
    }

    public ResearchReport? SubmitReport(string playerToken, int newsId, Prediction prediction,
                                        int currentTick, int? playerResearchWindow = null,
                                        double decayMultiplier = 1.0)
    {
        var news = _newsSystem.GetNews(newsId);
        if (news == null) return null;

        int effectiveWindow = playerResearchWindow ?? _researchWindow;
        int ticksUsed = currentTick - news.PublishTick;
        if (ticksUsed < 0 || ticksUsed > effectiveWindow) return null;

        if (_pendingReports.Any(r => r.PlayerToken == playerToken && r.NewsId == newsId))
            return null;
        if (_settledReports.Any(r => r.PlayerToken == playerToken && r.NewsId == newsId))
            return null;

        var report = new ResearchReport
        {
            PlayerToken = playerToken,
            NewsId = newsId,
            Prediction = prediction,
            SubmitTick = currentTick,
            TicksUsed = ticksUsed,
            DecayMultiplier = decayMultiplier,
            EffectiveWindow = effectiveWindow
        };

        _pendingReports.Add(report);
        return report;
    }

    public List<ResearchReport> SettleReports(int currentTick, Func<int, long> getMidPriceAtTick)
    {
        var settled = new List<ResearchReport>();

        for (int i = _pendingReports.Count - 1; i >= 0; i--)
        {
            var report = _pendingReports[i];
            var news = _newsSystem.GetNews(report.NewsId);
            if (news == null) continue;

            int settlementTick = news.PublishTick + _settlementDelay;
            if (currentTick < settlementTick) continue;

            long priceAtPublish = getMidPriceAtTick(news.PublishTick);
            long priceAtSettlement = getMidPriceAtTick(settlementTick);
            long actualChange = priceAtSettlement - priceAtPublish;

            int effectiveWindow = report.EffectiveWindow;

            if (news.IsFake && report.PlayerToken != news.SourcePlayer)
            {
                report.IsCorrect = false;
                double timeFactor = 1.0 - (double)report.TicksUsed / effectiveWindow * report.DecayMultiplier;
                report.Reward = -(long)(_baseReward * timeFactor * Math.Max(Math.Abs(actualChange), 1));

                _pendingReports.RemoveAt(i);
                _settledReports.Add(report);
                settled.Add(report);
                continue;
            }

            if (report.Prediction == Prediction.Hold)
            {
                report.IsCorrect = true;
                report.Reward = 0;
            }
            else
            {
                bool priceWentUp = actualChange > 0;
                bool priceWentDown = actualChange < 0;

                if (report.Prediction == Prediction.Long)
                    report.IsCorrect = priceWentUp;
                else
                    report.IsCorrect = priceWentDown;

                if (actualChange == 0)
                    report.IsCorrect = false;

                double timeFactor = 1.0 - (double)report.TicksUsed / effectiveWindow * report.DecayMultiplier;
                long rewardMagnitude = (long)(_baseReward * timeFactor * Math.Abs(actualChange));

                report.Reward = report.IsCorrect == true ? rewardMagnitude : -rewardMagnitude;
            }

            _pendingReports.RemoveAt(i);
            _settledReports.Add(report);
            settled.Add(report);
        }

        return settled;
    }

    public List<ResearchReport> GetPendingReports(string playerToken)
    {
        return _pendingReports.Where(r => r.PlayerToken == playerToken).ToList();
    }

    public IReadOnlyList<ResearchReport> PendingReports => _pendingReports;

    public IReadOnlyList<ResearchReport> SettledReports => _settledReports;

    public void Reset()
    {
        _pendingReports.Clear();
        _settledReports.Clear();
    }
}
