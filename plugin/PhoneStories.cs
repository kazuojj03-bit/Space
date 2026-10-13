namespace SpacePhone;

/// <summary>Cumulative, observed shop activity. Never includes phone borrowing or securities.</summary>
public readonly record struct ShopActivity(int Purchases, int Sales, int Resales, int Customers, int Categories);

public sealed class StoryBook
{
    public int Version { get; set; } = 1;
    public StoryThread[] Threads { get; set; } = NewThreads();
    internal static StoryThread[] NewThreads() => new[] { new StoryThread(), new StoryThread(), new StoryThread() };
}

public sealed class StoryThread
{
    public int Phase { get; set; }
    public int Route { get; set; } = -1;
    public int Outcome { get; set; } = -1;
    public int AcceptedDay { get; set; }
    public int FollowupDay { get; set; }
    public ShopActivity Baseline { get; set; }
    public List<string> PublicCustomerIds { get; set; } = new();
    public List<string> PurchasedCategories { get; set; } = new();
    public bool Completed { get; set; }
    public bool Rewarded { get; set; }
    public int QueuedVisitScene { get; set; } = -1;
    public int LastVisitScene { get; set; } = -1;
    public int LastVisitDay { get; set; } = -1;
    public int VisitCount { get; set; }
}

public readonly record struct StoryChoice(string Label, int Key);
public readonly record struct StoryReply(bool Changed, int ResearchGain, string Message);

/// <summary>Original phone correspondence. All transitions are validated here, not in UI buttons.</summary>
public static class StoryRules
{
    private static readonly string[] Names = { "雨鸦", "阿岚", "洛克" };
    private static readonly string[] Roles = { "鉴价顾问 · 给价格留下证据", "社区医务员 · 把生意做得长久", "货运联络员 · 接上断开的货路" };
    private static readonly string[] Benefits = { "情报研究费用减免 10%", "优先联络费用减免 10", "每天增加一次免费供应商联络" };

    public static string GetTitle(int index) => InRange(index) ? Names[index] : "未知联系人";
    public static string GetRole(int index) => InRange(index) ? Roles[index] : "联系资料不可用";

    public static StoryBook Validate(StoryBook? book)
    {
        book ??= new StoryBook();
        var old = book.Threads;
        var normalized = StoryBook.NewThreads();
        for (int i = 0; i < 3; i++)
        {
            StoryThread? thread = old != null && i < old.Length ? old[i] : null;
            if (thread == null) continue;
            // A corrupt route cannot grant a benefit or reinterpret progress from another route.
            if (thread.Phase < 0 || thread.Phase > 4 ||
                (thread.Phase > 0 && !Binary(thread.Route)) ||
                (thread.Phase >= 3 && !Binary(thread.Outcome))) continue;
            thread.AcceptedDay = Math.Clamp(thread.AcceptedDay, 0, int.MaxValue - 1);
            thread.FollowupDay = Math.Clamp(thread.FollowupDay, 0, int.MaxValue - 1);
            thread.Baseline = Normalize(thread.Baseline);
            thread.QueuedVisitScene = thread.QueuedVisitScene is >= -1 and <= 3 ? thread.QueuedVisitScene : -1;
            thread.LastVisitScene = thread.LastVisitScene is >= -1 and <= 3 ? thread.LastVisitScene : -1;
            thread.LastVisitDay = Math.Clamp(thread.LastVisitDay, -1, 100000);
            thread.VisitCount = Math.Clamp(thread.VisitCount, 0, 100000);
            thread.PublicCustomerIds = NormalizeSet(thread.PublicCustomerIds, 120);
            thread.PurchasedCategories = NormalizeSet(thread.PurchasedCategories, 100);
            if (thread.Phase == 0)
            {
                thread.Route = -1; thread.Outcome = -1;
                thread.AcceptedDay = 0; thread.FollowupDay = 0;
                thread.Baseline = default;
                thread.PublicCustomerIds.Clear(); thread.PurchasedCategories.Clear();
            }
            else if (thread.Phase < 3)
            {
                thread.Outcome = -1; thread.FollowupDay = 0;
            }
            else thread.FollowupDay = Math.Max(thread.AcceptedDay, thread.FollowupDay);
            // Completion is one atomic save transition. Never repay a completed legacy thread.
            thread.Completed = thread.Phase == 4;
            thread.Rewarded = thread.Phase == 4;
            normalized[i] = thread;
        }
        book.Threads = normalized;
        book.Version = 1;
        return book;
    }

