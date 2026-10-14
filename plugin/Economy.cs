using System.Text.Json;
namespace SpacePhone;

public sealed class PhoneState
{
    public int Schema { get; set; } = 1;
    public int Seed { get; set; } = Random.Shared.Next(1, int.MaxValue);
    public int Debt { get; set; }
    public int InterestDay { get; set; }
    public int[] Shares { get; set; } = new int[4];
    public int TicketSerial { get; set; }
    public List<Ticket> Tickets { get; set; } = new();
    public int PendingPrize { get; set; }
    public string LotteryResult { get; set; } = "还没有开奖记录";
    public List<int> LikedVideos { get; set; } = new();
    public long[] CostBasis { get; set; } = new long[4];
    public long RealizedProfit { get; set; }
    public List<LedgerEntry> Ledger { get; set; } = new();
    public int PendingSupplier { get; set; } = -1;
    public float MusicVolume { get; set; } = .25f;
    public float UiScale { get; set; } = 1f;
    public bool ClickSounds { get; set; } = true;
    public BusinessState Business { get; set; } = new();
    public StoryBook Stories { get; set; } = new();
    public CommissionBook Commissions { get; set; } = new();
    public FollowupBook Followups { get; set; } = new();
    public LostCallBook LostCall { get; set; } = new();
    public NightOrderBook NightOrders { get; set; } = new();
    public int SupplierCallDay { get; set; } = -1;
    public int SupplierCallsToday { get; set; }
    public int RecommendedDay { get; set; } = -1;
    public bool PendingRecommendation { get; set; }
    public int PendingSupplierFeeCap { get; set; }
    public int PendingRecommendationFeeCap { get; set; }
    public int ActiveStory { get; set; } = -1;
}
public sealed class LedgerEntry
{
    public int Day { get; set; }
    public string Title { get; set; } = "";
    public int Amount { get; set; }
}
public readonly record struct Candle(int Open,int High,int Low,int Close,int Volume);
public sealed class Ticket
{
    public int Day { get; set; }
    public int Number { get; set; }
}
public static class Economy
{
    public static readonly string[] Companies = { "红帝物流", "轨道矿业", "霓虹生物", "空间站能源" };
    public static uint Hash(int seed, int day, int index)
    {
        unchecked {
            uint h = (uint)seed ^ ((uint)day * 0x9e3779b9u) ^ ((uint)(index + 1) * 0x85ebca6bu);
            h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; return h ^ (h >> 16);
        }
    }
    public static int Price(int seed, int day, int company)
    {
        // Per-run deterministic fictional market: opening the app cannot reroll prices.
        int p = 80 + company * 45;
        for (int d = 0; d <= Math.Clamp(day, 0, 100000); d++)
            p = Math.Clamp(p + (int)(Hash(seed, d, company) % 31) - 15, 20, 2000);
        return p;
    }
    public static int Prize(int seed, int serial)
    {
        uint roll = Hash(seed, serial, 77) % 1000;
        return roll == 0 ? 2000 : roll < 10 ? 200 : roll < 70 ? 50 : roll < 300 ? 10 : 0;
    }
    public static readonly string[] Tickers={"RIMP","OREX","NOVA","GRID"};
    public static Candle Bar(int seed,int day,int company)
    {
        int close=Price(seed,day,company),open=Price(seed,Math.Max(0,day-1),company);
        int range=2+(int)(Hash(seed,day,company+20)%12);
        return new Candle(open,Math.Max(open,close)+range,Math.Max(1,Math.Min(open,close)-range),close,800+(int)(Hash(seed,day,company+60)%9000));
    }
    public static int Fee(int gross)=>Math.Max(1,(int)Math.Ceiling(gross*.002));
    public static void Record(PhoneState state,int day,string title,int amount)
    {
        state.Ledger.Insert(0,new LedgerEntry{Day=day,Title=title,Amount=amount});
        if(state.Ledger.Count>40)state.Ledger.RemoveRange(40,state.Ledger.Count-40);
    }
    public static bool Settle(PhoneState state, int day)
    {
        bool changed = false;
        if (day > state.InterestDay)
        {
            // Simple daily interest, settled once for each elapsed game day.
            int elapsed = day - state.InterestDay;
            long interest = (long)Math.Ceiling(state.Debt * 0.01) * elapsed;
            state.Debt = (int)Math.Min(100000000, state.Debt + interest);
            state.InterestDay = day;
            changed = true;
        }
        foreach (var ticket in state.Tickets.Where(t => t.Day < day).ToArray())
        {
            int prize = Prize(state.Seed, ticket.Number);
            state.PendingPrize = checked(state.PendingPrize + prize);
            state.LotteryResult = $"票号 {ticket.Number:D6}：" + (prize > 0 ? $"中奖 {prize}" : "未中奖");
            state.Tickets.Remove(ticket);
            changed = true;
        }
        return changed;
    }
    public static string Encode(PhoneState s) => JsonSerializer.Serialize(s);
    public static PhoneState Decode(string json)
    {
        using var document=JsonDocument.Parse(json);
        if(document.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid phone save object");
        string? followupJson=document.RootElement.TryGetProperty(nameof(PhoneState.Followups),out var followup)?followup.GetRawText():null;
        string? lostCallJson=document.RootElement.TryGetProperty(nameof(PhoneState.LostCall),out var lostCall)?lostCall.GetRawText():null;
        string? nightOrdersJson=document.RootElement.TryGetProperty(nameof(PhoneState.NightOrders),out var nightOrders)?nightOrders.GetRawText():null;
        // An additive chapter cannot prevent the original phone from loading when its JSON types are damaged.
        using var payload=new MemoryStream();
        using(var writer=new Utf8JsonWriter(payload))
        {
            writer.WriteStartObject();
            foreach(var property in document.RootElement.EnumerateObject())
                if(property.Name!=nameof(PhoneState.Followups)&&property.Name!=nameof(PhoneState.LostCall)&&property.Name!=nameof(PhoneState.NightOrders))property.WriteTo(writer);
            writer.WriteEndObject();
        }
        var state = JsonSerializer.Deserialize<PhoneState>(payload.ToArray()) ?? throw new InvalidDataException("Empty phone data");
        state.Followups=followupJson==null?new FollowupBook():FollowupRules.Decode(followupJson);
        state.LostCall=lostCallJson==null?new LostCallBook():LostCallRules.Decode(lostCallJson);
        state.NightOrders=nightOrdersJson==null?new NightOrderBook():NightOrderRules.Decode(nightOrdersJson);
        state.LikedVideos ??= new();
        state.Ledger ??= new();
        state.CostBasis ??= new long[4];
        state.Business ??= new();BusinessRules.Validate(state.Business);state.Stories=StoryRules.Validate(state.Stories);
        state.Commissions=CommissionRules.Validate(state.Commissions);
        state.Followups=FollowupRules.Validate(state.Followups);
        state.LostCall=LostCallRules.Validate(state.LostCall);
        state.NightOrders=NightOrderRules.Validate(state.NightOrders);
        if(state.ActiveStory is < -1 or > 2)state.ActiveStory=-1;
        if(state.SupplierCallDay < -1 || state.SupplierCallsToday < 0 || state.SupplierCallsToday >100000 || state.RecommendedDay < -1 || state.PendingSupplierFeeCap is <0 or >20 || state.PendingRecommendationFeeCap is <0 or >40)throw new InvalidDataException("Invalid phone schedule");
        if(state.CostBasis.Length!=4 || state.CostBasis.Any(x=>x<0) || state.Ledger.Count>40)throw new InvalidDataException("Invalid ledger or portfolio data");
        state.MusicVolume=Math.Clamp(state.MusicVolume,0,1);
        state.UiScale=Math.Clamp(state.UiScale,.8f,1.15f);
        if(state.PendingSupplier < -1 || state.PendingSupplier>3)state.PendingSupplier=-1;
        if (state.Schema != 1 || state.Shares == null || state.Shares.Length != 4 || state.Shares.Any(x => x < 0 || x > 1000000)
            || state.Debt < 0 || state.Debt > 100000000 || state.Tickets == null || state.Tickets.Count > 100
            || state.PendingPrize < 0 || state.TicketSerial < 0 || state.InterestDay < 0)
            throw new InvalidDataException("Invalid phone save data");
        return state;
    }
}
