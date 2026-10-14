using System.Text.Json;

namespace SpacePhone;

public enum FollowupPhase { Locked = 0, Offered = 1, Active = 2, AwaitingDebrief = 3, Completed = 4 }

/// <summary>Only this additive follow-up is disabled if its data is contradictory. Old phone assets remain separate.</summary>
public sealed class FollowupBook
{
    public int Version { get; set; } = 1;
    public FollowupPhase Phase { get; set; }
    public bool Disabled { get; set; }
    public int UnlockDay { get; set; } = -1;
    public int ChosenDay { get; set; } = -1;
    public int Route { get; set; } = -1;
    public List<FollowupCall> Calls { get; set; } = new();
    public int DebriefDay { get; set; } = -1;
}

public sealed class FollowupCall
{
    public int Day { get; set; }
    public int Kind { get; set; }
    public int BaseFee { get; set; }
    public int FinalFee { get; set; }
    public string EventKey { get; set; } = "";
    public int Saved => BaseFee - FinalFee;
}

public readonly record struct FollowupChoice(string Label, int Key, string Detail);
public readonly record struct FollowupReply(bool Changed, string Message);
public sealed record FollowupGuide(string Heading, string Step, string Progress, string Instruction,
    string PrimaryLabel, bool CanInvite, int WaitUntilDay);

/// <summary>
/// Read-only quotes and validated, one-way transitions. The native caller must own the current
/// run/book/client and invoke RecordCall only after a supplier was successfully added to the queue.
/// No method gives money, merchandise, research or changes any original story/commission.
/// </summary>
public static class FollowupRules
{
    public const int MaxDay = 100000;
    public const int MaxBaseFee = 20;
    public const int MaxEventLength = 180;
    public static readonly string[] SupplierNames = { "食品", "日用品", "材料", "药品" };

    public static FollowupBook Validate(FollowupBook? book)
    {
        if (book == null) return new FollowupBook(); // A genuinely absent old-save field is a new, locked book.
        if (book.Disabled) return DisabledBook();
        // Never reinterpret contradictory history as an unused new gift.
        return Shape(book) ? book : DisabledBook();
    }

    public static bool Unlock(FollowupBook book, int day, bool commissionCompleted)
    {
        if (!commissionCompleted || !Day(day) || !Shape(book) || book.Phase != FollowupPhase.Locked) return false;
        book.UnlockDay = day; book.Phase = FollowupPhase.Offered;
        return true;
    }

    public static int Limit(FollowupBook book) => book.Route == 0 ? 3 : book.Route == 1 ? 1 : 0;
    public static int Remaining(FollowupBook book) => Shape(book) ? Math.Max(0, Limit(book) - book.Calls.Count) : 0;
    public static int TotalSaved(FollowupBook book) => Shape(book) ? book.Calls.Sum(c => c.Saved) : 0;
    public static int LastCallDay(FollowupBook book) => Shape(book) && book.Calls.Count > 0 ? book.Calls[^1].Day : -1;
    public static bool CanOffer(FollowupBook book, int day) => Shape(book) && Day(day)
        && book.Phase == FollowupPhase.Offered && day > book.UnlockDay;
    public static bool CanDebrief(FollowupBook book, int day) => Shape(book) && Day(day)
        && book.Phase == FollowupPhase.AwaitingDebrief && day > book.Calls[^1].Day;

    public static FollowupChoice[] Choices(FollowupBook book, int day)
    {
        if (CanOffer(book, day)) return new[] {
            new FollowupChoice("给我固定材料线", 0, "三次材料供应商付费联络，各减10，最多减到0；每日免费额度先用，不消耗安排。"),
            new FollowupChoice("留一次跨线补位", 1, "一次任意品类的付费联络免联络费，最多20；已有免费额度时不消耗。"),
            new FollowupChoice("暂时不排，先做我的生意", 2, "没有期限，改天再决定；不改变旧合作或委托。") };
        if (CanDebrief(book, day)) return new[] { new FollowupChoice("收下这次货路记录", 3, "结束这次安排，保留实际使用记录；不再次发放优惠。") };
        return Array.Empty<FollowupChoice>();
    }

    public static FollowupReply Reply(FollowupBook book, int choice, int day)
    {
        if (!Choices(book, day).Any(c => c.Key == choice)) return Reject("这条选择暂不可用，请回手机查看下一步。");
        if (choice == 2) return Reject("先顾你的生意；这次安排没有期限，改天再谈。");
        if (choice == 3)
        {
            book.Phase = FollowupPhase.Completed; book.DebriefDay = day;
            return Changed("货路记录已保存。这次安排结束，旧合作与真实货品交易照常继续。");
        }
        book.Route = choice; book.ChosenDay = day; book.Phase = FollowupPhase.Active;
        return Changed(choice == 0 ? "已选择固定材料线：三次材料供应商付费联络各减10。" : "已留下跨线补位：一次任意供应商付费联络免联络费，最多20。");
    }

