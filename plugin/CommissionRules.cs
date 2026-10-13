using System.Text.Json;

namespace SpacePhone;

// These rules only observe confirmed merchandise payments. The native caller owns
// the current run, StoreClient pointer, negotiation, merchandise and wallet.
public enum CommissionStatus
{
    Unaccepted = 0,
    Accepted = 1,
    Deferred = 2,
    Declined = 3,
    Completed = 4,
    Expired = 5
}

public sealed class CommissionBook
{
    public int Version { get; set; } = 1;
    public CommissionEntry[] Entries { get; set; } = NewEntries();
    public List<string> SeenEvents { get; set; } = new();
    internal static CommissionEntry[] NewEntries() => new[] { new CommissionEntry(), new CommissionEntry(), new CommissionEntry() };
}

public sealed class CommissionEntry
{
    public CommissionStatus Status { get; set; }
    public string ItemId { get; set; } = "";
    public string ItemName { get; set; } = "";
    public int RequiredUnits { get; set; }
    public int DeliveredUnits { get; set; }
    public int AvailableDay { get; set; }
    public int AcceptedDay { get; set; } = -1;
    public int Deadline { get; set; } = -1;
    public bool OfferDeferred { get; set; }
    public bool AcceptedDeferred { get; set; }
    public bool Rewarded { get; set; }
    public bool PendingResearch { get; set; }
    public string DeliveryToken { get; set; } = "";
    public int QueuedStage { get; set; } = -1;
}

public readonly record struct CommissionChoice(string Label, int Key);
public readonly record struct CommissionReply(bool Changed, int ResearchGain, string Message);

public static class CommissionRules
{
    public const int MaxDay = 100000;
    public const int EventLimit = 32;
    private static readonly string[] Names = { "雨鸦", "阿岚", "洛克" };

    public static string GetName(int index) => Index(index) ? Names[index] : "未知委托人";

    /// <summary>Configure once, using a verified native merchandise definition. Never rerolls or rebinds a request.</summary>
    public static bool Configure(CommissionBook book, int index, string itemId, string itemName, int units, int day = 0)
    {
        if (!TryEntry(book, index, out var entry) || entry.ItemId.Length != 0 || !Day(day)
            || !Text(itemId, 120) || !Text(itemName, 160) || units is < 1 or > 3) return false;
        entry.ItemId = itemId;
        entry.ItemName = itemName;
        entry.RequiredUnits = units;
        entry.AvailableDay = day;
        return true;
    }

    public static int Remaining(CommissionBook book, int index) =>
        TryEntry(book, index, out var entry) && entry.ItemId.Length > 0 ? entry.RequiredUnits - entry.DeliveredUnits : 0;

    public static string GetRequest(CommissionBook book, int index) =>
        TryEntry(book, index, out var entry) && entry.ItemId.Length > 0
            ? $"{entry.ItemName} × {entry.RequiredUnits} · 已交付 {entry.DeliveredUnits}/{entry.RequiredUnits}"
            : "尚未找到合适的实物，继续正常营业。";

    public static string GetMessage(CommissionBook book, int index, int day)
    {
        if (!TryEntry(book, index, out var entry) || !Day(day)) return "委托资料暂不可用。";
        if (entry.ItemId.Length == 0) return GetRequest(book, index);
        string goods = GetRequest(book, index);
        return entry.Status switch
        {
            CommissionStatus.Unaccepted => goods + "\n在柜台选择接下、明天再谈或婉拒；不会自动扣款或锁走货物。",
            CommissionStatus.Deferred => goods + (day < entry.AvailableDay
                ? $"\n第 {entry.AvailableDay} 天再到柜台谈，之后只能接下或婉拒。"
                : "\n可以再到柜台谈了，选择接下或婉拒。"),
            CommissionStatus.Accepted when day <= entry.Deadline => goods +
                $"\n第 {entry.Deadline} 天营业结束前完成。备齐后预约本人来买，照常议价、成交。"
                + (!entry.AcceptedDeferred && entry.Deadline < MaxDay ? "\n需要更多时间时，可以在柜台申请延长一天。" : ""),
            CommissionStatus.Accepted => goods + "\n期限已过，委托结束；货物仍属于你。",
            CommissionStatus.Declined => goods + "\n已婉拒，委托结束；继续正常做生意。",
            CommissionStatus.Completed => goods + (entry.PendingResearch
                ? "\n委托已完成，1 点研究资料等待归档；资料有空位后自动补入，只结算一次。"
                : "\n实物已成交，1 点研究资料只结算一次。"),
            _ => goods + "\n期限已过，委托结束；货物仍属于你。"
        };
    }