    public static string GetMessage(StoryBook book, int index, int day, ShopActivity activity)
    {
        if (!TryThread(book, index, out var t)) return "联系资料损坏，请重新载入手机。";
        day = Math.Max(0, day);
        ShopActivity progress = Delta(activity, t.Baseline);
        if (t.Phase == 0) return Intro(index);
        if (t.Phase == 1)
        {
            string request = Requirement(index, t.Route, progress, t);
            string wait = day > t.AcceptedDay
                ? (Satisfied(index, t.Route, progress, t) ? "记录齐了，可以发给我。" : "继续在柜台完成交易，我等你的记录。")
                : "今天先照常营业，至少到下一天再汇总给我。";
            return Accepted(index, t.Route) + "\n\n" + request + "\n" + wait + "\n只统计接下这件事之后的真实店铺经营。";
        }
        if (t.Phase == 2) return Conclusion(index, t.Route) +
            "\n\n长期合作留下服务优惠；研究材料给予 1 点资料，并将每日研判上限增至 3。两种回报只能选一种。资料满时奖励先归档，后续研究自动补入。";
        if (t.Phase == 3)
        {
            string choice = t.Outcome == 0 ? "长期合作：" + Benefits[index] : "研究材料：获得 1 点研究，日报告上限增至 3";
            return Followup(index, t.Outcome) + "\n\n已确定 " + choice +
                (day > t.FollowupDay ? "\n回复已到，确认收下即可完成。" : "\n我需要一个晚上整理，明天再回你。") +
                "\n选择已经寄出，不能改寄另一条路线。";
        }
        if (t.Phase == 4) return CompletedMessage(index, t.Outcome) + "\n\n" +
            (t.Outcome == 0 ? "已建立合作 · " + Benefits[index] : "研究材料已归档 · 1 点研究已保存，日报告上限增至 3") +
            "\n这段通信已完成，回报只结算一次。";
        return "联系资料不可用。";
    }

    public static StoryChoice[] Choices(StoryBook book, int index, int day, ShopActivity activity)
    {
        if (!TryThread(book, index, out var t) || t.Completed || t.Rewarded || day < 0) return Array.Empty<StoryChoice>();
        if (t.Phase == 0) return index switch
        {
            0 => new[] { new StoryChoice("核对两笔转售", 0), new StoryChoice("观察四笔销售", 1) },
            1 => new[] { new StoryChoice("维持社区补给", 0), new StoryChoice("联络两类顾客", 1) },
            _ => new[] { new StoryChoice("拓宽进货品类", 0), new StoryChoice("打通周转货路", 1) }
        };
        if (t.Phase == 1 && day > t.AcceptedDay && Satisfied(index, t.Route, Delta(activity, t.Baseline), t))
            return new[] { new StoryChoice("发送经营记录", 2) };
        if (t.Phase == 2) return new[] { new StoryChoice("建立长期合作", 0), new StoryChoice("整理研究材料", 1) };
        if (t.Phase == 3 && day > t.FollowupDay) return new[] { new StoryChoice("收下回信", 3) };
        return Array.Empty<StoryChoice>();
    }

