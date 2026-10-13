namespace SpacePhone;

public enum GuidePrimaryKind { Invite, ReturnShop, Research, CheckIn }

public sealed record GuideCard(string Heading, string Step, string Progress,
    string Instruction, string PrimaryLabel, GuidePrimaryKind PrimaryKind, int Scene);

/// <summary>Read-only guidance for the same transitions enforced by StoryRules.</summary>
public static class TaskGuide
{
    public static GuideCard Build(StoryBook? book, int index, int day, ShopActivity activity, int researchPoints)
    {
        if (day < 0 || !TryThread(book, index, out var thread))
            return new("任务指南", "等待店铺资料", "请先进入店铺", "进入店铺后打开通讯，选择一位联系人。",
                "回到店铺", GuidePrimaryKind.ReturnShop, -1);
        string name = StoryRules.GetTitle(index);
        if (thread.QueuedVisitScene is >= 0 and <= 3)
            return new(name, "已预约到店", "保持营业 · 等待柜台空闲",
                $"先处理柜台前的顾客，保持店铺营业。柜台空下来后{name}会来；无需重复预约。到店后在柜台对话中继续。",
                "回柜台等候", GuidePrimaryKind.ReturnShop, thread.QueuedVisitScene);

        if (thread.Phase == 0)
            return new(name, "第 1 步 / 4 · 当面选路线", "两条路线，任选一条",
                $"营业时让柜台空出来，邀请{name}到店。在柜台对话中挑一条适合现有货源的路线，再开始经营；接取前的交易不会补算。",
                "邀请到店选路线", GuidePrimaryKind.Invite, 0);

        if (thread.Phase == 1)
        {
            var progress = Delta(activity, thread.Baseline);
            string counts = Progress(index, thread, progress);
            if (HasChoice(book!, index, day, activity, 2))
                return new(name, "第 2 步 / 4 · 当面复盘", counts,
                    $"记录已经齐了。营业时让柜台空出来，邀请{name}来复盘；随后当面选择长期合作或研究材料，两种回报任选一种。",
                    "邀请到店复盘", GuidePrimaryKind.Invite, 1);
            if (HasChoice(book!, index, int.MaxValue, activity, 2))
                return new(name, "第 2 步 / 4 · 明天汇总", counts,
                    $"今天的记录已经够了，{NextDay(thread.AcceptedDay)}再邀请{name}到柜台复盘。现在照常营业，或在收工后进入下一天；无需再凑交易。",
                    "继续营业，明天汇总", GuidePrimaryKind.ReturnShop, -1);
            return new(name, "第 2 步 / 4 · 完成经营目标", counts,
                Tip(index, thread.Route) + " 只统计接取后的真实柜台成交；完成后至少到下一天，再邀请到店复盘。",
                "回柜台做生意", GuidePrimaryKind.ReturnShop, -1);
        }

        // A 2.0 save may already be waiting at its conclusion. Continue that
        // exact choice at the counter rather than resetting its trade history.
        if (thread.Phase == 2)
            return new(name, "第 3 步 / 4 · 当面选回报", "经营记录已确认",
                $"邀请{name}到柜台接着聊。选择长期合作，可得到服务优惠；选择研究材料，可获得 1 点资料并提高日报告上限。已做过的经营记录会保留。",
                "邀请到店选回报", GuidePrimaryKind.Invite, 1);

        if (thread.Phase == 3)
        {
            if (!HasChoice(book!, index, day, activity, 3))
                return new(name, "第 4 步 / 4 · 明天交接", "回报路线已经确定",
                    $"{name}正在整理回报，{NextDay(thread.FollowupDay)}可邀请到柜台完成交接。照常营业或收工进入下一天，选择不会改变。" +
                    (thread.Outcome == 1 && researchPoints >= 5 ? " 资料满也能正常交接，新资料会归档，在之后消耗研究资料时自动补入。" : ""),
                    "继续营业，明天见", GuidePrimaryKind.ReturnShop, -1);
            if (thread.Outcome == 1 && researchPoints >= 5)
                return new(name, "第 4 步 / 4 · 当面交接", "资料已满 · 新资料将归档",
                    $"营业时让柜台空出来，邀请{name}来完成交接。新资料会先归档，之后消耗研究资料时自动补入；无需腾出空位，回报只结算一次。",
                    "邀请到店完成交接", GuidePrimaryKind.Invite, 2);
            return new(name, "第 4 步 / 4 · 当面交接", thread.Outcome == 0 ? Benefit(index) : "研究资料 +1 · 日报告上限提高至 3",
                $"营业时让柜台空出来，邀请{name}来完成交接。回报只结算一次；当面交接后，长期影响就会生效。",
                "邀请到店完成交接", GuidePrimaryKind.Invite, 2);
        }

        string reward = thread.Outcome == 0 ? Benefit(index) : "研究路线完成 · 日报告上限 3";
        if (thread.LastVisitDay != day)
            return new(name, "已完成 · 还能继续来往", reward,
                $"这条路线已经完成，回报已归档。柜台空闲时可以邀请{name}聊聊近况；这次会面不会再次发放任务奖励。",
                "邀请到店聊近况", GuidePrimaryKind.CheckIn, 3);
        return new(name, "已完成 · 今天已经见过面", reward,
            "合作已经落地。继续经营，明天还可以再邀请来聊近况；任务回报不会重复发放。",
            "继续营业", GuidePrimaryKind.ReturnShop, -1);
    }