    // Choices only describe valid actions; drawing the UI never changes a contract.
    public static CommissionChoice[] Choices(CommissionBook book, int index, int day)
    {
        if (!TryEntry(book, index, out var entry) || entry.ItemId.Length == 0 || !Day(day)
            || day < entry.AvailableDay) return Array.Empty<CommissionChoice>();
        var choices = new List<CommissionChoice>();
        if (entry.Status is CommissionStatus.Unaccepted or CommissionStatus.Deferred)
        {
            choices.Add(new CommissionChoice("接下委托", 0));
            if (!entry.OfferDeferred && day < MaxDay) choices.Add(new CommissionChoice("明天再谈", 1));
            choices.Add(new CommissionChoice("这次婉拒", 2));
        }
        else if (entry.Status == CommissionStatus.Accepted && day >= entry.AcceptedDay && day <= entry.Deadline)
        {
            if (!entry.AcceptedDeferred && entry.Deadline < MaxDay) choices.Add(new CommissionChoice("延长一天", 1));
            choices.Add(new CommissionChoice("放弃委托", 2));
        }
        return choices.ToArray();
    }

    public static CommissionReply Reply(CommissionBook book, int index, int choice, int day)
    {
        if (!TryEntry(book, index, out var entry) || !Choices(book, index, day).Any(c => c.Key == choice))
            return Reject("这条选择暂时不可用，请查看当前委托指引。");
        entry.QueuedStage = -1;
        if (choice == 2)
        {
            entry.Status = CommissionStatus.Declined;
            return Changed("已婉拒这份委托，现有货物和资金保持原样。");
        }
        if (choice == 0)
        {
            entry.Status = CommissionStatus.Accepted;
            entry.AcceptedDay = day;
            entry.Deadline = Math.Min(MaxDay, day + 2);
            return Changed($"已接下 {entry.ItemName} × {entry.RequiredUnits}，第 {entry.Deadline} 天结束前交付。备货后预约本人来柜台买。");
        }
        if (entry.Status == CommissionStatus.Accepted)
        {
            entry.AcceptedDeferred = true;
            entry.Deadline++;
            return Changed($"已延长一天，请在第 {entry.Deadline} 天结束前成交。这份委托不能再次延长。");
        }
        entry.OfferDeferred = true;
        entry.Status = CommissionStatus.Deferred;
        entry.AvailableDay = day + 1;
        return Changed($"已约到第 {entry.AvailableDay} 天再谈，届时可以接下或婉拒。");
    }

    /// <summary>Bind the exact native buyer ID suffix once. The current release uses token "1" for delivery stage 1.</summary>
    public static bool BindDelivery(CommissionBook book, int index, string deliveryToken)
    {
        if (!TryEntry(book, index, out var entry) || entry.Status != CommissionStatus.Accepted
            || !Token(deliveryToken) || entry.DeliveryToken.Length > 0) return false;
        entry.DeliveryToken = deliveryToken;
        return true;
    }

    public static string ClientId(CommissionBook book, int index) =>
        TryEntry(book, index, out var entry) && entry.DeliveryToken.Length > 0
            ? $"spacephone.commission.{index}.{entry.DeliveryToken}" : "";

    public static bool CanDeliver(CommissionBook book, int index, int day) =>
        TryEntry(book, index, out var entry) && entry.Status == CommissionStatus.Accepted
        && entry.DeliveryToken.Length > 0 && Day(day) && day >= entry.AcceptedDay && day <= entry.Deadline;

    /// <summary>Call only after the current native commission BUY client actually paid for the matching merchandise.</summary>
    public static CommissionReply RecordPayment(CommissionBook book, int index, int day, string clientId,
        string itemId, int units, int actualAmount, string eventKey)
    {
        if (!TryEntry(book, index, out var entry) || !CanDeliver(book, index, day)
            || clientId != ClientId(book, index) || itemId != entry.ItemId
            || units < 1 || units > entry.RequiredUnits - entry.DeliveredUnits || actualAmount <= 0
            || !Text(eventKey, 300) || book.SeenEvents.Contains(eventKey) || book.SeenEvents.Count >= EventLimit)
            return Reject("这笔交易不符合当前委托，委托进度未变化。");
        book.SeenEvents.Add(eventKey);
        entry.DeliveredUnits += units;
        entry.QueuedStage = -1;
        if (entry.DeliveredUnits != entry.RequiredUnits)
            return Changed($"已确认 {entry.ItemName} × {units} 的真实成交，还差 {entry.RequiredUnits - entry.DeliveredUnits} 件。");
        // Completion and reward consumption share one atomic save transition.
        entry.Status = CommissionStatus.Completed;
        entry.Rewarded = true;
        entry.PendingResearch = true;
        return new CommissionReply(true, 1, "委托完成，实际成交款由柜台结算，1 点额外研究资料等待归档。");
    }

    /// <summary>Call on observed game-day changes. Deadline day is still valid; only the following day expires.</summary>
    public static bool Settle(CommissionBook book, int day)
    {
        if (!BookShape(book) || !Day(day)) return false;
        bool changed = false;
        for (int i = 0; i < 3; i++)
        {
            if (!TryEntry(book, i, out var entry) || entry.Status != CommissionStatus.Accepted || day <= entry.Deadline) continue;
            entry.Status = CommissionStatus.Expired;
            entry.QueuedStage = -1;
            changed = true;
        }
        return changed;
    }