    public static StoryReply Reply(StoryBook book, int index, int choice, int day, ShopActivity activity)
    {
        if (!TryThread(book, index, out var t)) return Reject("联系资料不可用。" );
        if (!Choices(book, index, day, activity).Any(c => c.Key == choice))
            return Reject(t.Completed ? "这段通信已经完成。" : "这条回复暂时不能发送，请先完成当前条件。" );
        if (t.Phase == 0)
        {
            t.Route = choice;
            t.Baseline = Normalize(activity);
            t.PublicCustomerIds = new(); t.PurchasedCategories = new();
            t.AcceptedDay = Math.Clamp(day, 0, int.MaxValue - 1);
            t.Phase = 1;
            return Changed("已回复" + Names[index] + "。路线已确定，开始记录新的经营活动。" );
        }
        if (t.Phase == 1)
        {
            t.Phase = 2;
            return Changed("经营记录已送达，" + Names[index] + "有新的回复。" );
        }
        if (t.Phase == 2)
        {
            t.Outcome = choice;
            t.FollowupDay = Math.Clamp(day, 0, int.MaxValue - 1);
            t.Phase = 3;
            return Changed("选择已寄出，等待次日的回信。" );
        }
        // A single transition owns the reward. Rendering, loading and day polling never grant it.
        t.Phase = 4; t.Completed = true; t.Rewarded = true;
        return new StoryReply(true, t.Outcome == 1 ? 1 : 0,
            t.Outcome == 1 ? "研究材料已归档，获得 1 点研究。" : "长期合作已建立：" + Benefits[index] + "。" );
    }

    public static bool HasCommunityBenefit(StoryBook book, int index) =>
        TryThread(book, index, out var t) && t.Phase == 4 && t.Completed && t.Rewarded && t.Outcome == 0;

    /// <summary>Call only after one confirmed native merchandise trade was accepted by BusinessRules.</summary>
    public static bool RecordTrade(StoryBook book, int day, bool purchase, string customerId, string category, int amount)
    {
        if (book?.Threads == null || day < 0 || amount <= 0) return false;
        customerId = CleanKey(customerId, 120);
        category = CleanKey(category, 100).ToLowerInvariant();
        bool changed = false;
        for (int i = 0; i < Math.Min(3, book.Threads.Length); i++)
        {
            if (!TryThread(book, i, out var t) || t.Phase != 1 || t.Completed || t.Rewarded || day < t.AcceptedDay) continue;
            t.PublicCustomerIds ??= new(); t.PurchasedCategories ??= new();
            if (customerId.Length > 0 && t.PublicCustomerIds.Count < 8 && !t.PublicCustomerIds.Contains(customerId))
            { t.PublicCustomerIds.Add(customerId); changed = true; }
            if (purchase && category.Length > 0 && t.PurchasedCategories.Count < 8 && !t.PurchasedCategories.Contains(category))
            { t.PurchasedCategories.Add(category); changed = true; }
        }
        return changed;
    }

    private static string CleanKey(string? key, int maxLength)
    {
        key = (key ?? "").Replace("\0", "").Trim();
        return key.Length > maxLength ? key[..maxLength] : key;
    }
    private static List<string> NormalizeSet(List<string>? keys, int maxLength) => (keys ?? new())
        .Select(key => CleanKey(key, maxLength)).Where(key => key.Length > 0)
        .Distinct(StringComparer.Ordinal).Take(8).ToList();

