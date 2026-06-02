namespace Thuai.GameLogic;

using System.Numerics;

public class ResearchSystem
{
    private readonly NewsSystem _newsSystem;
    private readonly long _baseReward;
    private readonly int _researchWindow;
    private readonly int _settlementDelay;
    private readonly long _maxAbsRewardPerReport;
    private readonly long _positiveRewardBudgetPerPlayerPerMonth;
    private readonly List<ResearchReport> _pendingReports = new();
    private readonly List<ResearchReport> _settledReports = new();
    private readonly Dictionary<string, long> _positiveRewardsByPlayer = new();

    public ResearchSystem(NewsSystem newsSystem, long baseReward = 10000,
                          int researchWindow = 2, int settlementDelay = 3,
                          long maxAbsRewardPerReport = 100000,
                          long positiveRewardBudgetPerPlayerPerMonth = 200000)
    {
        _newsSystem = newsSystem;
        _baseReward = baseReward;
        _researchWindow = researchWindow;
        _settlementDelay = settlementDelay;
        _maxAbsRewardPerReport = Math.Max(0, maxAbsRewardPerReport);
        _positiveRewardBudgetPerPlayerPerMonth = Math.Max(0, positiveRewardBudgetPerPlayerPerMonth);
    }

    public ResearchReport? SubmitReport(string playerToken, int newsId, Prediction prediction,
        int currentTick, int? playerResearchWindow = null, double decayMultiplier = 1.0)
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
            SubmitDay = currentTick,
            SettlementDay = news.PublishTick + _settlementDelay
        };

        _pendingReports.Add(report);
        return report;
    }

    public List<ResearchReport> SettleReports(int currentTick, Func<int, long> getPriceAtTick)
    {
        var settled = new List<ResearchReport>();
        var dueReports = _pendingReports
            .Where(report =>
            {
                var news = _newsSystem.GetNews(report.NewsId);
                return news != null && currentTick >= news.PublishTick + _settlementDelay;
            })
            .GroupBy(report => report.NewsId)
            .ToList();

        foreach (var group in dueReports)
        {
            var news = _newsSystem.GetNews(group.Key);
            if (news == null)
                continue;

            int settlementTick = news.PublishTick + _settlementDelay;
            long priceAtPublish = getPriceAtTick(news.PublishTick);
            long priceAtSettlement = getPriceAtTick(settlementTick);
            long actualChange = priceAtSettlement - priceAtPublish;
            long magnitude = Math.Abs(actualChange);

            var ordered = group
                .OrderBy(report => report.SubmitTick)
                .ThenBy(report => report.PlayerToken)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                var report = ordered[i];
                report.ActualChange = actualChange;
                report.SubmissionRank = i + 1;

                bool isCorrect = report.Prediction switch
                {
                    Prediction.Long => actualChange > 0,
                    Prediction.Short => actualChange < 0,
                    _ => actualChange == 0
                };

                if (news.IsFake && report.PlayerToken != news.SourcePlayer)
                    isCorrect = false;

                report.IsCorrect = isCorrect;
                if (magnitude == 0)
                {
                    report.Reward = 0;
                }
                else
                {
                    long rankMultiplier = Math.Max(1, ordered.Count - i);
                    long rewardMagnitude = CalculateRewardMagnitude(priceAtPublish, magnitude, rankMultiplier);
                    report.Reward = isCorrect
                        ? ApplyPositiveRewardBudget(report.PlayerToken, rewardMagnitude)
                        : -rewardMagnitude;
                }

                _pendingReports.Remove(report);
                _settledReports.Add(report);
                settled.Add(report);
            }
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
        _positiveRewardsByPlayer.Clear();
    }

    private long CalculateRewardMagnitude(long priceAtPublish, long magnitude, long rankMultiplier)
    {
        long referencePrice = Math.Max(1, priceAtPublish);
        BigInteger scaledReward = (BigInteger)_baseReward * rankMultiplier * magnitude;
        long rewardMagnitude = ClampToInt64(scaledReward / referencePrice);
        if (_maxAbsRewardPerReport > 0)
            rewardMagnitude = Math.Min(rewardMagnitude, _maxAbsRewardPerReport);
        return Math.Max(0, rewardMagnitude);
    }

    private long ApplyPositiveRewardBudget(string playerToken, long rewardMagnitude)
    {
        if (rewardMagnitude <= 0)
            return 0;

        if (_positiveRewardBudgetPerPlayerPerMonth <= 0)
            return rewardMagnitude;

        long usedBudget = _positiveRewardsByPlayer.GetValueOrDefault(playerToken, 0);
        long remainingBudget = Math.Max(0, _positiveRewardBudgetPerPlayerPerMonth - usedBudget);
        long appliedReward = Math.Min(rewardMagnitude, remainingBudget);
        _positiveRewardsByPlayer[playerToken] = usedBudget + appliedReward;
        return appliedReward;
    }

    private static long ClampToInt64(BigInteger value)
    {
        if (value > long.MaxValue)
            return long.MaxValue;
        if (value < long.MinValue)
            return long.MinValue;
        return (long)value;
    }
}
