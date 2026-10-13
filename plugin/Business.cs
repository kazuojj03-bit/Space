namespace SpacePhone;

// Everything in this file is save data or pure rules. Native shop callbacks are
// responsible for confirming a completed transaction before calling RecordTrade.
public sealed class BusinessState
{
    public int Schema { get; set; } = 1;
    public int Career { get; set; } = -1;
    public int CareerSelectedDay { get; set; } = -1;
    public bool FreeMode { get; set; }
    public int ResearchPoints { get; set; } = 1;
    public int StoredResearchPoints { get; set; }
    public int ResearchEarnDay { get; set; } = -1;
    public int ResearchEarnedToday { get; set; }
    public int ResearchDay { get; set; } = -1;
    public int ResearchToday { get; set; }
    public int ResearchDailyBonus { get; set; }
    public int PurchaseCount { get; set; }
    public int SaleCount { get; set; }
    public long PurchaseAmount { get; set; }
    public long SaleAmount { get; set; }
    public long Turnover { get; set; }
    public int KnownResaleCount { get; set; }
    public int ProfitableResaleCount { get; set; }
    public List<string> KnownResaleItemKeys { get; set; } = new();
    public List<string> ProfitableResaleItemKeys { get; set; } = new();
    public List<string> UniqueClientIds { get; set; } = new();
    public List<string> PurchasedCategories { get; set; } = new();
    public List<BusinessTrade> Trades { get; set; } = new();
    public List<BusinessContact> Contacts { get; set; } = new();
    public List<BusinessLot> Lots { get; set; } = new();
    public List<string> SeenEvents { get; set; } = new();
    public List<string> RewardedItems { get; set; } = new();
    public List<ResearchReport> Reports { get; set; } = new();
}

public sealed class BusinessTrade
{
    public int Day { get; set; }
    public bool Purchase { get; set; }
    public string ItemUid { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string Category { get; set; } = "";
    public int Units { get; set; }
    public int Amount { get; set; }
    public string CustomerId { get; set; } = "";
    public string PublicName { get; set; } = "";
    public long? KnownCost { get; set; }
    public long? Profit { get; set; }
}

public sealed class BusinessContact
{
    public string CustomerId { get; set; } = "";
    public string PublicName { get; set; } = "";
    public int FirstDay { get; set; }
    public int LastDay { get; set; }
    public int Purchases { get; set; }
    public int Sales { get; set; }
    public long Turnover { get; set; }
    public List<string> Categories { get; set; } = new();
}

public sealed class BusinessLot
{
    public string ItemUid { get; set; } = "";
    public string ItemId { get; set; } = "";
    public int Units { get; set; }
    public long Cost { get; set; }
    public int Day { get; set; }
    public bool CostKnown { get; set; } = true;
}

public sealed class ResearchReport
{
    public int Day { get; set; }
    public int Company { get; set; }
    public int Offset { get; set; }
    public int TargetDay { get; set; }
    public int Low { get; set; }
    public int High { get; set; }
    public string Signal { get; set; } = "";
    public int Confidence { get; set; }
}

public static class BusinessRules
{
    public static readonly string[] CareerNames = { "商脉经纪", "鉴价师", "物流调度" };
    public const int ResearchFee = 30;
    public const int ResearchDailyLimit = 2;
    public const int ResearchPointLimit = 5;
    public const int ResearchRewardStorageLimit = 3;
    private const long MoneyLimit = 1000000000000000;