    public static int RecommendedIndex(StoryBook? book, int activeStory)
    {
        if (TryThread(book, activeStory, out _)) return activeStory;
        for (int i = 0; i < 3; i++)
            if (TryThread(book, i, out var t) && t.Phase is >= 1 and <= 3) return i;
        for (int i = 0; i < 3; i++)
            if (TryThread(book, i, out var t) && t.Phase == 0) return i;
        for (int i = 0; i < 3; i++)
            if (TryThread(book, i, out _)) return i;
        return -1;
    }

    public static string GetSummary(StoryBook? book, int activeStory, int day, ShopActivity activity, int researchPoints) =>
        GetSummary(Build(book, RecommendedIndex(book, activeStory), day, activity, researchPoints));

    public static string GetSummary(GuideCard card)
    {
        string next = card.PrimaryKind switch
        {
            GuidePrimaryKind.Invite or GuidePrimaryKind.CheckIn => card.PrimaryLabel,
            GuidePrimaryKind.Research => "资料已满，先去情报台",
            _ => card.Step.Contains("明天") ? card.Progress + " · 明天继续" : card.Progress
        };
        return card.Heading + " · " + next;
    }

    private static bool HasChoice(StoryBook book, int index, int day, ShopActivity activity, int key)
    {
        // Old in-progress saves can temporarily have null local sets before
        // normal load validation. Do not invent their missing observations.
        var t = book.Threads[index];
        if (t.Phase == 1 && ((index == 1 && t.Route == 1 && t.PublicCustomerIds == null) ||
            (index == 2 && t.Route == 0 && t.PurchasedCategories == null))) return false;
        return StoryRules.Choices(book, index, day, activity).Any(c => c.Key == key);
    }

    private static bool TryThread(StoryBook? book, int index, out StoryThread thread)
    {
        thread = null!;
        if (index is < 0 or > 2 || book?.Threads == null || index >= book.Threads.Length || book.Threads[index] == null) return false;
        thread = book.Threads[index];
        return thread.Phase is >= 0 and <= 4 &&
            (thread.Phase == 0 || thread.Route is 0 or 1) &&
            (thread.Phase < 3 || thread.Outcome is 0 or 1);
    }

    private static ShopActivity Delta(ShopActivity a, ShopActivity baseline) => new(
        Difference(a.Purchases, baseline.Purchases), Difference(a.Sales, baseline.Sales),
        Difference(a.Resales, baseline.Resales), Difference(a.Customers, baseline.Customers), Difference(a.Categories, baseline.Categories));
    private static int Difference(int count, int baseline) => (int)Math.Clamp((long)count - baseline, 0, int.MaxValue);
    private static string Progress(int index, StoryThread t, ShopActivity p) => (index, t.Route) switch
    {
        (0, 0) => $"有成本转售 {Math.Min(2, p.Resales)} / 2",
        (0, 1) => $"柜台销售 {Math.Min(4, p.Sales)} / 4",
        (1, 0) => $"收购 {Math.Min(3, p.Purchases)} / 3 · 销售 {Math.Min(3, p.Sales)} / 3",
        (1, 1) => $"接取后成交顾客群 {Math.Min(2, t.PublicCustomerIds?.Count ?? 0)} / 2 · 销售 {Math.Min(2, p.Sales)} / 2",
        (2, 0) => $"接取后进货品类 {Math.Min(2, t.PurchasedCategories?.Count ?? 0)} / 2 · 销售 {Math.Min(2, p.Sales)} / 2",
        _ => $"柜台销售 {Math.Min(5, p.Sales)} / 5"
    };
    private static string Tip(int index, int route) => (index, route) switch
    {
        (0, 0) => "先正常收购一件货，再把同一件货卖给顾客；货柜里显示有采购成本的货最适合核对。无需为了任务低价出售。",
        (0, 1) => "用现有货物正常完成四笔销售。顾客价格不合适时可以拒绝，不必为了凑数低价甩货。",
        (1, 0) => "正常收购三笔，再销售三笔。可以分几天完成，先留出进货现金，不需要把货免费送给诊所。",
        (1, 1) => "接取后与两个不同顾客群完成有付款的交易，再完成两笔销售。老客也算，同一个顾客群只记一次。",
        (2, 0) => "接取后正常买入两种不同品类，再完成两笔销售。以前买过的品类也算，但要重新收购；供应商可以帮助补齐品类。",
        _ => "沿用现有货源，正常完成五笔销售。可以分几天完成，贷款和股票成交不计入货物周转。"
    };
    private static string Benefit(int index) => index switch
    {
        0 => "长期合作 · 情报研究费用减免 10%",
        1 => "长期合作 · 优先联络费减免 10",
        _ => "长期合作 · 每日免费供应商联络 +1"
    };
    private static string NextDay(int day) => day < int.MaxValue - 1 ? $"第 {day + 1} 天起" : "进入下一天后";
}
