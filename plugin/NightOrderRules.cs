using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpacePhone;

public enum NightOrderBuyerKind { Warehouse = 0, Canteen = 1, Repair = 2 }
public enum NightOrderCategory { Food = 0, Supplies = 1, Repair = 2 }
public enum NightOrderResult { Completed = 0, Cancelled = 1, PartialClosed = 2 }
public enum NightOrderAction { ContinueTrading, ReviewBoard, InviteBuyer, ReturnToCounter, CheckInventory, ViewReceipt }

// A separate phone ledger. No wallet, inventory, career, native pointer or automatic reward is owned here.
public sealed class NightOrderBook
{
    public int Version { get; set; } = 1;
    public bool Disabled { get; set; }
    public int UnlockedDay { get; set; } = -1;
    public int LastBoardDay { get; set; } = -1;
    public int BoardSerial { get; set; }
    public int LastAcceptedDay { get; set; } = -1;
    public int LastCompletionDay { get; set; } = -1;
    public int LastClosedDay { get; set; } = -1;
    public NightOrderOffer[] Offers { get; set; } = Array.Empty<NightOrderOffer>();
    public NightOrderActive? Active { get; set; }
    public List<NightOrderReceipt> History { get; set; } = new();
    public int TotalCompleted { get; set; }
    public int TotalClosed { get; set; }
    public long TotalDeliveredUnits { get; set; }
    public long TotalActualPaid { get; set; }
}

public sealed record NightOrderOffer
{
    public string OrderId { get; init; } = "";
    public int OfferedDay { get; init; } = -1;
    public int Ordinal { get; init; }
    public int Slot { get; init; }
    public NightOrderBuyerKind BuyerKind { get; init; }
    public string ItemId { get; init; } = "";
    public string ItemName { get; init; } = "";
    public int RequiredUnits { get; init; }
    public int ProposedTotalBudget { get; init; }
    public long ReferenceUnitValue { get; init; }
    public long ObservedOwnedUnits { get; init; }
    // Cost of all same-ID lots observed at registration, not a promised cost for the exact units later sold.
    public long? KnownStockCost { get; init; }
    public int ReputationLevel { get; init; }
    public bool RequiresDrinkingWater { get; init; }
}

public sealed class NightOrderActive
{
    public NightOrderOffer Offer { get; set; } = new();
    public int AcceptedDay { get; set; } = -1;
    public int DeliveredUnits { get; set; }
    public long ActualPaidTotal { get; set; }
    public int PlanRevision { get; set; } = 1;
    public List<NightOrderPayment> Payments { get; set; } = new();
}

public sealed record NightOrderPayment(int Day, int ActualUniqueId, string ItemId, int Units, int ActualAmount, string NativeToken);
public sealed record NightOrderReceipt(NightOrderOffer Offer, int AcceptedDay, int ClosedDay, int DeliveredUnits,
    long ActualPaidTotal, NightOrderResult Result, NightOrderPayment[] Payments);
// Caller has already proved real ownership, ordinary legal native item types, and drinking-water quality if required.
public readonly record struct NightOrderStockLot(int ActualUniqueId, string ItemId, string ItemName, int Units,
    NightOrderCategory Category, long BaseUnitValue, long? KnownTotalCost, bool RequiresDrinkingWater = false);
public readonly record struct NightOrderVisitPlan(string OrderId, string ClientId, NightOrderBuyerKind BuyerKind,
    string RequiredItemId, int RemainingUnits, int ProposedBuyerBudget, int PlanRevision, bool RequiresDrinkingWater = false);
public readonly record struct NightOrderTradeEvidence(int Day, int ActualUniqueId, string ItemId, int Units,
    int ActualAmount, string NativeToken, bool PaymentConfirmed, bool OwnershipTransferred, bool GoodsRequirementConfirmed = false);
public readonly record struct NightOrderReply(bool Changed, string Message);
public sealed record NightOrderGuide(string Title, string Step, string Progress, string Instruction,
    string PrimaryLabel, NightOrderAction PrimaryAction, bool CanInvite, int RemainingUnits, int RemainingBudget);