    public static bool RecordTrade(BusinessState state, int day, bool purchase,
        string itemUid, string itemId, string itemName, string category, int units,
        int amount, string customerId, string publicName)
    {
        // Zero-priced completed gifts can be logged, but grant no direct research.
        if (day < 0 || day > 100000 || units <= 0 || units > 1000000 || amount < 0
            || string.IsNullOrWhiteSpace(itemId)) return false;
        itemUid = Clean(itemUid, 80); itemId = Clean(itemId, 120);
        itemName = Clean(itemName, 160); category = Clean(category, 100);
        customerId = Clean(customerId, 120); publicName = Clean(publicName, 160);
        string token = $"{(purchase ? 'B' : 'S')}:{itemUid.Length}:{itemUid}:{itemId.Length}:{itemId}:{day}:{units}:{amount}";
        if (state.SeenEvents.Contains(token)) return false;
        if (state.PurchaseCount == int.MaxValue || state.SaleCount == int.MaxValue
            || state.Turnover > MoneyLimit - amount) return false;
        var existingLot = itemUid.Length > 0
            ? state.Lots.FirstOrDefault(l => l.ItemUid == itemUid && l.ItemId == itemId) : null;
        if (purchase && existingLot != null && existingLot.Units > int.MaxValue - units) return false;
        state.SeenEvents.Add(token);
        TrimOldest(state.SeenEvents, 256);

        BusinessLot? lot = existingLot;
        long? cost = null;
        if (purchase)
        {
            state.PurchaseCount = checked(state.PurchaseCount + 1);
            state.PurchaseAmount = checked(state.PurchaseAmount + amount);
            if (amount > 0 && category.Length > 0 && !state.PurchasedCategories.Contains(category)
                && state.PurchasedCategories.Count < 32) state.PurchasedCategories.Add(category);
            if (itemUid.Length > 0)
            {
                if (lot == null)
                {
                    lot = new BusinessLot { ItemUid = itemUid, ItemId = itemId, Day = day };
                    state.Lots.Add(lot);
                }
                lot.Units = checked(lot.Units + units);
                if (lot.CostKnown) lot.Cost = checked(lot.Cost + amount);
                lot.Day = day;
                // An already ambiguous stack cannot become known by adding purchases.
                TrimOldest(state.Lots, 128);
            }
        }
        else
        {
            state.SaleCount = checked(state.SaleCount + 1);
            state.SaleAmount = checked(state.SaleAmount + amount);
            if (lot != null)
            {
                if (lot.CostKnown && lot.Units >= units)
                {
                    // Keep the rounding remainder in the unsold stack, so partial
                    // sales conserve the exact original purchase cost.
                    cost = units == lot.Units ? lot.Cost : (long)(lot.Cost * (decimal)units / lot.Units);
                    lot.Cost -= cost.Value;
                    lot.Units -= units;
                }
                else
                {
                    // More sold units than tracked proves a merge/untracked origin.
                    // Do not label its partial tracked cost as the whole sale's cost.
                    lot.CostKnown = false;
                    lot.Cost = 0;
                    lot.Units = Math.Max(0, lot.Units - units);
                }
                if (lot.Units == 0) state.Lots.Remove(lot);
            }
            if (amount > 0 && cost.HasValue && itemUid.Length > 0)
            {
                string itemKey = $"{itemUid.Length}:{itemUid}:{itemId.Length}:{itemId}";
                if (!state.KnownResaleItemKeys.Contains(itemKey) && state.KnownResaleItemKeys.Count < 512)
                { state.KnownResaleItemKeys.Add(itemKey); state.KnownResaleCount++; }
                // A losing first sale may qualify for its first profitable milestone
                // later, but another resale of the same physical item grants no XP.
                if (amount > cost.Value && state.KnownResaleItemKeys.Contains(itemKey)
                    && !state.ProfitableResaleItemKeys.Contains(itemKey) && state.ProfitableResaleItemKeys.Count < 512)
                { state.ProfitableResaleItemKeys.Add(itemKey); state.ProfitableResaleCount++; }
            }
        }
        state.Turnover = checked(state.Turnover + amount);

        state.Trades.Insert(0, new BusinessTrade {
            Day = day, Purchase = purchase, ItemUid = itemUid, ItemId = itemId,
            ItemName = itemName, Category = category, Units = units, Amount = amount,
            CustomerId = customerId, PublicName = publicName, KnownCost = cost,
            Profit = cost.HasValue ? amount - cost.Value : null
        });
        if (state.Trades.Count > 60) state.Trades.RemoveRange(60, state.Trades.Count - 60);

        if (customerId.Length > 0 && amount > 0)
        {
            if (!state.UniqueClientIds.Contains(customerId) && state.UniqueClientIds.Count < 256)
                state.UniqueClientIds.Add(customerId);
            var contact = state.Contacts.FirstOrDefault(c => c.CustomerId == customerId);
            if (contact == null)
            {
                contact = new BusinessContact { CustomerId = customerId, FirstDay = day };
                state.Contacts.Add(contact);
            }
            contact.PublicName = publicName;
            contact.LastDay = day;
            contact.Turnover = checked(contact.Turnover + amount);
            if (purchase) contact.Purchases = checked(contact.Purchases + 1);
            else contact.Sales = checked(contact.Sales + 1);
            if (category.Length > 0 && !contact.Categories.Contains(category) && contact.Categories.Count < 16)
                contact.Categories.Add(category);
            state.Contacts.Remove(contact); state.Contacts.Insert(0, contact);
            if (state.Contacts.Count > 32) state.Contacts.RemoveRange(32, state.Contacts.Count - 32);
        }

        // Consume the first-seen physical item's eligibility even when daily/held
        // caps are full. Its later resale/next-day round trip cannot mint points.
        if (amount > 0 && itemUid.Length > 0 && !state.RewardedItems.Contains(itemUid)
            && state.RewardedItems.Count < 512)
        {
            state.RewardedItems.Add(itemUid);
            if (day > state.ResearchEarnDay)
            { state.ResearchEarnDay = day; state.ResearchEarnedToday = 0; }
            if (day == state.ResearchEarnDay && state.ResearchEarnedToday < 3 && state.ResearchPoints < ResearchPointLimit)
            { state.ResearchEarnedToday++; state.ResearchPoints++; }
        }
        return true;
    }

