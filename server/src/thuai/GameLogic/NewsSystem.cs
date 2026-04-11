namespace Thuai.GameLogic;

public class NewsSystem
{
    private readonly Random _rng = new();
    private readonly int _intervalMin;
    private readonly int _intervalMax;
    private readonly int _researchWindow;
    private int _nextNewsId = 1;
    private int _nextNewsTick;

    private readonly List<News> _allNews = new();
    private News? _latestNews;
    private News? _preGeneratedNews;

    private static readonly string[] BullishNews =
    [
        "璃月港黄金需求激增，各大商会争相采购",
        "岩王帝君显灵，璃月矿脉产量骤降，金价看涨",
        "须弥学者预测璃月黄金储备不足，供应趋紧",
        "北斗船队延误，黄金运输受阻，市场供给减少",
        "天权星重启黄金储备计划，大量收购信号明确",
        "璃月七星宣布增加黄金战略储备",
        "稻妻锁国令解除，跨区黄金贸易需求暴增",
        "层岩巨渊矿难频发，黄金开采成本上升",
        "万民堂推出黄金主题宴会，民间藏金热潮兴起",
        "飞云商会发布看多研报，建议增持黄金头寸"
    ];

    private static readonly string[] BearishNews =
    [
        "璃月港发现新金矿，黄金供应量预计大增",
        "枫丹廷宣布抛售黄金储备，金价承压下行",
        "天权星缩减黄金采购预算，需求预期走弱",
        "新型合成黄金技术突破，实物黄金需求下降",
        "璃月总务司发布政策收紧黄金投机交易",
        "至冬国大使馆抛售大量黄金兑换摩拉",
        "须弥雨林发现大型金矿脉，远期供应预期充裕",
        "北斗船队提前到港，大批黄金入市供给充足",
        "璃月七星考虑征收黄金交易附加税",
        "飞云商会发布看空研报，建议减持黄金头寸"
    ];

    public NewsSystem(int intervalMin = 200, int intervalMax = 400, int researchWindow = 50)
    {
        _intervalMin = intervalMin;
        _intervalMax = intervalMax;
        _researchWindow = researchWindow;
        _nextNewsTick = _rng.Next(_intervalMin, _intervalMax + 1);
    }

    public News? Tick(int currentTick)
    {
        if (currentTick >= _nextNewsTick)
        {
            News news;
            if (_preGeneratedNews != null)
            {
                // Use the pre-generated news (created for insider preview) but
                // stamp it with the actual publish tick so timing is correct.
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
            _latestNews = news;
            _nextNewsTick = currentTick + _rng.Next(_intervalMin, _intervalMax + 1);
            return news;
        }
        return null;
    }

    /// <summary>
    /// Pre-generate the next news item so insider players can preview it early.
    /// The returned News has a placeholder PublishTick (the expected publish tick);
    /// the actual PublishTick is set when the news is formally published in Tick().
    /// Returns null if already pre-generated or if the next tick is too far away.
    /// </summary>
    public News? PreGenerateNextNews()
    {
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

    public bool IsWithinResearchWindow(int newsId, int currentTick)
    {
        var news = _allNews.Find(n => n.NewsId == newsId);
        if (news == null) return false;
        return currentTick - news.PublishTick <= _researchWindow;
    }

    public News? GetNews(int newsId)
    {
        return _allNews.Find(n => n.NewsId == newsId);
    }

    public News? LatestNews => _latestNews;

    public IReadOnlyList<News> AllNews => _allNews;

    public NewsSentiment? CurrentSentiment => _latestNews?.Sentiment;

    public int? NextNewsTickForInsider => _nextNewsTick > 3 ? _nextNewsTick - 3 : null;

    public int NextNewsTick => _nextNewsTick;

    public int? GetTicksUsed(int newsId, int submitTick)
    {
        var news = GetNews(newsId);
        if (news == null) return null;
        return submitTick - news.PublishTick;
    }

    public void Reset()
    {
        _allNews.Clear();
        _latestNews = null;
        _preGeneratedNews = null;
        _nextNewsTick = _rng.Next(_intervalMin, _intervalMax + 1);
    }
}
