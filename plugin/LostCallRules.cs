using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpacePhone;

public enum LostCallPhase { Locked, Offered, AwaitingIntroduction, AwaitingAcquisition, Acquired, Delivered, Closed, Declined }
public enum LostCallRoute { None = -1, Profit = 0, Human = 1, Information = 2 }
public enum LostCallDisposition { None, Owned, Delivered, SoldElsewhere, Destroyed }
public enum LostCallVisitKind { None, Introduction, Acquisition, Delivery, Debrief, Memory }
public enum LostCallOwnership { Unknown, Owned, NotObserved }
public enum LostCallAction { ContinueTrading, ReviewEntry, ReturnToCounter, CheckInventory, ChooseRoute, InviteIntroduction, InviteAcquisition, InviteDelivery, InviteDebrief, ViewReceipt }

// This chapter owns no old phone arrays, wallet, item factory or native client pointer.
public sealed class LostCallBook
{
    public int Version { get; set; } = 1;
    public bool Disabled { get; set; }
    public LostCallPhase Phase { get; set; }
    public int OfferedDay { get; set; } = -1;
    public int InvitedDay { get; set; } = -1;
    public int IntroductionDay { get; set; } = -1;
    public int PurchaseDay { get; set; } = -1;
    public int DispositionDay { get; set; } = -1;
    public int ClosedDay { get; set; } = -1;
    public int ItemDefinitionVersion { get; set; } = 1;
    public int AcquiredActualUniqueId { get; set; }
    public int ActualPurchaseAmount { get; set; }
    public int ActualSaleAmount { get; set; }
    public string NativePurchaseToken { get; set; } = "";
    public string NativeDeliveryToken { get; set; } = "";
    public string NativeDisposalToken { get; set; } = "";
    public LostCallRoute ProposedRoute { get; set; } = LostCallRoute.None;
    public int ProposalRevision { get; set; }
    public LostCallRoute CommittedRoute { get; set; } = LostCallRoute.None;
    public LostCallDisposition ItemDisposition { get; set; }
    public int LastVisitDay { get; set; } = -1;
    public LostCallVisitKind LastVisitKind { get; set; }
    public string LastVisitToken { get; set; } = "";
    public bool LastVisitFinished { get; set; } = true;
    public LostCallRoute LastVisitRoute { get; set; } = LostCallRoute.None;
    public int LastVisitProposalRevision { get; set; }
    public string LastIntroductionAnswerToken { get; set; } = "";
    public List<string> RecentVisitTokens { get; set; } = new();
    public bool OriginalVoiceUnlocked { get; set; }
    public string RewardReceiptId { get; set; } = "";
    public int PendingResearch { get; set; }
    public bool RewardSettled { get; set; }
}

public readonly record struct LostCallReply(bool Changed, string Message);
public readonly record struct LostCallChoice(int Key, string Label, string Detail, bool ChargesMoney, int ProposedBuyerBudget);
public readonly record struct LostCallVisitPlan(LostCallVisitKind Kind, LostCallRoute Route, int ProposalRevision,
    string RequiredItemId, int RequiredActualUniqueId, int SuggestedSpendCeiling, int ProposedBuyerBudget, int EarliestDay);
// Both booleans are evidence supplied by a current-run native transaction observer, never by a button or inventory scan.
public readonly record struct LostCallTradeEvidence(int Day, int ActualUniqueId, string ItemId, int Units, int ActualAmount,
    string NativeToken, bool PaymentConfirmed, bool OwnershipTransferred);
public readonly record struct LostCallResearchReward(string ReceiptId, int Points);
public sealed record LostCallGuide(string Heading, string Step, string Progress, string Instruction,
    string PrimaryLabel, bool CanInvite, int WaitUntilDay, bool ChargesMoney, int SuggestedSpendCeiling, int ProposedBuyerBudget)
{
    public string Title => Heading;
    public LostCallAction PrimaryAction { get; init; } = LostCallAction.ContinueTrading;
}

public static class LostCallRules
{
    public const string ItemId = "spacephone.item.route_recorder";
    public const string ContactId = "spacephone.lostcall.yudeng";
    public const int MaxDay = 100000;
    public const int MaxAmount = 1000000000;
    public const int MaxTokenLength = 180;
    public const int SuggestedPurchaseBudget = 70;
    public const int ReferencePurchasePrice = 40;
    private const int MaxRecentVisits = 64;
    public const int InviteChoice = 10, LaterChoice = 11, DeclineChoice = 12;
    public const int InspectChoice = 20, PauseChoice = 21, CloseOfferChoice = 22;
    public const int ProfitChoice = 30, HumanChoice = 31, InformationChoice = 32, PauseRouteChoice = 33;
    public const int ArchiveChoice = 40, AskMemoryChoice = 41;