    // Integration may explicitly invalidate tracked stock after an observed
    // merge/split. Never invent a proportional basis for an unknown new identity.
    public static void InvalidateLot(BusinessState state, string itemUid, string itemId)
    {
        var lot = state.Lots.FirstOrDefault(l => l.ItemUid == itemUid && l.ItemId == itemId);
        if (lot == null) return;
        lot.CostKnown = false; lot.Cost = 0;
    }

    // A native stack split changes object identity, not the merchandise's
    // first-trade eligibility. Carry only an already-consumed research identity;
    // this never awards points or transfers appraisal/profession milestones.
    public static bool InheritResearchIdentity(BusinessState state, string sourceUid, string childUid)
    {
        if (!Text(sourceUid, 80) || !Text(childUid, 80) || string.IsNullOrWhiteSpace(sourceUid)
            || string.IsNullOrWhiteSpace(childUid) || sourceUid != sourceUid.Trim() || childUid != childUid.Trim()
            || sourceUid.Any(char.IsControl) || childUid.Any(char.IsControl) || sourceUid == childUid
            || !state.RewardedItems.Contains(sourceUid) || state.RewardedItems.Contains(childUid)
            || state.RewardedItems.Count >= 512) return false;
        state.RewardedItems.Add(childUid);
        return true;
    }