public static class NightOrderRules
{
    public const int MaxDay = 100000;
    public const int MaxAmount = 1000000000;
    public const int MaxTokenLength = 180;
    public const int HistoryLimit = 24;
    public const string ClientPrefix = "spacephone.nightorder.";
    private const long MaxAggregate = 1000000000000;
    private const int MaxSnapshotLots = 3000;
    private const long MaxObservedUnits = (long)MaxSnapshotLots * 100000;

    public static NightOrderBook DisabledBook() => new() { Disabled = true };
    public static NightOrderBook Validate(NightOrderBook? book) => book == null ? new NightOrderBook() : Shape(book) ? book : DisabledBook();
    public static bool IsValid(NightOrderBook? book) => Shape(book);
    public static string Encode(NightOrderBook book) => JsonSerializer.Serialize(Validate(book));
    public static NightOrderBook Decode(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 131072) return DisabledBook();
        try { return JsonSerializer.Deserialize<NightOrderBook>(json) is { } book ? Validate(book) : DisabledBook(); }
        catch (JsonException) { return DisabledBook(); }
        catch (NotSupportedException) { return DisabledBook(); }
    }
    public static bool ValidDay(int day) => day is >= 0 and <= MaxDay;
    public static int ReputationLevel(NightOrderBook book) => Shape(book) ? LevelFor(book.TotalCompleted) : 0;
    public static int UnitLimit(NightOrderBook book) => 2 + ReputationLevel(book);
    public static int BudgetCap(NightOrderBook book) => BudgetCapFor(ReputationLevel(book));
    public static string ReputationName(NightOrderBook book) => ReputationLevel(book) switch { 2 => "夜班熟面孔", 1 => "可靠小店", _ => "初次合作" };
    public static string BuyerName(NightOrderBuyerKind kind) => kind switch {
        NightOrderBuyerKind.Warehouse => "港仓验货员", NightOrderBuyerKind.Canteen => "夜班食堂领班",
        NightOrderBuyerKind.Repair => "巡修工", _ => "夜班买方" };

    // At most one immutable registration per game day. Empty stock also seals the board for that day.
    // Taking a snapshot never spawns merchandise. An accepted order survives all future day changes.
    public static NightOrderReply Refresh(NightOrderBook book, int day, int actualPaidTradeCount, IEnumerable<NightOrderStockLot> stock)
    {
        if (!Shape(book) || !ValidDay(day) || day < 3 || actualPaidTradeCount < 5 || stock == null)
            return No("先做五笔实际付款的柜台生意；第三个营业日起可留意夜班求购。");
        if (book.Active != null) return No("已接的求购跨日保留，没有期限；先照顾这一单。");
        if (day <= book.LastBoardDay || day <= book.LastAcceptedDay || day <= book.LastCompletionDay || day <= book.LastClosedDay)
            return No("今天登记过的求购保持原样，明天再看新需求。");
        var source = stock.Take(MaxSnapshotLots + 1).ToArray();
        if (source.Length > MaxSnapshotLots) return No("现货资料仍在整理，先照常营业。");
        int level = LevelFor(book.TotalCompleted), cap = BudgetCapFor(level), serial = book.BoardSerial + 1;
        if (serial > MaxDay) return No("夜班账本已到记录上限，原版生意照常。");
        var unique = source.Where(LotValid).GroupBy(l => l.ActualUniqueId).Where(g => g.Count() == 1).Select(g => g.Single());
        var candidates = new List<Candidate>();
        foreach (var group in unique.GroupBy(l => l.ItemId, StringComparer.Ordinal))
        {
            var lots = group.ToArray(); var first = lots.OrderBy(l => l.ActualUniqueId).First();
            // A translated name or a mixed-quality same-ID scan cannot silently define a demand.
            if (lots.Any(l => l.Category != first.Category || l.RequiresDrinkingWater != first.RequiresDrinkingWater)) continue;
            long count = lots.Sum(l => (long)l.Units);
            long reference = lots.Max(l => l.BaseUnitValue);
            if (reference > cap) continue;
            int units = (int)Math.Min(Math.Min(2 + level, count), cap / reference);
            if (units < 1) continue;
            var buyer = BuyerFor(first.Category);
            int percentage = buyer == NightOrderBuyerKind.Canteen ? 115 : buyer == NightOrderBuyerKind.Warehouse ? 125 : 135;
            int budget = (int)Math.Min(cap, Math.Max(1, reference * units * percentage / 100));
            long? cost = lots.All(l => l.KnownTotalCost.HasValue) ? lots.Sum(l => l.KnownTotalCost!.Value) : null;
            if (cost > MaxAggregate) cost = null;
            candidates.Add(new(first.ItemId, first.ItemName, buyer, count, reference, units, budget, cost,
                first.RequiresDrinkingWater, Hash("candidate:" + day + ":" + first.ItemId)));
        }
        var selected = new List<Candidate>();
        foreach (NightOrderBuyerKind kind in Enum.GetValues<NightOrderBuyerKind>())
        {
            var candidate = candidates.Where(c => c.Buyer == kind).OrderBy(c => c.Priority, StringComparer.Ordinal).FirstOrDefault();
            if (candidate != null) selected.Add(candidate);
        }
        foreach (var candidate in candidates.OrderBy(c => c.Priority, StringComparer.Ordinal))
            if (selected.Count < 3 && !selected.Any(c => c.ItemId == candidate.ItemId)) selected.Add(candidate);
        book.UnlockedDay = book.UnlockedDay < 0 ? day : book.UnlockedDay;
        book.BoardSerial = serial; book.LastBoardDay = day;
        book.Offers = selected.Take(3).Select((c, slot) => new NightOrderOffer {
            OrderId = OrderId(day, serial, slot, c.Buyer, c.ItemId), OfferedDay = day, Ordinal = serial, Slot = slot,
            BuyerKind = c.Buyer, ItemId = c.ItemId, ItemName = c.ItemName, RequiredUnits = c.Units,
            ProposedTotalBudget = c.Budget, ReferenceUnitValue = c.Reference, ObservedOwnedUnits = c.OwnedUnits,
            KnownStockCost = c.KnownCost, ReputationLevel = level, RequiresDrinkingWater = c.Water }).ToArray();
        return Yes(book.Offers.Length == 0 ? "今天没有合适的普通现货。明天再按现货登记，先照常营业。"
            : "夜班求购已按现货登记。今天可选一单，接下后没有期限；预算仍要在柜台议价。");
    }

    public static bool CanAccept(NightOrderBook book, string orderId, int day) => Shape(book) && ValidDay(day)
        && book.UnlockedDay >= 3 && book.Active == null && day == book.LastBoardDay
        && day > book.LastAcceptedDay && day > book.LastCompletionDay && day > book.LastClosedDay && book.TotalClosed < MaxDay
        && book.Offers.Any(o => o.OrderId == orderId);
    public static NightOrderReply Accept(NightOrderBook book, string orderId, int day)
    {
        if (!CanAccept(book, orderId, day)) return No("这张求购现在不能接。已接单保留，今天不反复重接或换单。");
        var offer = book.Offers.Single(o => o.OrderId == orderId);
        book.Active = new NightOrderActive { Offer = offer, AcceptedDay = day };
        book.LastAcceptedDay = day;
        return Yes("已记下 " + offer.ItemName + " ×" + offer.RequiredUnits + "。货物仍归你，闲时预约本人来柜台正常买卖，没有期限。");
    }
    public static int Remaining(NightOrderBook book) => Shape(book) && book.Active is { } active ? active.Offer.RequiredUnits - active.DeliveredUnits : 0;
    public static int RemainingBudget(NightOrderBook book) => Shape(book) && book.Active is { } active ? (int)(active.Offer.ProposedTotalBudget - active.ActualPaidTotal) : 0;
    public static string ClientId(NightOrderActive active) => ClientPrefix + (int)active.Offer.BuyerKind + "." + active.Offer.OrderId + "." + active.PlanRevision;
    public static NightOrderVisitPlan? PlanVisit(NightOrderBook book, int day)
    {
        if (!Shape(book) || !ValidDay(day) || book.Active is not { } active || day < active.AcceptedDay
            || (active.Payments.Count > 0 && day < active.Payments[^1].Day) || book.LastCompletionDay == day) return null;
        return new(active.Offer.OrderId, ClientId(active), active.Offer.BuyerKind, active.Offer.ItemId,
            active.Offer.RequiredUnits - active.DeliveredUnits, (int)(active.Offer.ProposedTotalBudget - active.ActualPaidTotal),
            active.PlanRevision, active.Offer.RequiresDrinkingWater);
    }

    // A current-run native observer supplies payment AND the exact transfer, not a phone button or disappearance scan.
    public static NightOrderReply RecordPayment(NightOrderBook book, NightOrderVisitPlan buyerPlan, NightOrderTradeEvidence trade)
    {
        if (!EvidenceValid(trade) || PlanVisit(book, trade.Day) is not { } expected || expected != buyerPlan
            || book.Active is not { } active || trade.ItemId != active.Offer.ItemId
            || trade.Units > expected.RemainingUnits || trade.ActualAmount > expected.ProposedBuyerBudget
            || (active.Offer.RequiresDrinkingWater && !trade.GoodsRequirementConfirmed)
            || active.Payments.Any(p => p.NativeToken == trade.NativeToken)
            || book.History.Any(h => h.Payments.Any(p => p.NativeToken == trade.NativeToken))
            || book.TotalActualPaid > MaxAggregate - trade.ActualAmount) return No("没有登记这笔未核实、重复或不属于本单的成交；原版实际钱货照常。");
        active.Payments.Add(new(trade.Day, trade.ActualUniqueId, trade.ItemId, trade.Units, trade.ActualAmount, trade.NativeToken));
        active.DeliveredUnits += trade.Units; active.ActualPaidTotal += trade.ActualAmount; active.PlanRevision++;
        book.TotalDeliveredUnits += trade.Units; book.TotalActualPaid += trade.ActualAmount;
        if (active.DeliveredUnits == active.Offer.RequiredUnits)
        {
            Close(book, trade.Day, NightOrderResult.Completed);
            return Yes("求购已按真实交付完成。只保留柜台实际收入和合作实绩，不另发钱、货物或研究。");
        }
        if (active.ActualPaidTotal == active.Offer.ProposedTotalBudget)
        {
            Close(book, trade.Day, NightOrderResult.PartialClosed);
            return Yes("本单预算已用完，余量平和归档。已成交钱货按实保留，不追回收入，也不算全部完成。");
        }
        return Yes("已核对实际售出 " + trade.Units + " 件，收入 " + trade.ActualAmount + "。本次收货封单，余 "
            + (active.Offer.RequiredUnits - active.DeliveredUnits) + " 件闲时另约，没有期限。");
    }
    public static NightOrderReply Cancel(NightOrderBook book, int day)
    {
        if (!Shape(book) || !ValidDay(day) || book.Active is not { } active || day < active.AcceptedDay
            || (active.Payments.Count > 0 && day < active.Payments[^1].Day)) return No("没有可以取消的当前余量。");
        Close(book, day, NightOrderResult.Cancelled);
        return Yes("未交的余量已取消。已成交收入和交付回执保留，没有罚金；明天再看新求购。");
    }
    private static void Close(NightOrderBook book, int day, NightOrderResult result)
    {
        var active = book.Active!;
        book.History.Add(new(active.Offer, active.AcceptedDay, day, active.DeliveredUnits, active.ActualPaidTotal, result, active.Payments.ToArray()));
        if (book.History.Count > HistoryLimit) book.History.RemoveAt(0);
        book.TotalClosed++; book.LastClosedDay = day;
        if (result == NightOrderResult.Completed) { book.TotalCompleted++; book.LastCompletionDay = day; }
        book.Active = null;
    }

    public static NightOrderGuide GetGuide(NightOrderBook book, int day, long observedMatchingUnits = -1, bool buyerAtCounter = false)
    {
        const string title = "夜班求购";
        if (!Shape(book) || !ValidDay(day)) return Guide("求购账本待核对", "店里的生意照常", "这份新账本暂时对不上，先照常营业。", "继续营业", NightOrderAction.ContinueTrading);
        if (book.UnlockedDay < 0) return Guide("先把柜台生意做起来", "D3 起 · 五笔实际付款成交", "夜班买方会留意有现货的小店。先做原有生意，没有必须接的任务。", "继续营业", NightOrderAction.ContinueTrading);
        string standing = ReputationName(book) + " · 完成 " + book.TotalCompleted + " 单";
        if (book.Active is not { } active)
        {
            if (day <= book.LastAcceptedDay || day <= book.LastCompletionDay || day <= book.LastClosedDay)
                return Guide("今天这单已经收好", standing, "已付部分照实留在回执。明天会按现货登记新需求，没有补交或退款义务。", "查看回执", NightOrderAction.ViewReceipt);
            if (day != book.LastBoardDay) return Guide("今天的现货正在核对", standing, "新需求会按今天的普通现货登记；昨日未接的单不强行沿用。", "继续营业", NightOrderAction.ContinueTrading);
            return Guide(book.Offers.Length == 0 ? "今天没有适合的现货" : "选一张合适的求购", standing,
                book.Offers.Length == 0 ? "今天已按普通现货核对。明天再看，继续做原来的柜台生意。" : "今天最多选一单，接下后跨日保留。预算只表示买方余款，不保证售价或利润。",
                book.Offers.Length == 0 ? "继续营业" : "查看今日求购", book.Offers.Length == 0 ? NightOrderAction.ContinueTrading : NightOrderAction.ReviewBoard);
        }
        int remaining = active.Offer.RequiredUnits - active.DeliveredUnits;
        int budget = (int)(active.Offer.ProposedTotalBudget - active.ActualPaidTotal);
        string progress = active.Offer.ItemName + " · 已交 " + active.DeliveredUnits + "/" + active.Offer.RequiredUnits + " · 实收 " + active.ActualPaidTotal;
        if (buyerAtCounter) return Guide("买方已在柜台", progress, "照常议价、付款后移交实物。未成交可以正常结束来访，余量保留。", "回柜台", NightOrderAction.ReturnToCounter, false, remaining, budget);
        if (observedMatchingUnits <= 0) return Guide(observedMatchingUnits < 0 ? "先核对这款现货" : "先找这款普通实物", progress,
            "检查货柜、容器或供货来源。扫描不到不是销毁，没有期限，可以取消未交余量。", "查看货柜", NightOrderAction.CheckInventory, false, remaining, budget);
        bool ready = PlanVisit(book, day) != null;
        return Guide("闲时约本人来收货", progress,
            "还需 " + remaining + " 件，本单剩余预算 " + budget + "。先按原版拆出合适数量；只认本人实际付款和实物移交，没有额外奖励。"
            + (active.Offer.RequiresDrinkingWater ? "饮水须满装至少500ml，空瓶不算。" : ""), ready ? "预约来柜台收货" : "继续营业",
            ready ? NightOrderAction.InviteBuyer : NightOrderAction.ContinueTrading, ready, remaining, budget);
        NightOrderGuide Guide(string step, string progress, string instruction, string primary, NightOrderAction action,
            bool canInvite = false, int units = 0, int budget = 0) => new(title, step, progress, instruction, primary, action, canInvite, units, budget);
    }

    private static bool Shape(NightOrderBook? b)
    {
        if (b == null || b.Disabled || b.Version != 1 || !OptionalDay(b.UnlockedDay) || !OptionalDay(b.LastBoardDay)
            || !OptionalDay(b.LastAcceptedDay) || !OptionalDay(b.LastCompletionDay) || !OptionalDay(b.LastClosedDay) || b.BoardSerial is < 0 or > MaxDay
            || b.Offers == null || b.Offers.Length > 3 || b.History == null || b.History.Count > HistoryLimit
            || b.TotalCompleted < 0 || b.TotalClosed < b.TotalCompleted || b.TotalClosed > b.BoardSerial
            || b.TotalDeliveredUnits < 0 || b.TotalDeliveredUnits > (long)MaxDay * 4
            || b.TotalActualPaid < 0 || b.TotalActualPaid > MaxAggregate) return false;
        if (b.UnlockedDay == -1) return b.LastBoardDay == -1 && b.BoardSerial == 0 && b.LastAcceptedDay == -1
            && b.LastCompletionDay == -1 && b.LastClosedDay == -1 && b.Offers.Length == 0 && b.Active == null && b.History.Count == 0
            && b.TotalCompleted == 0 && b.TotalClosed == 0 && b.TotalDeliveredUnits == 0 && b.TotalActualPaid == 0;
        if (b.UnlockedDay < 3 || b.LastBoardDay < b.UnlockedDay || b.BoardSerial < 1
            || b.BoardSerial > b.LastBoardDay - 2 || (b.LastAcceptedDay != -1 && b.LastAcceptedDay < b.UnlockedDay)
            || (b.TotalCompleted == 0 ? b.LastCompletionDay != -1 : b.LastCompletionDay < b.UnlockedDay)
            || (b.TotalClosed == 0 ? b.LastClosedDay != -1 : b.LastClosedDay < b.UnlockedDay)
            || b.LastCompletionDay > b.LastClosedDay) return false;
        if (b.Offers.Any(o => !OfferValid(o) || o.OfferedDay != b.LastBoardDay || o.Ordinal != b.BoardSerial || o.ReputationLevel > LevelFor(b.TotalCompleted))
            || b.Offers.Select(o => o.OrderId).Distinct(StringComparer.Ordinal).Count() != b.Offers.Length
            || b.Offers.Select(o => o.ItemId).Distinct(StringComparer.Ordinal).Count() != b.Offers.Length
            || b.Offers.Select(o => o.Slot).Distinct().Count() != b.Offers.Length) return false;
        if (b.History.Any(h => !ReceiptValid(h) || h.Offer.Ordinal > b.BoardSerial || h.AcceptedDay < b.UnlockedDay)
            || b.History.Select(h => h.Offer.OrderId).Distinct(StringComparer.Ordinal).Count() != b.History.Count
            || b.History.Count != Math.Min(b.TotalClosed, HistoryLimit) || b.TotalCompleted < b.History.Count(h => h.Result == NightOrderResult.Completed)) return false;
        for (int i = 1; i < b.History.Count; i++)
            if (b.History[i].AcceptedDay <= b.History[i - 1].AcceptedDay || b.History[i].AcceptedDay <= b.History[i - 1].ClosedDay) return false;
        if (b.History.Count > 0 && (b.History[^1].AcceptedDay > b.LastAcceptedDay || b.History[^1].ClosedDay != b.LastClosedDay)) return false;
        var all = b.History.SelectMany(h => h.Payments).ToList();
        if (b.Active is { } active)
        {
            if (!ActiveValid(active) || active.AcceptedDay != b.LastAcceptedDay || active.AcceptedDay != b.LastBoardDay
                || !b.Offers.Any(o => o == active.Offer) || active.Offer.Ordinal != b.BoardSerial
                || b.History.Any(h => h.Offer.OrderId == active.Offer.OrderId || h.ClosedDay > active.AcceptedDay)
                || b.LastClosedDay >= active.AcceptedDay) return false;
            all.AddRange(active.Payments);
        }
        else if (b.TotalClosed > 0 && b.History.Count == 0) return false;
        else if (b.History.Count == 0 ? b.LastAcceptedDay != -1 : b.LastAcceptedDay != b.History[^1].AcceptedDay) return false;
        var recentComplete = b.History.Where(h => h.Result == NightOrderResult.Completed).ToArray();
        if (recentComplete.Length > 0 && b.LastCompletionDay != recentComplete[^1].ClosedDay) return false;
        if (b.TotalClosed <= HistoryLimit && b.TotalCompleted != recentComplete.Length) return false;
        if (all.Select(p => p.NativeToken).Distinct(StringComparer.Ordinal).Count() != all.Count
            || all.Sum(p => (long)p.Units) > b.TotalDeliveredUnits || all.Sum(p => (long)p.ActualAmount) > b.TotalActualPaid) return false;
        if (b.TotalClosed <= HistoryLimit && (all.Sum(p => (long)p.Units) != b.TotalDeliveredUnits
            || all.Sum(p => (long)p.ActualAmount) != b.TotalActualPaid)) return false;
        return true;
    }
    private static bool OfferValid(NightOrderOffer? o) => o != null && ValidDay(o.OfferedDay) && o.OfferedDay >= 3
        && o.Ordinal is >= 1 and <= MaxDay && o.Ordinal <= o.OfferedDay - 2 && o.Slot is >= 0 and <= 2
        && BuyerValid(o.BuyerKind) && Text(o.ItemId, 120) && !o.ItemId.StartsWith("spacephone.", StringComparison.Ordinal)
        && Text(o.ItemName, 160) && o.ReputationLevel is >= 0 and <= 2 && o.RequiredUnits >= 1 && o.RequiredUnits <= 2 + o.ReputationLevel
        && o.ReferenceUnitValue is >= 1 and <= 10000 && o.ObservedOwnedUnits >= o.RequiredUnits && o.ObservedOwnedUnits <= MaxObservedUnits
        && o.ProposedTotalBudget >= 1 && o.ProposedTotalBudget <= BudgetCapFor(o.ReputationLevel)
        && (!o.KnownStockCost.HasValue || o.KnownStockCost.Value is >= 0 and <= MaxAggregate)
        && (!o.RequiresDrinkingWater || o.BuyerKind == NightOrderBuyerKind.Canteen)
        && o.OrderId == OrderId(o.OfferedDay, o.Ordinal, o.Slot, o.BuyerKind, o.ItemId);
    private static bool ActiveValid(NightOrderActive? a) => a != null && OfferValid(a.Offer)
        && ValidDay(a.AcceptedDay) && a.AcceptedDay == a.Offer.OfferedDay && a.DeliveredUnits >= 0 && a.DeliveredUnits < a.Offer.RequiredUnits
        && a.ActualPaidTotal >= 0 && a.ActualPaidTotal < a.Offer.ProposedTotalBudget && a.Payments != null
        && a.PlanRevision == a.Payments.Count + 1 && PaymentsValid(a.Payments, a.Offer, a.AcceptedDay, MaxDay, a.DeliveredUnits, a.ActualPaidTotal);
    private static bool ReceiptValid(NightOrderReceipt? h)
    {
        if (h == null || !OfferValid(h.Offer) || !ValidDay(h.AcceptedDay) || h.AcceptedDay != h.Offer.OfferedDay
            || !ValidDay(h.ClosedDay) || h.ClosedDay < h.AcceptedDay || h.DeliveredUnits < 0 || h.DeliveredUnits > h.Offer.RequiredUnits
            || h.ActualPaidTotal < 0 || h.ActualPaidTotal > h.Offer.ProposedTotalBudget || h.Payments == null
            || !PaymentsValid(h.Payments, h.Offer, h.AcceptedDay, h.ClosedDay, h.DeliveredUnits, h.ActualPaidTotal)) return false;
        return h.Result switch {
            NightOrderResult.Completed => h.DeliveredUnits == h.Offer.RequiredUnits && h.ActualPaidTotal > 0,
            NightOrderResult.Cancelled => h.DeliveredUnits < h.Offer.RequiredUnits,
            NightOrderResult.PartialClosed => h.DeliveredUnits > 0 && h.DeliveredUnits < h.Offer.RequiredUnits && h.ActualPaidTotal == h.Offer.ProposedTotalBudget,
            _ => false };
    }
    private static bool PaymentsValid(IReadOnlyCollection<NightOrderPayment> payments, NightOrderOffer offer,
        int fromDay, int untilDay, int units, long amount)
    {
        if (payments.Count > offer.RequiredUnits || payments.Any(p => p == null || !ValidDay(p.Day) || p.Day < fromDay || p.Day > untilDay
            || p.ActualUniqueId <= 0 || p.ItemId != offer.ItemId || p.Units < 1 || p.Units > offer.RequiredUnits
            || p.ActualAmount <= 0 || p.ActualAmount > offer.ProposedTotalBudget || !Token(p.NativeToken))
            || payments.Select(p => p.NativeToken).Distinct(StringComparer.Ordinal).Count() != payments.Count
            || payments.Sum(p => (long)p.Units) != units || payments.Sum(p => (long)p.ActualAmount) != amount) return false;
        var ordered = payments.ToArray();
        for (int i = 1; i < ordered.Length; i++) if (ordered[i].Day < ordered[i - 1].Day) return false;
        return (units == 0) == (amount == 0) && (units == 0) == (payments.Count == 0);
    }
    private static bool LotValid(NightOrderStockLot l) => l.ActualUniqueId > 0 && Text(l.ItemId, 120)
        && !l.ItemId.StartsWith("spacephone.", StringComparison.Ordinal) && Text(l.ItemName, 160) && l.Units is >= 1 and <= 100000
        && (int)l.Category is >= 0 and <= 2 && l.BaseUnitValue is >= 1 and <= 10000
        && (!l.KnownTotalCost.HasValue || l.KnownTotalCost.Value is >= 0 and <= MaxAggregate)
        && (!l.RequiresDrinkingWater || l.Category == NightOrderCategory.Food);
    private static bool EvidenceValid(NightOrderTradeEvidence t) => ValidDay(t.Day) && t.ActualUniqueId > 0
        && Text(t.ItemId, 120) && t.Units is >= 1 and <= 4 && t.ActualAmount is > 0 and <= MaxAmount
        && Token(t.NativeToken) && t.PaymentConfirmed && t.OwnershipTransferred;
    private static int LevelFor(int completed) => completed >= 8 ? 2 : completed >= 3 ? 1 : 0;
    private static int BudgetCapFor(int level) => level switch { 2 => 460, 1 => 300, _ => 180 };
    private static NightOrderBuyerKind BuyerFor(NightOrderCategory category) => category switch {
        NightOrderCategory.Food => NightOrderBuyerKind.Canteen, NightOrderCategory.Supplies => NightOrderBuyerKind.Warehouse, _ => NightOrderBuyerKind.Repair };
    private static bool BuyerValid(NightOrderBuyerKind kind) => (int)kind is >= 0 and <= 2;
    private static bool OptionalDay(int day) => day == -1 || ValidDay(day);
    private static bool Token(string? token) => Text(token, MaxTokenLength);
    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max
        && value == value.Trim() && !value.Any(char.IsControl);
    private static string OrderId(int day, int ordinal, int slot, NightOrderBuyerKind kind, string itemId)
        => Hash("nightorder/v1:" + day + ":" + ordinal + ":" + slot + ":" + (int)kind + ":" + itemId)[..32];
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static NightOrderReply Yes(string message) => new(true, message);
    private static NightOrderReply No(string message) => new(false, message);
    private sealed record Candidate(string ItemId, string ItemName, NightOrderBuyerKind Buyer, long OwnedUnits,
        long Reference, int Units, int Budget, long? KnownCost, bool Water, string Priority);
}