    public static LostCallBook Validate(LostCallBook? book) => book == null ? new LostCallBook()
        : Shape(book) ? book : DisabledBook();
    public static LostCallBook DisabledBook() => new() { Disabled = true };
    public static bool IsValid(LostCallBook? book) => Shape(book);
    public static string Encode(LostCallBook book) => JsonSerializer.Serialize(Validate(book));
    public static LostCallBook Decode(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 65536) return DisabledBook();
        try { return JsonSerializer.Deserialize<LostCallBook>(json) is { } book ? Validate(book) : DisabledBook(); }
        catch (JsonException) { return DisabledBook(); }
        catch (NotSupportedException) { return DisabledBook(); }
    }

    // nativeContentReady must include the custom definition, UID persistence and original sprite gates.
    public static LostCallReply Offer(LostCallBook book, int day, int actualPaidTradeCount, bool nativeContentReady)
    {
        if (!nativeContentReady || !Day(day) || day < 2 || actualPaidTradeCount < 2 || !Shape(book)
            || book.Phase != LostCallPhase.Locked) return No("来电尚未登记；原有拨号和柜台照常。");
        book.OfferedDay = day; book.Phase = LostCallPhase.Offered;
        return Yes("陌生来电已保留。可以先忙柜台，没有期限。");
    }

    public static LostCallChoice[] EntryChoices(LostCallBook book) => Shape(book) && book.Phase == LostCallPhase.Offered
        ? new[] { Choice(InviteChoice, "记下号码，约他来柜台", "只约见，不扣款，不增加或移走货物。"),
            Choice(LaterChoice, "先做生意，回头再看", "来电记录保留，没有期限。"),
            Choice(DeclineChoice, "这次不接陌生委托", "归档来电，没有罚金，不影响原版电话剧情。") } : Array.Empty<LostCallChoice>();

    public static LostCallReply ReplyEntry(LostCallBook book, int choice, int day)
    {
        if (!Shape(book) || !Day(day) || day < book.OfferedDay || !EntryChoices(book).Any(c => c.Key == choice)) return No("这条来电选择暂不可用。");
        if (choice == LaterChoice) return No("来电留着。先顾柜台，想聊时再看。");
        if (choice == DeclineChoice) { Decline(book, day); return Yes("来电已归档，没有扣款或惩罚。"); }
        book.InvitedDay = day; book.Phase = LostCallPhase.AwaitingIntroduction;
        return Yes("已记下余灯的号码。柜台空闲时可以约他来核对处置单。");
    }

    // A plan is only a configuration preview. Obtaining one must not create a client or charge money.
    public static LostCallVisitPlan? PlanVisit(LostCallBook book, int day)
    {
        if (!Shape(book) || !Day(day) || !book.LastVisitFinished || day <= book.LastVisitDay) return null;
        LostCallVisitKind kind; int earliest; int uid = 0, budget = 0, ceiling = 0;
        LostCallRoute route = LostCallRoute.None;
        switch (book.Phase)
        {
            case LostCallPhase.AwaitingIntroduction: kind = LostCallVisitKind.Introduction; earliest = book.InvitedDay; break;
            case LostCallPhase.AwaitingAcquisition: kind = LostCallVisitKind.Acquisition; earliest = NextDay(book.IntroductionDay); ceiling = SuggestedPurchaseBudget; break;
            case LostCallPhase.Acquired:
                if (!Route(book.ProposedRoute)) return null;
                kind = LostCallVisitKind.Delivery; earliest = NextDay(book.PurchaseDay); route = book.ProposedRoute;
                uid = book.AcquiredActualUniqueId; budget = ProposedBuyerBudget(route); break;
            case LostCallPhase.Delivered: kind = LostCallVisitKind.Debrief; earliest = NextDay(book.DispositionDay); break;
            case LostCallPhase.Closed: kind = LostCallVisitKind.Memory; earliest = NextDay(book.ClosedDay); break;
            default: return null;
        }
        if (earliest < 0 || day < earliest) return null;
        return new(kind, route, book.ProposalRevision, kind is LostCallVisitKind.Acquisition or LostCallVisitKind.Delivery ? ItemId : "",
            uid, ceiling, budget, earliest);
    }

    // Call after the exact planned native visitor reaches the counter, not when merely queued.
    public static LostCallReply ObserveVisit(LostCallBook book, LostCallVisitPlan plan, int day, string nativeVisitToken)
    {
        if (PlanVisit(book, day) is not { } expected || expected != plan || !Token(nativeVisitToken)
            || book.RecentVisitTokens.Contains(nativeVisitToken) || NativeTokens(book).Contains(nativeVisitToken)) return No("未登记重复或已变化的来访。");
        book.LastVisitDay = day; book.LastVisitKind = plan.Kind; book.LastVisitToken = nativeVisitToken;
        book.LastVisitRoute = plan.Route; book.LastVisitProposalRevision = plan.ProposalRevision; book.LastVisitFinished = false;
        book.RecentVisitTokens.Add(nativeVisitToken);
        if (book.RecentVisitTokens.Count > MaxRecentVisits) book.RecentVisitTokens.RemoveAt(0);
        return Yes("余灯已到柜台。本次谈话或交易按原生流程进行。");
    }

    // On load, only use this after the current-run integration proves this visit has no live native client.
    public static LostCallReply FinishVisit(LostCallBook book, string nativeVisitToken)
    {
        if (!Shape(book) || book.LastVisitFinished || book.LastVisitToken != nativeVisitToken || !Token(nativeVisitToken)) return No("未结束其他来访。");
        book.LastVisitFinished = true;
        return Yes("这次来访已结束。未成交的实物和选择照常保留。");
    }

    public static LostCallChoice[] IntroductionChoices(LostCallBook book, int day, string nativeVisitToken)
        => AtVisit(book, LostCallVisitKind.Introduction, day, nativeVisitToken) && book.Phase == LostCallPhase.AwaitingIntroduction
            && book.LastIntroductionAnswerToken != nativeVisitToken
            ? new[] { Choice(InspectChoice, "先看看东西和预算", "次日起再约看货；参考40，建议预算70，最终按原生报价决定。"),
                Choice(PauseChoice, "等我腾出现金再谈", "保留来电，没有期限，不锁库存。"),
                Choice(CloseOfferChoice, "我这次不接", "结束这条可选委托，没有扣款或罚金。") } : Array.Empty<LostCallChoice>();

    public static LostCallReply ReplyIntroduction(LostCallBook book, int choice, int day, string nativeVisitToken)
    {
        if (!IntroductionChoices(book, day, nativeVisitToken).Any(c => c.Key == choice)) return No("这句回复已变化或已经确认。");
        book.IntroductionDay = day; book.LastIntroductionAnswerToken = nativeVisitToken;
        if (choice == CloseOfferChoice) { Decline(book, day); return Yes("处置单收好。这次不接委托，没有罚金。"); }
        if (choice == PauseChoice) return Yes("等现金宽裕再谈。来电保留，没有期限。");
        book.Phase = LostCallPhase.AwaitingAcquisition;
        return Yes("次日起可再约余灯带来录音匣。只在柜台真实收购后记录成本。");
    }

    public static LostCallReply RecordPurchase(LostCallBook book, string nativeVisitToken, LostCallTradeEvidence trade)
    {
        if (!Evidence(trade) || trade.ActualAmount <= 0 || !AtVisit(book, LostCallVisitKind.Acquisition, trade.Day, nativeVisitToken)
            || book.Phase != LostCallPhase.AwaitingAcquisition || trade.Day <= book.IntroductionDay || NativeTokens(book).Contains(trade.NativeToken)) return No("未登记未完成、重复或不属于这只录音匣的收购。");
        book.AcquiredActualUniqueId = trade.ActualUniqueId; book.ActualPurchaseAmount = trade.ActualAmount;
        book.PurchaseDay = trade.Day; book.NativePurchaseToken = trade.NativeToken;
        book.ItemDisposition = LostCallDisposition.Owned; book.Phase = LostCallPhase.Acquired;
        return Yes("收购已按原生成交登记。物品仍归你；先看成本，再选择去向。");
    }

    public static LostCallChoice[] RouteChoices(LostCallBook book) => Shape(book) && book.Phase == LostCallPhase.Acquired
        ? new[] { Choice(ProfitChoice, "卖给夜航收藏人", "拟定买方预算140；实际成交价由原生议价决定，只收成交款。", 140),
            Choice(HumanChoice, "让它回到公共听音台", "拟定预算80；成交后留下原创报平安记忆，无经营加成。", 80),
            Choice(InformationChoice, "交给维修站换一页研究", "拟定预算80；成交及收尾后一次1点研究，容量满则待入账。", 80),
            Choice(PauseRouteChoice, "再核对一次成本", "物品仍归你，没有扣款、截止日或罚金。") } : Array.Empty<LostCallChoice>();

    public static LostCallReply SelectRoute(LostCallBook book, int choice)
    {
        if (!Shape(book) || !RouteChoices(book).Any(c => c.Key == choice)) return No("现在不能更改这只录音匣的去向。");
        if (choice == PauseRouteChoice) return No("先核对采购记录。没有期限，预选不会移走实物。");
        if (!book.LastVisitFinished && book.LastVisitKind == LostCallVisitKind.Delivery) return No("本次收货人已在场；正常结束来访后再改去向。");
        LostCallRoute route = (LostCallRoute)(choice - ProfitChoice);
        if (route == book.ProposedRoute) return No("这个去向已预选，尚未成交。");
        if (book.ProposalRevision == int.MaxValue) return No("预选记录已到上限，先保留现有去向。");
        book.ProposedRoute = route; book.ProposalRevision++;
        return Yes("去向仅作预选。实物仍在库存，实际成交后才确定去向。");
    }

    public static LostCallReply RecordDelivery(LostCallBook book, string nativeVisitToken, LostCallVisitPlan nativeBuyerPlan, LostCallTradeEvidence trade)
    {
        if (!Evidence(trade) || trade.ActualAmount <= 0 || !AtVisit(book, LostCallVisitKind.Delivery, trade.Day, nativeVisitToken)
            || book.Phase != LostCallPhase.Acquired || trade.ActualUniqueId != book.AcquiredActualUniqueId
            || trade.Day <= book.PurchaseDay || NativeTokens(book).Contains(trade.NativeToken)
            || nativeBuyerPlan.Kind != LostCallVisitKind.Delivery || nativeBuyerPlan.Route != book.LastVisitRoute
            || nativeBuyerPlan.Route != book.ProposedRoute || !Route(nativeBuyerPlan.Route)
            || nativeBuyerPlan.ProposalRevision != book.LastVisitProposalRevision || nativeBuyerPlan.ProposalRevision != book.ProposalRevision
            || nativeBuyerPlan.RequiredActualUniqueId != book.AcquiredActualUniqueId || nativeBuyerPlan.RequiredItemId != ItemId
            || nativeBuyerPlan.ProposedBuyerBudget != ProposedBuyerBudget(nativeBuyerPlan.Route)
            || nativeBuyerPlan.SuggestedSpendCeiling != 0 || nativeBuyerPlan.EarliestDay != NextDay(book.PurchaseDay)) return No("交付未通过本次买方、同一实物和实际成交核对。");
        book.ActualSaleAmount = trade.ActualAmount; book.NativeDeliveryToken = trade.NativeToken;
        book.CommittedRoute = nativeBuyerPlan.Route; book.ItemDisposition = LostCallDisposition.Delivered;
        book.DispositionDay = trade.Day; book.Phase = LostCallPhase.Delivered;
        return Yes("同一只录音匣已真实成交并移交，去向已锁定。次日闲时听余灯说结果。");
    }

    // A confirmed ordinary sale can close the episode softly. Missing inventory is never a sale event.
    public static LostCallReply RecordSoldElsewhere(LostCallBook book, LostCallTradeEvidence trade)
    {
        if (!Shape(book) || book.Phase != LostCallPhase.Acquired || !Evidence(trade)
            || trade.ActualUniqueId != book.AcquiredActualUniqueId || trade.Day < book.PurchaseDay
            || NativeTokens(book).Contains(trade.NativeToken)) return No("未登记不确定或重复的自行出售。");
        book.ActualSaleAmount = trade.ActualAmount; book.NativeDeliveryToken = trade.NativeToken;
        book.ItemDisposition = LostCallDisposition.SoldElsewhere; book.DispositionDay = trade.Day;
        book.Phase = LostCallPhase.Delivered;
        return Yes("已记录自行出售。没有罚金，不再索要实物，也不发三路线额外回报。");
    }

    public static LostCallReply RecordConfirmedDestroy(LostCallBook book, int day, int actualUniqueId, string itemId, string nativeToken, bool destructionConfirmed)
    {
        if (!destructionConfirmed || !Shape(book) || book.Phase != LostCallPhase.Acquired || !Day(day)
            || day < book.PurchaseDay || actualUniqueId != book.AcquiredActualUniqueId || itemId != ItemId
            || !Token(nativeToken) || NativeTokens(book).Contains(nativeToken)) return No("实物暂未找到；先检查货柜和容器，不把扫描缺席当成销毁。");
        book.NativeDisposalToken = nativeToken; book.ItemDisposition = LostCallDisposition.Destroyed;
        book.DispositionDay = day; book.Phase = LostCallPhase.Delivered;
        return Yes("已核对销毁事件。可以软收尾，没有罚金，也不自动补货。");
    }

    public static LostCallChoice[] DebriefChoices(LostCallBook book, int day, string nativeVisitToken)
        => AtVisit(book, LostCallVisitKind.Debrief, day, nativeVisitToken) && book.Phase == LostCallPhase.Delivered
            ? new[] { Choice(ArchiveChoice, "收好回执，继续营业", "保存真实收付和去向；新增回报如有，仅一次。"),
                Choice(AskMemoryChoice, "后来还顺利吗", "听对应去向近况，不结算、不改变路线。") } : Array.Empty<LostCallChoice>();

    public static LostCallReply ReplyDebrief(LostCallBook book, int choice, int day, string nativeVisitToken)
    {
        if (!DebriefChoices(book, day, nativeVisitToken).Any(c => c.Key == choice)) return No("这次回执已变化或已经收好。");
        if (choice == AskMemoryChoice) return No(OutcomeText(book));
        return Archive(book, day);
    }

    // Optional explicit phone-receipt archive fallback; this mutates the ending and must not run merely when viewing it.
    // Integration may keep it unavailable to preserve the fourth native visit. The next-day boundary still applies.
    public static LostCallReply ArchivePhoneReceipt(LostCallBook book, int day)
    {
        if (!Shape(book) || book.Phase != LostCallPhase.Delivered || !Day(day) || day <= book.DispositionDay
            || !book.LastVisitFinished) return No("回执还没有到，或本次来访尚未结束。");
        return Archive(book, day);
    }

    private static LostCallReply Archive(LostCallBook book, int day)
    {
        book.Phase = LostCallPhase.Closed; book.ClosedDay = day;
        book.OriginalVoiceUnlocked = book.ItemDisposition == LostCallDisposition.Delivered && book.CommittedRoute == LostCallRoute.Human;
        bool research = book.ItemDisposition == LostCallDisposition.Delivered && book.CommittedRoute == LostCallRoute.Information;
        book.PendingResearch = research ? 1 : 0; book.RewardSettled = !research;
        book.RewardReceiptId = research ? Receipt(book.NativeDeliveryToken, book.AcquiredActualUniqueId) : "";
        return Yes(research ? "回执已保存。一页研究待入账；容量满时保留，之后只补入这一次。" : "回执已保存，真实收付和去向留下了。这次故事结束，没有新增现金。");
    }

    public static LostCallResearchReward? PendingResearchReward(LostCallBook book) => Shape(book) && book.Phase == LostCallPhase.Closed
        && book.PendingResearch == 1 && !book.RewardSettled ? new(book.RewardReceiptId, 1) : null;

    // Call only after BusinessRules.StoreResearchReward accepted exactly this 1 point in the same synchronous state write.
    public static LostCallReply MarkResearchStored(LostCallBook book, string receiptId, int acceptedPoints)
    {
        if (acceptedPoints != 1 || PendingResearchReward(book) is not { } pending || receiptId != pending.ReceiptId) return No("研究尚未入账；不创建第二份回报。");
        book.PendingResearch = 0; book.RewardSettled = true;
        return Yes("这次唯一的一页研究已入账，回访或读信不会再次发放。");
    }

    public static int ProposedBuyerBudget(LostCallRoute route) => route == LostCallRoute.Profit ? 140 : route is LostCallRoute.Human or LostCallRoute.Information ? 80 : 0;
    public static long KnownTradeDifference(LostCallBook book) => Shape(book) && book.ItemDisposition is LostCallDisposition.Delivered or LostCallDisposition.SoldElsewhere
        ? (long)book.ActualSaleAmount - book.ActualPurchaseAmount : 0;
    public static string RouteName(LostCallRoute route) => route switch { LostCallRoute.Profit => "夜航收藏人", LostCallRoute.Human => "公共听音台", LostCallRoute.Information => "维修站", _ => "尚未预选" };

    public static string OutcomeText(LostCallBook book)
    {
        if (!Shape(book)) return "这条新记录待核对；原版电话、旧任务、钱和货物照常。";
        string receipt = book.AcquiredActualUniqueId > 0 ? "实际收购支出 " + book.ActualPurchaseAmount + "。" : "尚未收购，没有支出。";
        if (book.ItemDisposition is LostCallDisposition.Delivered or LostCallDisposition.SoldElsewhere)
            receipt += "实际售出收入 " + book.ActualSaleAmount + "，已知买卖差额 " + KnownTradeDifference(book) + "。";
        return book.ItemDisposition switch {
            LostCallDisposition.SoldElsewhere => "余灯说：它是你付钱收来的货，自行出售也是一种决定。三张回执不再使用，没有额外回报。" + receipt,
            LostCallDisposition.Destroyed => "录音匣已确认销毁。不拿别的货抵、不补一只、不罚钱；这次记录照实收好。" + receipt,
            LostCallDisposition.Delivered when book.CommittedRoute == LostCallRoute.Profit => "收藏人听完播报放下了放大镜。这是一笔按柜台成交的生意，故事不会替你编利润。" + receipt,
            LostCallDisposition.Delivered when book.CommittedRoute == LostCallRoute.Human => "公共听音台已收到录音匣。林芦说：灯没灭，放心。原创报平安片段在收好回执后留下，不附经营加成。" + receipt,
            LostCallDisposition.Delivered when book.CommittedRoute == LostCallRoute.Information => "维修站留下匿名周转记录。一页研究在收好回执后只归档一次，不保证股价或货价。" + receipt,
            _ => receipt };
    }

    public static LostCallGuide GetGuide(LostCallBook book, int day, LostCallOwnership ownership = LostCallOwnership.Unknown)
    {
        const string title = "余灯 · 失物来电";
        if (!Shape(book) || !Day(day)) return Guide("来电记录待核对", "店里的生意照常", "这通来电的记录暂时对不上，先照常营业。", "继续营业");
        if (book.Phase == LostCallPhase.Locked) return Guide("先做两笔柜台生意", "第二个营业日起可留意来电", "两笔实际付款的柜台生意可能带来一通陌生来电。没有期限，先顾自己的生意。", "继续营业");
        if (book.Phase == LostCallPhase.Offered) return Guide("陌生来电留着", "没有期限", "选择只约见、以后看或婉拒，均不扣款，也不接管原版拨号。", "查看来电");
        if (book.Phase == LostCallPhase.Declined) return Guide("来电已归档", "没有罚金", "这次没有接委托。原版名片电话和柜台生意照常。", "继续营业");
        if (!book.LastVisitFinished) return Guide("余灯在柜台", "先完成本次来访", "原生谈话和议价照常。正常结束后再预约；拒价不扣额外费用。", "回柜台");
        if (book.Phase == LostCallPhase.Acquired && ownership == LostCallOwnership.NotObserved)
            return Guide("先找这只录音匣", "已知收购支出 " + book.ActualPurchaseAmount, "检查货柜、容器或真实售出记录；暂时扫描不到，不算遗失，不会偷偷补货。", "查看货柜");
        if (book.Phase == LostCallPhase.Acquired && !Route(book.ProposedRoute))
            return Guide("为录音匣选择去向", "实际收购支出 " + book.ActualPurchaseAmount, "收藏人拟定预算140，另外两处80。预选不转移库存、不付钱；实际报价仍按柜台。", "选择去向");
        var plan = PlanVisit(book, day);
        bool ready = plan != null; int wait = book.Phase switch {
            LostCallPhase.AwaitingIntroduction => book.InvitedDay,
            LostCallPhase.AwaitingAcquisition => NextDay(book.IntroductionDay),
            LostCallPhase.Acquired => NextDay(book.PurchaseDay),
            LostCallPhase.Delivered => NextDay(book.DispositionDay),
            LostCallPhase.Closed => NextDay(book.ClosedDay), _ => -1 };
        if (book.LastVisitDay >= wait) wait = NextDay(book.LastVisitDay);
        string waitText = wait < 0 ? "已到有效日期上限，记录保留，不强行推进。" : "最早 D" + wait + " 再约，没有期限或罚金。";
        return book.Phase switch {
            LostCallPhase.AwaitingIntroduction => Guide(ready ? "先核对处置单" : "闲时再约", "首次见面不做交易", ready ? "柜台空闲时约余灯来讲来历；先看授权再决定是否看货。" : waitText, ready ? "邀请余灯核对处置单" : "继续营业", ready, wait),
            LostCallPhase.AwaitingAcquisition => Guide(ready ? "先看货和实际报价" : "明天起可看货", "参考40 · 手机建议预算70", ready ? "在柜台决定是否收购；手机不提前扣款，70是建议，不是硬上限。" : waitText, ready ? "邀请余灯带来录音匣" : "继续营业", ready, wait, 70),
            LostCallPhase.Acquired => Guide(ready ? "按预选去向正常卖出" : "明天起可约收货", "预选：" + RouteName(book.ProposedRoute) + " · 成本 " + book.ActualPurchaseAmount, ready ? "带同一只录音匣到原生议价台。预算不是承诺售价；未成交离店后能改预选。" : waitText, ready ? "邀请余灯来收货" : "继续营业", ready, wait, 0, ProposedBuyerBudget(book.ProposedRoute)),
            LostCallPhase.Delivered => Guide(ready ? "闲时听回执结果" : "回执还在路上", "去向与真实收付已记录", ready ? OutcomeText(book) : waitText, ready ? "邀请余灯讲结果" : "继续营业", ready, wait),
            LostCallPhase.Closed => Guide("回执已经收好", book.PendingResearch == 1 ? "一页研究待入账" : "故事已完成", OutcomeText(book) + (book.PendingResearch == 1 ? "研究容量腾出后补入这一次，不改每日额度。" : "想聊时再约，不重复结算。"), "查看回执", ready, wait),
            _ => Guide("继续营业", "记录保留", "没有期限。", "继续营业") };

        LostCallGuide Guide(string step, string progress, string instruction, string primary, bool canInvite = false, int until = -1, int ceiling = 0, int budget = 0)
            => new(title, step, progress, instruction, primary, canInvite, until, false, ceiling, budget)
                { PrimaryAction = PrimaryActionFor(book, day, ownership) };
    }

    public static LostCallGuide GetGuide(LostCallBook book, int day, bool nativeItemReady, bool itemOwned)
    {
        if (!nativeItemReady && Shape(book) && Day(day) && book.Phase is not LostCallPhase.Closed and not LostCallPhase.Declined)
            return new("余灯 · 失物来电", "余灯资料同步中", "店里的生意照常", "余灯正在核对资料。来电留着，准备好时再从这里联系；先照常营业。", "继续营业", false, -1, false, 0, 0);
        return GetGuide(book, day, itemOwned ? LostCallOwnership.Owned : LostCallOwnership.NotObserved);
    }

    private static LostCallAction PrimaryActionFor(LostCallBook book, int day, LostCallOwnership ownership)
    {
        if (!Shape(book) || !Day(day)) return LostCallAction.ContinueTrading;
        if (book.Phase == LostCallPhase.Offered) return LostCallAction.ReviewEntry;
        if (!book.LastVisitFinished) return LostCallAction.ReturnToCounter;
        if (book.Phase == LostCallPhase.Acquired && ownership == LostCallOwnership.NotObserved) return LostCallAction.CheckInventory;
        if (book.Phase == LostCallPhase.Acquired && !Route(book.ProposedRoute)) return LostCallAction.ChooseRoute;
        if (book.Phase == LostCallPhase.Closed) return LostCallAction.ViewReceipt;
        if (PlanVisit(book, day) == null) return LostCallAction.ContinueTrading;
        return book.Phase switch {
            LostCallPhase.AwaitingIntroduction => LostCallAction.InviteIntroduction,
            LostCallPhase.AwaitingAcquisition => LostCallAction.InviteAcquisition,
            LostCallPhase.Acquired => LostCallAction.InviteDelivery,
            LostCallPhase.Delivered => LostCallAction.InviteDebrief,
            _ => LostCallAction.ContinueTrading };
    }

    private static bool Shape(LostCallBook? b)
    {
        if (b == null || b.Disabled || b.Version != 1 || b.ItemDefinitionVersion != 1 || (int)b.Phase is < 0 or > 7
            || (int)b.ItemDisposition is < 0 or > 4 || (int)b.ProposedRoute is < -1 or > 2 || (int)b.CommittedRoute is < -1 or > 2
            || b.ProposalRevision < 0 || (b.ProposedRoute == LostCallRoute.None) != (b.ProposalRevision == 0)
            || b.AcquiredActualUniqueId < 0 || b.ActualPurchaseAmount < 0 || b.ActualPurchaseAmount > MaxAmount
            || b.ActualSaleAmount < 0 || b.ActualSaleAmount > MaxAmount || b.PendingResearch is < 0 or > 1
            || !OptionalToken(b.NativePurchaseToken) || !OptionalToken(b.NativeDeliveryToken) || !OptionalToken(b.NativeDisposalToken)
            || !OptionalToken(b.LastIntroductionAnswerToken) || b.RewardReceiptId == null || b.RecentVisitTokens == null
            || b.RecentVisitTokens.Count > MaxRecentVisits || b.RecentVisitTokens.Any(t => !Token(t))
            || b.RecentVisitTokens.Distinct(StringComparer.Ordinal).Count() != b.RecentVisitTokens.Count) return false;
        if (b.LastVisitDay == -1)
        {
            if (b.LastVisitKind != LostCallVisitKind.None || b.LastVisitToken != "" || !b.LastVisitFinished
                || b.LastVisitRoute != LostCallRoute.None || b.LastVisitProposalRevision != 0 || b.RecentVisitTokens.Count != 0) return false;
        }
        else if (!Day(b.LastVisitDay) || (int)b.LastVisitKind is < 1 or > 5 || !Token(b.LastVisitToken)
            || !b.RecentVisitTokens.Contains(b.LastVisitToken) || b.LastVisitProposalRevision < 0
            || b.LastVisitProposalRevision > b.ProposalRevision
            || (b.LastVisitKind == LostCallVisitKind.Delivery ? !Route(b.LastVisitRoute) || b.LastVisitProposalRevision == 0 : b.LastVisitRoute != LostCallRoute.None)) return false;
        var nativeTokens = NativeTokens(b).Where(t => t.Length > 0).ToArray();
        if (nativeTokens.Distinct(StringComparer.Ordinal).Count() != nativeTokens.Length) return false;
        bool noItem = b.AcquiredActualUniqueId == 0 && b.ActualPurchaseAmount == 0 && b.ActualSaleAmount == 0
            && b.PurchaseDay == -1 && b.DispositionDay == -1 && b.NativePurchaseToken == "" && b.NativeDeliveryToken == ""
            && b.NativeDisposalToken == "" && b.ItemDisposition == LostCallDisposition.None && b.ProposedRoute == LostCallRoute.None
            && b.CommittedRoute == LostCallRoute.None;
        bool noReward = !b.OriginalVoiceUnlocked && b.RewardReceiptId == "" && b.PendingResearch == 0 && !b.RewardSettled;
        if (b.Phase == LostCallPhase.Locked) return noItem && noReward && b.OfferedDay == -1 && b.InvitedDay == -1
            && b.IntroductionDay == -1 && b.ClosedDay == -1 && b.LastVisitDay == -1 && b.LastIntroductionAnswerToken == "";
        if (!Day(b.OfferedDay) || b.OfferedDay < 2) return false;
        if (b.Phase == LostCallPhase.Offered) return noItem && noReward && b.InvitedDay == -1
            && b.IntroductionDay == -1 && b.ClosedDay == -1 && b.LastVisitDay == -1 && b.LastIntroductionAnswerToken == "";
        if (b.Phase == LostCallPhase.Declined) return noItem && Day(b.ClosedDay) && b.ClosedDay >= b.OfferedDay
            && b.RewardSettled && b.PendingResearch == 0 && b.RewardReceiptId == "" && !b.OriginalVoiceUnlocked
            && (b.InvitedDay == -1 ? b.IntroductionDay == -1 && b.LastVisitDay == -1 && b.LastIntroductionAnswerToken == ""
                : Day(b.InvitedDay) && b.InvitedDay >= b.OfferedDay && Day(b.IntroductionDay) && b.IntroductionDay >= b.InvitedDay
                    && b.ClosedDay >= b.IntroductionDay && b.LastVisitDay == b.IntroductionDay && b.LastVisitKind == LostCallVisitKind.Introduction
                    && b.LastIntroductionAnswerToken == b.LastVisitToken);
        if (!Day(b.InvitedDay) || b.InvitedDay < b.OfferedDay || (b.LastVisitDay >= 0 && b.LastVisitDay < b.InvitedDay)) return false;
        if (b.Phase == LostCallPhase.AwaitingIntroduction) return noItem && noReward && b.ClosedDay == -1
            && (b.IntroductionDay == -1 ? b.LastIntroductionAnswerToken == "" : Day(b.IntroductionDay) && b.IntroductionDay >= b.InvitedDay
                && Token(b.LastIntroductionAnswerToken) && b.LastVisitDay >= b.IntroductionDay)
            && (b.LastVisitDay == -1 || b.LastVisitKind == LostCallVisitKind.Introduction);
        if (!Day(b.IntroductionDay) || b.IntroductionDay < b.InvitedDay || !Token(b.LastIntroductionAnswerToken)) return false;
        if (b.Phase == LostCallPhase.AwaitingAcquisition) return noItem && noReward && b.ClosedDay == -1
            && b.LastVisitDay >= b.IntroductionDay && b.LastVisitKind is LostCallVisitKind.Introduction or LostCallVisitKind.Acquisition;
        if (!Day(b.PurchaseDay) || b.PurchaseDay <= b.IntroductionDay || b.AcquiredActualUniqueId <= 0
            || b.ActualPurchaseAmount <= 0 || !Token(b.NativePurchaseToken) || b.LastVisitDay < b.PurchaseDay) return false;
        if (b.Phase == LostCallPhase.Acquired) return noReward && b.ClosedDay == -1 && b.DispositionDay == -1
            && b.ItemDisposition == LostCallDisposition.Owned && b.CommittedRoute == LostCallRoute.None
            && b.ActualSaleAmount == 0 && b.NativeDeliveryToken == "" && b.NativeDisposalToken == ""
            && b.LastVisitKind is LostCallVisitKind.Acquisition or LostCallVisitKind.Delivery;
        if (!Day(b.DispositionDay) || b.DispositionDay < b.PurchaseDay) return false;
        bool disposition = b.ItemDisposition switch {
            LostCallDisposition.Delivered => b.DispositionDay > b.PurchaseDay && b.ActualSaleAmount > 0 && Route(b.CommittedRoute) && b.CommittedRoute == b.ProposedRoute
                && b.ProposalRevision > 0 && Token(b.NativeDeliveryToken) && b.NativeDisposalToken == "",
            LostCallDisposition.SoldElsewhere => b.CommittedRoute == LostCallRoute.None && Token(b.NativeDeliveryToken) && b.NativeDisposalToken == "",
            LostCallDisposition.Destroyed => b.CommittedRoute == LostCallRoute.None && b.ActualSaleAmount == 0 && b.NativeDeliveryToken == "" && Token(b.NativeDisposalToken),
            _ => false };
        if (!disposition) return false;
        if (b.Phase == LostCallPhase.Delivered) return noReward && b.ClosedDay == -1;
        if (b.Phase != LostCallPhase.Closed || !Day(b.ClosedDay) || b.ClosedDay <= b.DispositionDay) return false;
        bool research = b.ItemDisposition == LostCallDisposition.Delivered && b.CommittedRoute == LostCallRoute.Information;
        bool voice = b.ItemDisposition == LostCallDisposition.Delivered && b.CommittedRoute == LostCallRoute.Human;
        return b.OriginalVoiceUnlocked == voice && (research ? b.RewardReceiptId == Receipt(b.NativeDeliveryToken, b.AcquiredActualUniqueId)
            && (b.RewardSettled ? b.PendingResearch == 0 : b.PendingResearch == 1)
            : b.RewardReceiptId == "" && b.PendingResearch == 0 && b.RewardSettled);
    }

    private static bool AtVisit(LostCallBook book, LostCallVisitKind kind, int day, string token) => Shape(book) && Day(day)
        && !book.LastVisitFinished && book.LastVisitKind == kind && book.LastVisitDay == day && book.LastVisitToken == token && Token(token);
    private static bool Evidence(LostCallTradeEvidence trade) => Day(trade.Day) && trade.ActualUniqueId > 0 && trade.ItemId == ItemId
        && trade.Units == 1 && trade.ActualAmount >= 0 && trade.ActualAmount <= MaxAmount && Token(trade.NativeToken)
        && trade.PaymentConfirmed && trade.OwnershipTransferred;
    private static IEnumerable<string> NativeTokens(LostCallBook book) => new[] { book.NativePurchaseToken, book.NativeDeliveryToken, book.NativeDisposalToken };
    private static bool OptionalToken(string? token) => token == "" || Token(token);
    private static bool Token(string? token) => !string.IsNullOrWhiteSpace(token) && token.Length <= MaxTokenLength
        && token == token.Trim() && !token.Any(char.IsControl);
    private static bool Day(int day) => day is >= 0 and <= MaxDay;
    private static bool Route(LostCallRoute route) => route is LostCallRoute.Profit or LostCallRoute.Human or LostCallRoute.Information;
    private static int NextDay(int day) => Day(day) && day < MaxDay ? day + 1 : -1;
    private static LostCallChoice Choice(int key, string label, string detail, int budget = 0) => new(key, label, detail, false, budget);
    private static string Receipt(string token, int uid) => "lostcall/v1/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uid + ":" + token))).ToLowerInvariant();
    private static void Decline(LostCallBook book, int day) { book.Phase = LostCallPhase.Declined; book.ClosedDay = day; book.RewardSettled = true; }
    private static LostCallReply Yes(string message) => new(true, message);
    private static LostCallReply No(string message) => new(false, message);
}