    public static int CareerMetric(BusinessState state, int career = -1)
    {
        career = career < 0 ? state.Career : career;
        return career switch { 0 => state.UniqueClientIds.Count, 1 => state.ProfitableResaleCount,
            2 => state.PurchasedCategories.Count, _ => 0 };
    }
    private static int[] Thresholds(int career) => career switch {
        0 => new[] { 3, 8, 16 }, 1 => new[] { 2, 6, 15 }, 2 => new[] { 3, 5, 7 }, _ => Array.Empty<int>()
    };
    public static int Tier(BusinessState state, int career = -1)
    {
        career = career < 0 ? state.Career : career;
        int metric = CareerMetric(state, career);
        return Thresholds(career).Count(t => metric >= t);
    }
    public static string CareerProgress(BusinessState state, int career = -1)
    {
        career = career < 0 ? state.Career : career;
        if (career is < 0 or > 2) return "尚未选择经营专长";
        int metric = CareerMetric(state, career), tier = Tier(state, career);
        string goal = career switch { 0 => "不同成交顾客群", 1 => "已知成本盈利转售", _ => "不同采购品类" };
        return tier == 3 ? $"{goal} {metric} · 已达最高级" : $"{goal} {metric} / {Thresholds(career)[tier]}";
    }
    public static int Discount(BusinessState state) => Math.Clamp(Tier(state) * 5, 0, 15);
    public static int ServicePrice(BusinessState state, int basePrice) => basePrice <= 0 ? 0
        : Math.Max(1, checked((int)(((long)basePrice * (100 - Discount(state)) + 99) / 100)));

    public static bool CanSelectCareer(BusinessState state, int day, int career, out string reason)
    {
        if (career is < 0 or > 2 || day is < 0 or > 100000) { reason = "专长不可用"; return false; }
        if (state.Career == career) { reason = "已选择此专长"; return false; }
        if (Tier(state, career) == 0) { reason = "先完成真实交易条件：" + CareerProgress(state, career); return false; }
        if (state.Career >= 0 && day <= state.CareerSelectedDay) { reason = "下个游戏日才可更换专长"; return false; }
        reason = ""; return true;
    }
    public static bool SelectCareer(BusinessState state, int day, int career, out string reason)
    {
        if (!CanSelectCareer(state, day, career, out reason)) return false;
        state.Career = career; state.CareerSelectedDay = day; return true;
    }

    public static ResearchReport? FindReport(BusinessState state, int day, int company, int offset)
        => state.Reports.FirstOrDefault(r => r.Day == day && r.Company == company && r.Offset == offset);
    /// <summary>The changed story transition owns the one-shot reward. Full capacity archives overflow.</summary>
    public static int StoreResearchReward(BusinessState state, int gain)
    {
        CheckResearchCapacity(state);
        if (gain < 0) throw new ArgumentOutOfRangeException(nameof(gain));
        int held = Math.Min(gain, ResearchPointLimit - state.ResearchPoints);
        int archived = Math.Min(gain - held, ResearchRewardStorageLimit - state.StoredResearchPoints);
        state.ResearchPoints += held;
        state.StoredResearchPoints += archived;
        return held + archived;
    }
    /// <summary>Transfer earned archived material only after a new report actually spends a point.</summary>
    public static int UseStoredResearch(BusinessState state)
    {
        CheckResearchCapacity(state);
        int moved = Math.Min(state.StoredResearchPoints, ResearchPointLimit - state.ResearchPoints);
        state.ResearchPoints += moved;
        state.StoredResearchPoints -= moved;
        return moved;
    }
    private static void CheckResearchCapacity(BusinessState state)
    {
        if (state.ResearchPoints is < 0 or > ResearchPointLimit || state.StoredResearchPoints is < 0 or > ResearchRewardStorageLimit)
            throw new InvalidDataException("Invalid research capacity");
    }
    public static int DailyResearchLimit(BusinessState state) => ResearchDailyLimit + state.ResearchDailyBonus;
    public static int ResearchPointCost(BusinessState state, int day) => state.Career == 1 && Tier(state) >= 1
        && (state.ResearchDay != day || state.ResearchToday == 0) ? 0 : 1;
    public static bool CanResearch(BusinessState state, int day, out string reason)
    {
        if (day < 0 || day > 99993) { reason = "研究日期不可用"; return false; }
        if (day < state.ResearchDay) { reason = "研究日期早于已记录日期"; return false; }
        if (state.ResearchPoints < ResearchPointCost(state, day))
        { reason = "情报点不足，完成新物品的实物交易可积累"; return false; }
        if (day == state.ResearchDay && state.ResearchToday >= DailyResearchLimit(state))
        { reason = $"今日 {DailyResearchLimit(state)} 份研究报告已用完"; return false; }
        reason = ""; return true;
    }
    public static ResearchReport? CreateReport(BusinessState state, int seed, int day, int company, int offset)
    {
        if (company is < 0 or > 3 || offset is < 1 or > 7) return null;
        var existing = FindReport(state, day, company, offset);
        if (existing != null) return existing;
        if (!CanResearch(state, day, out _)) return null;
        int current = Economy.Price(seed, day, company), future = Economy.Price(seed, day + offset, company);
        uint noise = Economy.Hash(seed, day, 120 + company * 9 + offset);
        int uncertainty = 7 + offset * 3;
        int estimation = Math.Clamp(current + (int)Math.Round((future - current) * .65)
            + (int)(noise % (uint)(uncertainty * 2 + 1)) - uncertainty, 20, 2000);
        int spread = uncertainty + 8;
        var report = new ResearchReport { Day = day, Company = company, Offset = offset, TargetDay = day + offset,
            Low = Math.Max(20, estimation - spread), High = Math.Min(2000, estimation + spread),
            Signal = estimation > current + 4 ? "偏上行" : estimation < current - 4 ? "偏下行" : "震荡",
            // This describes model conviction, not a validated statistical win rate.
            Confidence = 50 + (int)(noise % 21) };
        int pointCost = ResearchPointCost(state, day);
        if (day > state.ResearchDay) { state.ResearchDay = day; state.ResearchToday = 0; }
        state.ResearchToday++; state.ResearchPoints -= pointCost;
        if (pointCost > 0) UseStoredResearch(state);
        state.Reports.Insert(0, report);
        if (state.Reports.Count > 16) state.Reports.RemoveRange(16, state.Reports.Count - 16);
        return report;
    }

