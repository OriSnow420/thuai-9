namespace Thuai.GameLogic;

public class NewsSystem
{
    private static readonly int[] DefaultScheduledNewsTicks = [1, 11, 21];
    private readonly Random _rng = new();
    private readonly int _researchWindow;
    private readonly int _sentimentDurationTicks;
    private readonly int[] _scheduledNewsTicks;
    private int _nextNewsId = 1;
    private int _scheduledIndex;
    private int _nextNewsTick;

    private readonly List<News> _allNews = new();
    private readonly List<News> _realNews = new();
    private News? _latestNews;
    private News? _preGeneratedNews;

    private static readonly string[] BullishNews =
    [
        "华清街‘大妈扫货团’倾巢出动，各大金店柜台已被扫荡至只剩标签",
        "知名算命先生断言本月‘五行缺金’，市民纷纷疯狂囤积金条以求转运",
        "华清街神秘富豪宣布将用纯金铺设私人厕所，实物金现货供应瞬间告急",
        "运金马车在桥头发生连环追尾，全城金条延迟入库，现货溢价直线飙升",
        "街道办宣布：为抵御摩拉通胀，即日起无限期开启黄金战略储备收购计划",
        "商会主席之子大婚，钦定‘黄金铺路’，十里长街库存被瞬间清空",
        "隔壁街区宣布严禁黄金流出，华清街金价应声起飞，空头集体‘上天’",
        "矿井深处发现爱吃金属的稀有怪兽，开采被迫暂停，黄金生产成本翻倍",
        "华清街流行起‘点金箔奶茶’，年轻人连摩拉都不存了，全喝进了肚子里",
        "华清街‘懂王’发布万字研报：黄金不仅是货币，更是信仰，建议满仓"
    ];

    private static readonly string[] BearishNews =
    [
        "华清街公厕翻修意外挖出前朝大金库，黄金市场供应量瞬间‘爆表’",
        "隔壁街区银行为了填补亏空，正在低价抛售黄金换取摩拉，金价承压",
        "街道办决定取消今年的‘金漆刷墙’工程，黄金采购预算被砍掉九成",
        "某落魄民科声称发明‘点石成金术’，虽未证实但已引发市场恐慌性抛售",
        "执法队突击检查非法炒金团伙，多个‘致富群’被封，市场陷入流动性危机",
        "华清街著名黄金多头突然改行卖烧饼，临走前清空了所有金条仓位",
        "满载黄金的船队因顺风提前抵达，码头工位被金砖填满，供应彻底饱和",
        "官方考虑征收‘反暴利税’，持有黄金超过十斤者需额外缴纳巨额摩拉",
        "交易所被爆出‘黄金券’超发，实物黄金根本不够兑换，金价应声暴跌",
        "曾经坚定的看多专家今日改口：黄金太重不好带，还是存摩拉最实在"
    ];

    public NewsSystem(int intervalMin = 1, int intervalMax = 1, int researchWindow = 2,
        IReadOnlyList<int>? scheduledNewsTicks = null, int sentimentDurationTicks = 3)
    {
        _researchWindow = researchWindow;
        _sentimentDurationTicks = Math.Max(0, sentimentDurationTicks);
        _scheduledNewsTicks = NormalizeScheduledTicks(scheduledNewsTicks);
        _scheduledIndex = 0;
        _nextNewsTick = _scheduledNewsTicks.Length > 0 ? _scheduledNewsTicks[0] : int.MaxValue;
    }

    public News? Tick(int currentTick)
    {
        if (_scheduledIndex >= _scheduledNewsTicks.Length || currentTick < _nextNewsTick)
            return null;

        News news;
        if (_preGeneratedNews != null)
        {
            news = new News
            {
                NewsId = _preGeneratedNews.NewsId,
                PublishTick = currentTick,
                Content = _preGeneratedNews.Content,
                Sentiment = _preGeneratedNews.Sentiment,
                IsFake = false,
                SourcePlayer = null
            };
            _preGeneratedNews = null;
        }
        else
        {
            news = GenerateNews(currentTick);
        }

        _allNews.Add(news);
        _realNews.Add(news);
        _latestNews = news;

        _scheduledIndex++;
        _nextNewsTick = _scheduledIndex < _scheduledNewsTicks.Length
            ? _scheduledNewsTicks[_scheduledIndex]
            : int.MaxValue;

        return news;
    }