    public static int Quote(FollowupBook book, int day, int kind, int baseFee)
    {
        if (!Shape(book) || !Day(day) || !Kind(kind) || baseFee is < 0 or > MaxBaseFee ||
            book.Phase != FollowupPhase.Active || day < book.ChosenDay ||
            (book.Calls.Count > 0 && day < book.Calls[^1].Day) || baseFee == 0) return baseFee;
        return book.Route == 0 && kind == 2 ? Math.Max(0, baseFee - 10)
            : book.Route == 1 ? 0 : baseFee;
    }

    /// <param name="agreedFeeCap">Optional native caller's already-approved quote; -1 means no extra cap check.</param>
    public static FollowupReply RecordCall(FollowupBook book, int day, int kind, int baseFee,
        int finalFee, string eventKey, int agreedFeeCap = -1)
    {
        if (!Shape(book) || !Day(day) || !Kind(kind) || book.Phase != FollowupPhase.Active ||
            baseFee is <= 0 or > MaxBaseFee || finalFee < 0 || agreedFeeCap < -1 ||
            (agreedFeeCap >= 0 && finalFee > agreedFeeCap) || !Token(eventKey) ||
            day < book.ChosenDay || (book.Calls.Count > 0 && day < book.Calls[^1].Day) ||
            book.Calls.Any(c => c.EventKey == eventKey)) return Reject("本次联络未消耗货路安排。");
        int expected = Quote(book, day, kind, baseFee);
        if (expected != finalFee || expected >= baseFee) return Reject("联络报价与货路安排不一致，未记录使用。");
        book.Calls.Add(new FollowupCall { Day = day, Kind = kind, BaseFee = baseFee, FinalFee = finalFee, EventKey = eventKey });
        if (book.Calls.Count == Limit(book)) book.Phase = FollowupPhase.AwaitingDebrief;
        return Changed($"洛克货路安排已使用，实际减免 {baseFee-finalFee}；剩余 {Remaining(book)} 次。" +
            (book.Phase == FollowupPhase.AwaitingDebrief ? " 次日可邀请洛克当面复盘。" : ""));
    }

    public static string OfferText(FollowupBook book, int day)
    {
        if (!Shape(book)) return "这份货路记录暂不可用；旧任务、货物与资金仍照常。";
        if (!CanOffer(book, day)) return GetOutcomeText(book);
        return "洛克把维修回执放在柜台上。\n“上回你送来的电子件，我交给维修班了。货梯能撑多久，谁也不敢拍胸口。现在我能替你留一条材料线，或者留一次跨线补位。班次有限，得先说好。”\n\n两项只选一种；没有期限，不发现金，也不改变旧合作。";
    }

    public static string GetOutcomeText(FollowupBook book)
    {
        if (!Shape(book)) return "这份货路记录暂不可用；旧任务、货物与资金仍照常。";
        if (book.Phase == FollowupPhase.Locked) return "先完成洛克的电子元件实物委托。原版柜台的真实交付完成后，次日再来聊货路。";
        if (book.Phase == FollowupPhase.Offered) return "维修回执到了。洛克有一条固定材料线，或一次任意品类补位可以安排；你准备好时再决定。";
        string route = book.Route == 0 ? "固定材料线" : "跨线补位";
        if (book.Phase == FollowupPhase.Active) return $"已选择{route}，剩余 {Remaining(book)} 次。每日免费额度先用；只有需要付费且符合品类的真实联络才消耗。没有期限，先照顾现有顾客。";
        string kinds = string.Join("、", book.Calls.Select(c => SupplierNames[c.Kind]).Distinct());
        string memory = book.Route == 0 ? "材料线实际安排了三次联络。" : $"你把那一次补位用在了{kinds}这条线。";
        return "洛克翻开这次货路记录。\n“" + memory + "新货源不会替你决定买什么；留住现金和选择，比多喊一班重要。”\n" +
            $"本次实际减免 {TotalSaved(book)}，已使用 {book.Calls.Count}/{Limit(book)} 次。" +
            (book.Phase == FollowupPhase.Completed ? "记录已经收下，不再发放新的安排。" : "次日当面收下记录，这次安排就结束；没有额外现金或资料奖励。");
    }