    public static void Validate(BusinessState state)
    {
        state.UniqueClientIds ??= new(); state.PurchasedCategories ??= new();
        state.Trades ??= new(); state.Contacts ??= new(); state.Lots ??= new();
        state.SeenEvents ??= new(); state.RewardedItems ??= new(); state.Reports ??= new();
        state.KnownResaleItemKeys ??= new(); state.ProfitableResaleItemKeys ??= new();
        if (state.Schema != 1 || state.Career is < -1 or > 2 || state.CareerSelectedDay is < -1 or > 100000
            || state.ResearchPoints is < 0 or > 5 || state.StoredResearchPoints is < 0 or > ResearchRewardStorageLimit
            || state.ResearchEarnDay is < -1 or > 100000
            || state.ResearchEarnedToday is < 0 or > 3 || state.ResearchDay is < -1 or > 100000
            || state.ResearchDailyBonus is < 0 or > 1 || state.ResearchToday < 0
            || state.ResearchToday > DailyResearchLimit(state) || state.PurchaseCount < 0 || state.SaleCount < 0
            || state.KnownResaleCount < 0 || state.KnownResaleCount > state.SaleCount
            || state.ProfitableResaleCount < 0 || state.ProfitableResaleCount > state.KnownResaleCount
            || state.KnownResaleCount != state.KnownResaleItemKeys.Count
            || state.ProfitableResaleCount != state.ProfitableResaleItemKeys.Count
            || !Money(state.PurchaseAmount) || !Money(state.SaleAmount) || !Money(state.Turnover)
            || state.Turnover != state.PurchaseAmount + state.SaleAmount
            || state.Trades.Count > 60 || state.Contacts.Count > 32 || state.Lots.Count > 128
            || state.Reports.Count > 16) throw new InvalidDataException("Invalid business save data");
        CheckStrings(state.UniqueClientIds, 256, 120); CheckStrings(state.PurchasedCategories, 32, 100);
        CheckStrings(state.SeenEvents, 256, 300); CheckStrings(state.RewardedItems, 512, 80);
        CheckStrings(state.KnownResaleItemKeys, 512, 220); CheckStrings(state.ProfitableResaleItemKeys, 512, 220);
        if (state.ProfitableResaleItemKeys.Any(key => !state.KnownResaleItemKeys.Contains(key)))
            throw new InvalidDataException("Unknown profitable resale identity");
        foreach (var trade in state.Trades)
        {
            if (trade == null || trade.Day is < 0 or > 100000 || trade.Units is < 1 or > 1000000 || trade.Amount < 0
                || !Text(trade.ItemUid, 80) || !Text(trade.ItemId, 120) || trade.ItemId.Length == 0
                || !Text(trade.ItemName, 160) || !Text(trade.Category, 100) || !Text(trade.CustomerId, 120)
                || !Text(trade.PublicName, 160) || (trade.KnownCost.HasValue && !Money(trade.KnownCost.Value))
                || (trade.Purchase && (trade.KnownCost != null || trade.Profit != null))
                || (trade.KnownCost.HasValue != trade.Profit.HasValue)
                || (trade.KnownCost.HasValue && trade.Profit != trade.Amount - trade.KnownCost.Value))
                throw new InvalidDataException("Invalid business trade");
        }
        if (state.Contacts.Select(c => c?.CustomerId).Distinct().Count() != state.Contacts.Count)
            throw new InvalidDataException("Duplicate business contacts");
        foreach (var contact in state.Contacts)
        {
            if (contact == null || !Text(contact.CustomerId, 120) || contact.CustomerId.Length == 0
                || !Text(contact.PublicName, 160) || contact.FirstDay < 0 || contact.LastDay < contact.FirstDay
                || contact.LastDay > 100000 || contact.Purchases < 0 || contact.Sales < 0 || !Money(contact.Turnover))
                throw new InvalidDataException("Invalid business contact");
            contact.Categories ??= new(); CheckStrings(contact.Categories, 16, 100);
        }
        if (state.Lots.Select(l => l == null ? "" : l.ItemUid + "\0" + l.ItemId).Distinct().Count() != state.Lots.Count)
            throw new InvalidDataException("Duplicate business stock lots");
        foreach (var lot in state.Lots)
            if (lot == null || !Text(lot.ItemUid, 80) || lot.ItemUid.Length == 0 || !Text(lot.ItemId, 120)
                || lot.ItemId.Length == 0 || lot.Units < 1 || lot.Day is < 0 or > 100000 || !Money(lot.Cost)
                || (!lot.CostKnown && lot.Cost != 0)) throw new InvalidDataException("Invalid business stock lot");
        if (state.Reports.Select(r => r == null ? "" : $"{r.Day}:{r.Company}:{r.Offset}").Distinct().Count() != state.Reports.Count)
            throw new InvalidDataException("Duplicate research reports");
        foreach (var report in state.Reports)
            if (report == null || report.Day is < 0 or > 99993 || report.Company is < 0 or > 3
                || report.Offset is < 1 or > 7 || report.TargetDay != report.Day + report.Offset
                || report.Low < 20 || report.High > 2000 || report.High <= report.Low
                || report.Confidence is < 50 or > 70 || (report.Signal != "偏上行" && report.Signal != "偏下行" && report.Signal != "震荡"))
                throw new InvalidDataException("Invalid research report");
    }

    private static bool Money(long amount) => amount is >= 0 and <= MoneyLimit;
    private static bool Text(string? value, int max) => value != null && value.Length <= max && !value.Contains('\0');
    private static string Clean(string? value, int max)
    {
        value = (value ?? "").Replace("\0", "").Trim();
        return value.Length > max ? value[..max] : value;
    }
    private static void CheckStrings(List<string> list, int maxCount, int maxLength)
    {
        if (list.Count > maxCount || list.Any(s => !Text(s, maxLength) || s.Length == 0)
            || list.Distinct().Count() != list.Count) throw new InvalidDataException("Invalid business identifier list");
    }
    private static void TrimOldest<T>(List<T> list, int count)
    { if (list.Count > count) list.RemoveRange(0, list.Count - count); }
}