    /// <summary>
    /// Pre-generate the next news item so insider players can preview it early.
    /// The returned News has a placeholder PublishTick (the expected publish tick);
    /// the actual PublishTick is set when the news is formally published in Tick().
    /// Returns null when no scheduled news remains this month.
    /// </summary>
    public News? PreGenerateNextNews()
    {
        if (_scheduledIndex >= _scheduledNewsTicks.Length)
            return null;

        if (_preGeneratedNews != null)
            return _preGeneratedNews;

        _preGeneratedNews = GenerateNews(_nextNewsTick);
        return _preGeneratedNews;
    }

    private News GenerateNews(int publishTick)
    {
        var sentiment = _rng.Next(2) == 0 ? NewsSentiment.Bullish : NewsSentiment.Bearish;
        var templates = sentiment == NewsSentiment.Bullish ? BullishNews : BearishNews;
        var content = templates[_rng.Next(templates.Length)];

        return new News
        {
            NewsId = _nextNewsId++,
            PublishTick = publishTick,
            Content = content,
            Sentiment = sentiment,
            IsFake = false,
            SourcePlayer = null
        };
    }

    public News InjectFakeNews(int currentTick, string sourcePlayer, NewsSentiment sentiment)
    {
        var templates = sentiment == NewsSentiment.Bullish ? BullishNews : BearishNews;
        var content = templates[_rng.Next(templates.Length)];

        var news = new News
        {
            NewsId = _nextNewsId++,
            PublishTick = currentTick,
            Content = content,
            Sentiment = sentiment,
            IsFake = true,
            SourcePlayer = sourcePlayer
        };

        _allNews.Add(news);
        _latestNews = news;
        return news;
    }

    public News? GetNews(int newsId)
    {
        return _allNews.Find(n => n.NewsId == newsId);
    }

    public bool IsWithinResearchWindow(int newsId, int currentTick)
    {
        var news = GetNews(newsId);
        if (news == null)
            return false;

        return currentTick - news.PublishTick <= _researchWindow;
    }

    public News? LatestNews => _latestNews;

    public News? LatestRealNews => _realNews.Count > 0 ? _realNews[^1] : null;

    public IReadOnlyList<News> AllNews => _allNews;

    public NewsSentiment? CurrentSentiment => LatestRealNews?.Sentiment;

    public NewsSentiment? GetEffectiveSentiment(int currentTick, int reactionDelayTicks = 0, bool includeFakeNews = false)
    {
        int effectiveTick = currentTick - Math.Max(0, reactionDelayTicks);
        if (effectiveTick < 0)
            return null;

        var source = includeFakeNews ? _allNews : _realNews;
        for (int index = source.Count - 1; index >= 0; index--)
        {
            var news = source[index];
            if (news.PublishTick > effectiveTick)
                continue;

            return effectiveTick - news.PublishTick <= _sentimentDurationTicks
                ? news.Sentiment
                : null;
        }

        return null;
    }

    public int? NextNewsTickForInsider => _nextNewsTick is > 3 and < int.MaxValue ? _nextNewsTick - 3 : null;
    public int PreviewTick => _scheduledIndex < _scheduledNewsTicks.Length ? Math.Max(0, _nextNewsTick - 3) : -1;

    public int NextNewsTick => _nextNewsTick;

    public News CreateSpoofedView(News source)
    {
        var sentiment = _rng.Next(2) == 0 ? NewsSentiment.Bullish : NewsSentiment.Bearish;
        var templates = sentiment == NewsSentiment.Bullish ? BullishNews : BearishNews;

        return new News
        {
            NewsId = source.NewsId,
            PublishTick = source.PublishTick,
            Content = templates[_rng.Next(templates.Length)],
            Sentiment = sentiment,
            IsFake = source.IsFake,
            SourcePlayer = source.SourcePlayer
        };
    }

    public int? GetTicksUsed(int newsId, int submitTick)
    {
        var news = GetNews(newsId);
        if (news == null) return null;
        return submitTick - news.PublishTick;
    }

    public void Reset()
    {
        _allNews.Clear();
        _realNews.Clear();
        _latestNews = null;
        _preGeneratedNews = null;
        _scheduledIndex = 0;
        _nextNewsTick = _scheduledNewsTicks.Length > 0 ? _scheduledNewsTicks[0] : int.MaxValue;
    }

    private static int[] NormalizeScheduledTicks(IReadOnlyList<int>? scheduledNewsTicks)
    {
        if (scheduledNewsTicks == null)
            return [.. DefaultScheduledNewsTicks];
        if (scheduledNewsTicks.Count == 0)
            return [];

        return scheduledNewsTicks
            .Where(tick => tick >= 0)
            .Distinct()
            .OrderBy(tick => tick)
            .ToArray();
    }
}