    public static FollowupGuide GetGuide(FollowupBook book, int day)
    {
        const string title = "洛克 · 货路还要有人跑";
        if (!Shape(book) || !Day(day)) return new(title, "新回访记录暂不可用", "旧玩法继续保留", "这份新记录无法核对，暂不提供新优惠。原有故事、委托、资金与货物仍照常。", "回柜台继续营业", false, -1);
        if (book.Phase == FollowupPhase.Locked) return new(title, "先完成电子元件委托", "原生柜台实际付款交货", GetOutcomeText(book), "查看洛克实物委托", false, -1);
        if (book.Phase == FollowupPhase.Offered)
        {
            bool ready = CanOffer(book, day); int wait = NextDay(book.UnlockDay);
            return new(title, ready ? "当面选择货路安排" : "明天再聊货路", "固定材料线 / 一次跨线补位", ready ? "柜台空闲时邀请洛克。选择前能看到品类、次数和费用影响；也可以改天再决定。" : WaitText(wait, "维修回执") , ready ? "邀请洛克谈货路" : "继续营业，明天再联系", ready, wait);
        }
        if (book.Phase == FollowupPhase.Active) return new(title, "合作安排保留中", $"{(book.Route==0?"固定材料线":"跨线补位")} · 余 {Remaining(book)} 次", $"已留好{(book.Route==0?"材料线":"跨线补位")}，余 {Remaining(book)} 次。需要补货时再用，不必额外叫货。免费额度优先，付费联络才消耗；安排没有期限。", book.Route == 0 ? "查看材料货源" : "查看供应商", false, -1);
        if (book.Phase == FollowupPhase.AwaitingDebrief)
        {
            bool ready = CanDebrief(book, day); int wait = NextDay(book.Calls[^1].Day);
            return new(title, ready ? "当面复盘这次货路" : "明天复盘", $"安排已用完 · 实际减免 {TotalSaved(book)}", ready ? "邀请洛克到柜台，收下这次实际使用记录。结束不会再次发券，也不会发现金或资料。" : WaitText(wait, "货路复盘") , ready ? "邀请洛克当面复盘" : "继续营业，明天复盘", ready, wait);
        }
        return new(title, "这次安排已结束", $"实际减免 {TotalSaved(book)} · 使用 {book.Calls.Count}/{Limit(book)}", GetOutcomeText(book), "继续营业", false, -1);
    }

    public static string Encode(FollowupBook book) => JsonSerializer.Serialize(Validate(book));
    public static FollowupBook Decode(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return DisabledBook();
        try { var book = JsonSerializer.Deserialize<FollowupBook>(json); return book == null ? DisabledBook() : Validate(book); }
        catch (JsonException) { return DisabledBook(); }
    }

    private static FollowupBook DisabledBook() => new() { Disabled = true };
    private static bool Shape(FollowupBook? b)
    {
        if (b == null || b.Disabled || b.Version != 1 || b.Calls == null || (int)b.Phase is < 0 or > 4) return false;
        if (b.Phase == FollowupPhase.Locked) return b.UnlockDay == -1 && b.ChosenDay == -1 && b.Route == -1 && b.Calls.Count == 0 && b.DebriefDay == -1;
        if (!Day(b.UnlockDay)) return false;
        if (b.Phase == FollowupPhase.Offered) return b.ChosenDay == -1 && b.Route == -1 && b.Calls.Count == 0 && b.DebriefDay == -1;
        if (!Day(b.ChosenDay) || b.ChosenDay <= b.UnlockDay || b.Route is < 0 or > 1 || b.Calls.Count > Limit(b)) return false;
        int previousDay = b.ChosenDay;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in b.Calls)
        {
            if (c == null || !Day(c.Day) || c.Day < previousDay || !Kind(c.Kind) || c.BaseFee is <= 0 or > MaxBaseFee ||
                !Token(c.EventKey) || !keys.Add(c.EventKey) ||
                (b.Route == 0 && (c.Kind != 2 || c.FinalFee != Math.Max(0, c.BaseFee-10))) ||
                (b.Route == 1 && c.FinalFee != 0)) return false;
            previousDay = c.Day;
        }
        return b.Phase switch {
            FollowupPhase.Active => b.Calls.Count < Limit(b) && b.DebriefDay == -1,
            FollowupPhase.AwaitingDebrief => b.Calls.Count == Limit(b) && b.DebriefDay == -1,
            FollowupPhase.Completed => b.Calls.Count == Limit(b) && Day(b.DebriefDay) && b.DebriefDay > previousDay,
            _ => false };
    }
    private static bool Day(int day) => day is >= 0 and <= MaxDay;
    private static bool Kind(int kind) => kind is >= 0 and <= 3;
    private static bool Token(string? key) => !string.IsNullOrWhiteSpace(key) && key.Length <= MaxEventLength && key == key.Trim() && !key.Any(char.IsControl);
    private static int NextDay(int day) => day < MaxDay ? day + 1 : -1;
    private static string WaitText(int day, string subject) => day < 0 ? "已到游戏日期上限，没有有效的次日；不强行推进或重发优惠。" : $"{subject}最早 D{day} 可继续。当前顾客照常交易，没有新的倒计时或罚金。";
    private static FollowupReply Changed(string text) => new(true, text);
    private static FollowupReply Reject(string text) => new(false, text);
}