    public static string Encode(CommissionBook book) => JsonSerializer.Serialize(Validate(book));
    public static CommissionBook Decode(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Empty commission data");
        try { return Validate(JsonSerializer.Deserialize<CommissionBook>(json) ?? throw new InvalidDataException("Empty commission data")); }
        catch (JsonException exception) { throw new InvalidDataException("Invalid commission JSON", exception); }
    }

    // A missing new book/list has a safe old-save default. Contradictory existing
    // contracts are rejected instead of resetting an earned reward into a new offer.
    public static CommissionBook Validate(CommissionBook? book)
    {
        book ??= new CommissionBook();
        book.Entries ??= CommissionBook.NewEntries();
        book.SeenEvents ??= new();
        if (!BookShape(book) || book.SeenEvents.Count > EventLimit
            || book.SeenEvents.Any(key => !Text(key, 300)) || book.SeenEvents.Distinct(StringComparer.Ordinal).Count() != book.SeenEvents.Count)
            throw new InvalidDataException("Invalid commission book");
        for (int i = 0; i < 3; i++)
        {
            var entry = book.Entries[i];
            if (!EntryValid(entry)) throw new InvalidDataException("Invalid commission entry " + i);
            if (entry.Status == CommissionStatus.Completed) entry.Rewarded = true;
            if (entry.Status is CommissionStatus.Completed or CommissionStatus.Declined or CommissionStatus.Expired)
                entry.QueuedStage = -1;
        }
        return book;
    }

    private static bool TryEntry(CommissionBook? book, int index, out CommissionEntry entry)
    {
        entry = null!;
        if (!Index(index) || !BookShape(book) || !EntryValid(book!.Entries[index])
            || book.SeenEvents.Count > EventLimit || book.SeenEvents.Any(key => !Text(key, 300))
            || book.SeenEvents.Distinct(StringComparer.Ordinal).Count() != book.SeenEvents.Count) return false;
        entry = book.Entries[index];
        return true;
    }

    private static bool BookShape(CommissionBook? book) => book != null && book.Version == 1
        && book.Entries != null && book.Entries.Length == 3 && book.SeenEvents != null;

    private static bool EntryValid(CommissionEntry? entry)
    {
        if (entry == null || !Enum.IsDefined(typeof(CommissionStatus), entry.Status) || !Day(entry.AvailableDay)
            || entry.QueuedStage is < -1 or > 1 || entry.DeliveredUnits < 0
            || !EmptyOrText(entry.DeliveryToken, 80) || (entry.DeliveryToken.Length > 0 && !Token(entry.DeliveryToken))) return false;
        if (entry.ItemId == "") return entry.ItemName == "" && entry.RequiredUnits == 0 && entry.DeliveredUnits == 0
            && entry.Status == CommissionStatus.Unaccepted && entry.AcceptedDay == -1 && entry.Deadline == -1
            && !entry.OfferDeferred && !entry.AcceptedDeferred && !entry.Rewarded && !entry.PendingResearch && entry.DeliveryToken == "" && entry.QueuedStage == -1;
        if (!Text(entry.ItemId, 120) || !Text(entry.ItemName, 160) || entry.RequiredUnits is < 1 or > 3
            || entry.DeliveredUnits > entry.RequiredUnits) return false;
        bool accepted = entry.AcceptedDay >= 0;
        if (!accepted)
        {
            if (entry.AcceptedDay != -1 || entry.Deadline != -1 || entry.DeliveredUnits != 0 || entry.AcceptedDeferred
                || entry.DeliveryToken.Length != 0 || entry.Rewarded || entry.PendingResearch) return false;
            return entry.Status switch
            {
                CommissionStatus.Unaccepted => !entry.OfferDeferred,
                CommissionStatus.Deferred => entry.OfferDeferred && entry.AvailableDay > 0,
                CommissionStatus.Declined => true,
                _ => false
            };
        }
        if (!Day(entry.AcceptedDay) || entry.AcceptedDay < entry.AvailableDay || !Day(entry.Deadline)
            || entry.Deadline != Math.Min(MaxDay, entry.AcceptedDay + 2 + (entry.AcceptedDeferred ? 1 : 0))) return false;
        if (entry.Status == CommissionStatus.Completed)
            return entry.DeliveredUnits == entry.RequiredUnits && entry.DeliveryToken.Length > 0;
        if (entry.Status is not (CommissionStatus.Accepted or CommissionStatus.Declined or CommissionStatus.Expired)
            || entry.DeliveredUnits >= entry.RequiredUnits || entry.Rewarded || entry.PendingResearch) return false;
        return entry.DeliveredUnits == 0 || entry.DeliveryToken.Length > 0;
    }

    private static bool Index(int index) => index is >= 0 and < 3;
    private static bool Day(int day) => day is >= 0 and <= MaxDay;
    private static bool EmptyOrText(string? value, int max) => value == "" || Text(value, max);
    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max
        && value == value.Trim() && !value.Any(char.IsControl);
    private static bool Token(string? value) => Text(value, 80) && value!.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');
    private static CommissionReply Reject(string message) => new(false, 0, message);
    private static CommissionReply Changed(string message) => new(true, 0, message);
}