    private static StoryReply Reject(string message) => new(false, 0, message);
    private static StoryReply Changed(string message) => new(true, 0, message);
    private static bool Binary(int value) => value is 0 or 1;
    private static bool InRange(int index) => index >= 0 && index < 3;
    private static bool TryThread(StoryBook? book, int index, out StoryThread thread)
    {
        thread = null!;
        if (!InRange(index) || book?.Threads == null || index >= book.Threads.Length || book.Threads[index] == null) return false;
        thread = book.Threads[index];
        return thread.Phase >= 0 && thread.Phase <= 4 &&
            (thread.Phase == 0 || Binary(thread.Route)) && (thread.Phase < 3 || Binary(thread.Outcome));
    }
    private static ShopActivity Normalize(ShopActivity activity) => new(
        Math.Max(0, activity.Purchases), Math.Max(0, activity.Sales), Math.Max(0, activity.Resales),
        Math.Max(0, activity.Customers), Math.Max(0, activity.Categories));
    private static ShopActivity Delta(ShopActivity activity, ShopActivity baseline) => new(
        Difference(activity.Purchases, baseline.Purchases), Difference(activity.Sales, baseline.Sales),
        Difference(activity.Resales, baseline.Resales), Difference(activity.Customers, baseline.Customers),
        Difference(activity.Categories, baseline.Categories));
    private static int Difference(int count, int baseline) => (int)Math.Clamp((long)count - baseline, 0, int.MaxValue);
    private static bool Satisfied(int index, int route, ShopActivity p, StoryThread t) => (index, route) switch
    {
        (0, 0) => p.Resales >= 2,
        (0, 1) => p.Sales >= 4,
        (1, 0) => p.Purchases >= 3 && p.Sales >= 3,
        (1, 1) => t.PublicCustomerIds.Count >= 2 && p.Sales >= 2,
        (2, 0) => t.PurchasedCategories.Count >= 2 && p.Sales >= 2,
        (2, 1) => p.Sales >= 5,
        _ => false
    };
    private static string Requirement(int index, int route, ShopActivity p, StoryThread t) => (index, route) switch
    {
        (0, 0) => $"有成本记录的转售 {Math.Min(2, p.Resales)}/2",
        (0, 1) => $"柜台销售 {Math.Min(4, p.Sales)}/4",
        (1, 0) => $"柜台收购 {Math.Min(3, p.Purchases)}/3 · 柜台销售 {Math.Min(3, p.Sales)}/3",
        (1, 1) => $"接取后成交顾客群 {Math.Min(2, t.PublicCustomerIds.Count)}/2 · 柜台销售 {Math.Min(2, p.Sales)}/2",
        (2, 0) => $"接取后进货品类 {Math.Min(2, t.PurchasedCategories.Count)}/2 · 柜台销售 {Math.Min(2, p.Sales)}/2",
        (2, 1) => $"柜台销售 {Math.Min(5, p.Sales)}/5",
        _ => "路线资料不可用"
    };
    private static string Intro(int index) => index switch
    {
        0 => "我是雨鸦，给楼下几家铺子做鉴价复核。\n\n很多人记得售价，忘了自己付过多少。等到租金来了，才发现忙了一天只是换了个地方放钱。\n\n你愿意跟踪两笔有成本记录的转售，还是先观察四笔普通销售？前者慢些，却能沿着同一件货核对；后者容易起步，但信息更粗。别把记录当鉴定证书，我们只看你实际做过的生意。",
        1 => "我是阿岚，社区诊所今晚又在清点耗材。\n\n我不需要你免费送东西，也不会替所有陌生人担保。我想知道，一家照常收钱的小店，能不能把进货和售货接成一条稳定的补给线。\n\n你可以在接单后各完成三笔收购与销售，或者跟两个不同顾客群完成交易，再完成两笔销售。老客户也能参与，但同一顾客群只算一次。一个考验周转，一个考验联系的广度。",
        _ => "洛克，货运联络员。你的号码是从旧货集散名单上找到的。\n\n最近有条货路断了。大家都想让我先保证利润，可我连下一批货会不会堵在门外都不敢保证。\n\n给我一份新记录：接单后买入两种不同品类，再完成两笔销售；或者用现有货路完成五笔销售。熟悉的品类也算，但要有新的收购。分散进货会占用现金，盯着周转则会放过一些新机会。"
    };
    private static string Accepted(int index, int route) => (index, route) switch
    {
        (0, 0) => "雨鸦：就跟着同一件货走。收购后再售出，手机能认出成本的两笔转售，才算这次记录。接单前的旧生意不补算。",
        (0, 1) => "雨鸦：先从四笔销售看柜台的节奏。你照常定价，不必为了凑数低价甩货；这不是比谁卖得快。",
        (1, 0) => "阿岚：进货和售货各三笔。货不必都卖给我，诊所也不是你的提款机；我要看的是铺子能不能继续周转。",
        (1, 1) => "阿岚：从现在起，跟两个不同顾客群完成有付款的柜台交易，再完成两笔销售。老客户也算，同一顾客群只记一次；你仍可以拒绝不合适的交易。",
        (2, 0) => "洛克：从现在起收购两种不同品类，再完成两笔销售。以前买过的类别也算，但接单前的交易不补算。新记录会比一张空口的进货计划更有说服力。",
        _ => "洛克：那就走周转路线。五笔销售，只看真实柜台成交。别用股票买卖或贷款来凑数，货路不认识那些数字。"
    };
    private static string Conclusion(int index, int route) => index switch
    {
        0 => route == 0
            ? "雨鸦：两笔货都走完了。成本能找到，才知道一口漂亮的报价到底值不值得接。我的复核表可以给你留一格。"
            : "雨鸦：四笔销售到了。记录说明柜台还在转，但销量不能替你回答每件货赚没赚钱。下一步可以靠长期咨询补细节，也可以自己研究。",
        1 => route == 0
            ? "阿岚：两头的记录接上了。社区需要会收钱、也能持续开门的铺子，不需要靠下一次好运才能进货的英雄。"
            : "阿岚：两类客人都留下了成交记录。我们都知道，不是每张面孔都可靠。你保留了判断的余地，这比一张无条件欢迎的广告更有用。",
        _ => route == 0
            ? "洛克：两种品类真的过了你的柜台。分散货路会占用现金，不过再有一条路线断掉时，你至少不只剩一个选择。"
            : "洛克：五笔销售够我画出这条周转线。它还谈不上壮观，但货在移动。大合同之前，最好先证明小合同不会把铺子拖垮。"
    };
    private static string Followup(int index, int outcome) => index switch
    {
        0 => outcome == 0 ? "雨鸦：我把你列进合作复核名单。手机里的行情研究费用会少一些，柜台鉴定仍照常；我们只是让记录派上用场。" : "雨鸦：明白，保留你的独立判断。我会把记录整理成一份可研究的复核方法，之后的情报研究仍按普通费用。",
        1 => outcome == 0 ? "阿岚：我替你把联络流程写下来。优先联系会更便宜，但不会让我替你保证货源或顾客。" : "阿岚：我把补给记录匿名整理成研究材料。你不会拿到长期联络优惠，不过这份经验属于你自己。",
        _ => outcome == 0 ? "洛克：我给你的每日第一通供应商联络留个优惠。货价照旧，先别把省下的联络费都花在下一箱未知货上。" : "洛克：那我把货路记录整理出来供你研究。以后每通联络照常计费，你也不欠我一个长期承诺。"
    };
    private static string CompletedMessage(int index, int outcome) => index switch
    {
        0 => outcome == 0 ? "雨鸦：名单已经生效。手机情报台的行情研究费用有减免，柜台的商品鉴定仍靠你自己完成。我们靠记录说话。" : "雨鸦：复核方法发过去了。数据不会替你承担风险，但它能让你知道自己为什么承担。",
        1 => outcome == 0 ? "阿岚：联络流程落了地。社区仍然会缺东西，你的铺子也仍然要付租金；能持续经营，才谈得上下一次合作。" : "阿岚：补给研究交给你。保持进货与售货之间的余地，不要让任何一头把另一头勒死。",
        _ => outcome == 0 ? "洛克：货路接好了。每天的第一通联络有优惠，真正买什么、敢压多少现金，还是你自己决定。" : "洛克：货路研究到了。你的柜台比地图更诚实，下一次路线断掉时，记得回来看看这些记录。"
    };
}
