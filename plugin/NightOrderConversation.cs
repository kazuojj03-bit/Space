namespace SpacePhone;

/// <summary>
/// Original night-shift customers. Text only: these methods never accept an order,
/// transfer goods, charge money, change a deadline or award shop progress.
/// </summary>
public static class NightOrderConversation
{
    public const int VariantCount = 3;

    public static string Name(NightOrderBuyerKind kind) => kind switch
    {
        NightOrderBuyerKind.Warehouse => "顾砚",
        NightOrderBuyerKind.Canteen => "宋葵",
        NightOrderBuyerKind.Repair => "许桥",
        _ => "夜班买方"
    };
    public static string Name(int buyerIndex) => Name((NightOrderBuyerKind)buyerIndex);

    public static string Role(NightOrderBuyerKind kind) => kind switch
    {
        NightOrderBuyerKind.Warehouse => "港仓验货员",
        NightOrderBuyerKind.Canteen => "夜班食堂领班",
        NightOrderBuyerKind.Repair => "巡修工",
        _ => "夜班买方"
    };

    private static readonly string[][] WarehouseOpenings =
    {
        new[] {
            "顾砚把帽沿的雨拍在门外，收货单却干干净净。",
            "“给港仓值班室找旧东西。箱子都有编号，只有我们坐哪儿一直没定。”"
        },
        new[] {
            "顾砚掏出短铅笔，写完名字，又把笔夹回耳后。",
            "“值班室最近腾了点地方。先添能用的旧货，空房间看着总像等人搬走。”"
        },
        new[] {
            "顾砚擦了擦手才摸柜台，袖口留着一圈没洗掉的粉笔灰。",
            "“今天替轮班的人跑一趟。东西要能用，表面旧点没事，我们也没多新。”"
        }
    };
    private static readonly string[][] CanteenOpenings =
    {
        new[] {
            "宋葵把围裙折好塞进包里，包角还露出半张菜单。",
            "“给食堂夜班带点普通补给。人轮着歇，补给也得留够，别等到换班只剩空箱。”"
        },
        new[] {
            "宋葵递来收货单，上面有个被划掉的‘加辣’。",
            "“这张纸用反了。今天买轮班补给，不是让你替后厨再加一道大菜。”"
        },
        new[] {
            "宋葵看了一眼钟，把包靠在柜台边，终于松了口气。",
            "“忙的时候大家轮着歇。补给简单点就行，别让晚来的人只领到一句‘都用完了’。”"
        }
    };
    private static readonly string[][] RepairOpenings =
    {
        new[] {
            "许桥把工具包放在脚边，先确认它没挡住别人进门。",
            "“给巡修班备点普通维修物料。不是全新的也行，合用比包装亮堂重要。”"
        },
        new[] {
            "许桥摘下手套，用没沾机油的那一面压住收货单。",
            "“同事说别再拿临时办法凑。我说得先找到正经东西，才好告别临时。”"
        },
        new[] {
            "许桥弯腰系好鞋带，起身时顺手接住滑下肩的背带。",
            "“跑一晚上，最后还得回巡修屋补东西。备好常用物料，少翻一次我的包。”"
        }
    };

    /// <summary>The same saved day and order number always select the same small incident.</summary>
    public static string[] Opening(NightOrderBuyerKind kind, int day, int orderNumber,
        string itemName, int requiredUnits, long proposedTotalBudget)
    {
        if (!Known(kind) || requiredUnits <= 0 || proposedTotalBudget <= 0 || string.IsNullOrWhiteSpace(itemName))
            return Array.Empty<string>();
        var pair = Intro(kind, day, orderNumber);
        return new[] { pair[0], pair[1],
            $"“要{ShortItem(itemName)}×{requiredUnits}，总预算{proposedTotalBudget}。摆上台照常谈，没谈妥前不定价。”" };
    }
    public static string[] Opening(int buyerIndex, int day, int orderNumber,
        string itemName, int requiredUnits, long proposedTotalBudget) =>
        Opening((NightOrderBuyerKind)buyerIndex, day, orderNumber, itemName, requiredUnits, proposedTotalBudget);

    /// <summary>A generic arrival needs no inventory guess and makes no payment promise.</summary>
    public static string[] Opening(NightOrderBuyerKind kind, int day, int orderNumber)
    {
        if (!Known(kind)) return Array.Empty<string>();
        var pair = Intro(kind, day, orderNumber);
        return new[] { pair[0], pair[1], "“收货单在手机里。东西摆上柜台再谈，预算不等于已经报好的价。”" };
    }

    // Call only after the caller has observed a genuine paid counter sale.
    public static string[] Partial(NightOrderBuyerKind kind, int deliveredUnits, int requiredUnits, long actualPaidTotal)
    {
        if (!Known(kind) || deliveredUnits <= 0 || deliveredUnits >= requiredUnits || actualPaidTotal <= 0)
            return Array.Empty<string>();
        string consequence = kind switch
        {
            NightOrderBuyerKind.Warehouse => "“这批先放值班室。剩下的等你有货，纸上记清楚就行。”",
            NightOrderBuyerKind.Canteen => "“这批先给值班的人用。剩下的留着，不让你为了凑数忙一整晚。”",
            NightOrderBuyerKind.Repair => "“这批先放巡修屋。剩下的等你有货，我也不把每天巡一遍叫作催单。”",
            _ => ""
        };
        return new[] { $"“已收{deliveredUnits}/{requiredUnits}，这张单累计付了{actualPaidTotal}现金。”", consequence };
    }
    public static string[] Partial(int buyerIndex, int deliveredUnits, int requiredUnits, long actualPaidTotal) =>
        Partial((NightOrderBuyerKind)buyerIndex, deliveredUnits, requiredUnits, actualPaidTotal);

    public static string[] Completed(NightOrderBuyerKind kind, int requiredUnits, long actualPaidTotal,
        int day, int orderNumber)
    {
        if (!Known(kind) || requiredUnits <= 0 || actualPaidTotal <= 0) return Array.Empty<string>();
        string[] endings = kind switch
        {
            NightOrderBuyerKind.Warehouse => WarehouseEndings,
            NightOrderBuyerKind.Canteen => CanteenEndings,
            NightOrderBuyerKind.Repair => RepairEndings,
            _ => Array.Empty<string>()
        };
        return new[] { $"“{requiredUnits}件都收齐了，这张单实际付了{actualPaidTotal}现金。收据收好。”",
            endings[Variant(day, orderNumber)] };
    }
    public static string[] Completed(int buyerIndex, int requiredUnits, long actualPaidTotal, int day, int orderNumber) =>
        Completed((NightOrderBuyerKind)buyerIndex, requiredUnits, actualPaidTotal, day, orderNumber);

    private static readonly string[] WarehouseEndings =
    {
        "“值班室总算能添齐了。新来的还想给旧货编号，我让他先把自己杯子找回来。”",
        "“今晚轮到小葛值班。他说空地方终于不那么像走错了楼层。”",
        "“我把单子带回去，省得换班的人又问是谁买的。旧东西也值得有个来路。”"
    };
    private static readonly string[] CanteenEndings =
    {
        "“这批轮班补给收齐了。大家说领取也得排班，我说先拿，别又开会。”",
        "“我把收据夹进本子。要不明早有人准会把‘补给收齐’看成‘加一道菜’。”",
        "“小秦主动说今晚收尾。他平时最会躲收尾，看来真想先领完补给歇着。”"
    };
    private static readonly string[] RepairEndings =
    {
        "“这批物料终于收齐了。回去先定好位置，别让大家又凭记忆翻我的包。”",
        "“老陈说这回总算备齐了。上回他也这么说，第二天又来问我借东西。”",
        "“先把这张收据留好。下次有人夸东西好用，我也能少解释两句。”"
    };

    public static string[] Receipt(NightOrderBuyerKind kind, int deliveredUnits, int requiredUnits,
        long actualPaidTotal, bool complete, int day = 0, int orderNumber = 0)
    {
        if (complete) return deliveredUnits == requiredUnits
            ? Completed(kind, requiredUnits, actualPaidTotal, day, orderNumber) : Array.Empty<string>();
        return Partial(kind, deliveredUnits, requiredUnits, actualPaidTotal);
    }

    /// <summary>Read an archived outcome using its saved closure day, never the day the phone is reopened.</summary>
    public static string[] Receipt(NightOrderReceipt? receipt)
    {
        if (receipt?.Offer == null || !Known(receipt.Offer.BuyerKind) || receipt.Offer.RequiredUnits <= 0
            || receipt.DeliveredUnits < 0 || receipt.DeliveredUnits > receipt.Offer.RequiredUnits
            || receipt.Offer.ProposedTotalBudget <= 0 || receipt.ActualPaidTotal < 0
            || receipt.ActualPaidTotal > receipt.Offer.ProposedTotalBudget
            || !NightOrderRules.ValidDay(receipt.AcceptedDay) || !NightOrderRules.ValidDay(receipt.ClosedDay)
            || receipt.ClosedDay < receipt.AcceptedDay || receipt.Offer.Ordinal <= 0)
            return Array.Empty<string>();
        switch (receipt.Result)
        {
            case NightOrderResult.Completed:
                return receipt.DeliveredUnits == receipt.Offer.RequiredUnits && receipt.ActualPaidTotal > 0
                    ? Completed(receipt.Offer.BuyerKind, receipt.Offer.RequiredUnits, receipt.ActualPaidTotal,
                        receipt.ClosedDay, receipt.Offer.Ordinal) : Array.Empty<string>();
            case NightOrderResult.PartialClosed:
                return receipt.DeliveredUnits > 0 && receipt.DeliveredUnits < receipt.Offer.RequiredUnits
                    && receipt.ActualPaidTotal == receipt.Offer.ProposedTotalBudget
                    ? PartialClosed(new NightOrderActive { Offer = receipt.Offer,
                        DeliveredUnits = receipt.DeliveredUnits, ActualPaidTotal = receipt.ActualPaidTotal })
                    : Array.Empty<string>();
            case NightOrderResult.Cancelled:
                if (receipt.DeliveredUnits >= receipt.Offer.RequiredUnits
                    || (receipt.DeliveredUnits == 0) != (receipt.ActualPaidTotal == 0))
                    return Array.Empty<string>();
                string status = receipt.DeliveredUnits == 0
                    ? "“这张单取消了，没有成交或付款。未交的货仍归你。”"
                    : $"“已收{receipt.DeliveredUnits}/{receipt.Offer.RequiredUnits}，共付{receipt.ActualPaidTotal}现金。这部分成交照实保留。”";
                string goodbye = receipt.Offer.BuyerKind switch
                {
                    NightOrderBuyerKind.Warehouse => "“剩下的需求取消，不记你欠货。我另找合适的，咱们照常做生意。”",
                    NightOrderBuyerKind.Canteen => "“余量取消，已收的我照常用。你不用补货或退这部分钱，先顾柜台。”",
                    NightOrderBuyerKind.Repair => "“剩下的取消，不用填数，也没有罚金。我先照常巡修。”",
                    _ => ""
                };
                if (receipt.DeliveredUnits == 0 && receipt.Offer.BuyerKind == NightOrderBuyerKind.Canteen)
                    goodbye = "“这张单收起来就行。我另找合适的补给，不找你追货，也不收罚金。”";
                return new[] { status, goodbye };
            default:
                return Array.Empty<string>();
        }
    }

    public static string[] PriceRefused(NightOrderBuyerKind kind) => kind switch
    {
        NightOrderBuyerKind.Warehouse => new[] { "“这价我这趟带的钱不够。先别划掉单子，下回有合适的再谈。”" },
        NightOrderBuyerKind.Canteen => new[] { "“这价先停一下。我还得留够轮班间别的开销，东西先留在你这里。”" },
        NightOrderBuyerKind.Repair => new[] { "“这价没谈拢。先收好东西，别为了让我少跑一趟就硬做亏本买卖。”" },
        _ => Array.Empty<string>()
    };
    public static string[] PriceRefused(int buyerIndex) => PriceRefused((NightOrderBuyerKind)buyerIndex);

    public static string[] NoStock(NightOrderBuyerKind kind) => kind switch
    {
        NightOrderBuyerKind.Warehouse => new[] { "“今天没合适的就先忙别的。值班室还开着，我这单也不催你。”" },
        NightOrderBuyerKind.Canteen => new[] { "“补给先等你有货。别拿没对上的东西硬凑，这张单不让你赶夜班。”" },
        NightOrderBuyerKind.Repair => new[] { "“没有就等下一批货。我先照常巡修，单子留着，不把缺货记成欠款。”" },
        _ => Array.Empty<string>()
    };
    public static string[] NoStock(int buyerIndex) => NoStock((NightOrderBuyerKind)buyerIndex);

    public static string[] Revisit(NightOrderBuyerKind kind, int deliveredUnits, int requiredUnits,
        int day, int orderNumber)
    {
        if (!Known(kind) || deliveredUnits < 0 || requiredUnits <= 0 || deliveredUnits >= requiredUnits)
            return Array.Empty<string>();
        string greeting = kind switch
        {
            NightOrderBuyerKind.Warehouse => "顾砚把原来的收货单摊开，没另起一张催办单。",
            NightOrderBuyerKind.Canteen => "宋葵从菜单后面抽出旧收货单，确认自己这回没拿反。",
            NightOrderBuyerKind.Repair => "许桥翻到上回折好的那页，纸角已经被手套磨软。",
            _ => ""
        };
        string line = Intro(kind, day, orderNumber)[1];
        return new[] { greeting, line,
            $"“之前收到{deliveredUnits}/{requiredUnits}。今天还是同一张单，有货摆上台照常谈。”" };
    }
    public static string[] Revisit(int buyerIndex, int deliveredUnits, int requiredUnits, int day, int orderNumber) =>
        Revisit((NightOrderBuyerKind)buyerIndex, deliveredUnits, requiredUnits, day, orderNumber);

    public static string[] Revisit(NightOrderBuyerKind kind, int deliveredUnits, int requiredUnits,
        int day, int orderNumber, string itemName, long visitBudget)
    {
        if (visitBudget <= 0 || string.IsNullOrWhiteSpace(itemName)) return Array.Empty<string>();
        var lines = Revisit(kind, deliveredUnits, requiredUnits, day, orderNumber);
        if (lines.Length != 3) return Array.Empty<string>();
        return new[] { lines[0],
            $"“上回收到{deliveredUnits}/{requiredUnits}，这次还要{ShortItem(itemName)}。带来预算{visitBudget}，柜台再谈。”",
            "“还是原单，照常摆货议价。没合适的先等，不把预算当作已经报好的价。”" };
    }

    public static string[] WrongItem(NightOrderBuyerKind kind) => kind switch
    {
        NightOrderBuyerKind.Warehouse => new[] { "“先对一下单子，这件不是这回要收的。你照常卖，别替我算进来。”" },
        NightOrderBuyerKind.Canteen => new[] { "“这件先放回去，和单子没对上。不是摆了东西就得硬把它算成一份。”" },
        NightOrderBuyerKind.Repair => new[] { "“这件不对这张单。用途没对上，便宜也不该凑数。”" },
        _ => Array.Empty<string>()
    };
    public static string[] WrongItem(int buyerIndex) => WrongItem((NightOrderBuyerKind)buyerIndex);

    public static string[] Decline(NightOrderBuyerKind kind) => kind switch
    {
        NightOrderBuyerKind.Warehouse => new[] { "“明白，这张单先收起来。货还归你，咱们以后照常做生意。”" },
        NightOrderBuyerKind.Canteen => new[] { "“好，我先另找合适的。没接这单也没关系，下回路过照样打招呼。”" },
        NightOrderBuyerKind.Repair => new[] { "“行，单子我带走。你手上的货照常卖，我照常巡路。”" },
        _ => Array.Empty<string>()
    };
    public static string[] Decline(int buyerIndex) => Decline((NightOrderBuyerKind)buyerIndex);

    public static string[] Accepted(NightOrderBuyerKind kind) => kind switch
    {
        NightOrderBuyerKind.Warehouse => new[] { "“我把单子留好。你有货再约，到了柜台还是照常议价。”" },
        NightOrderBuyerKind.Canteen => new[] { "“行，先留下这张单。以后有货再见面，不为了接单自动划走你的东西。”" },
        NightOrderBuyerKind.Repair => new[] { "“好，单子留着。哪天有合适的再约，先把你自己的生意做稳。”" },
        _ => Array.Empty<string>()
    };

    // The saved order supplies numbers. These wrappers still only return text.
    public static string[] Opening(NightOrderOffer? offer, int day) => offer == null
        ? Array.Empty<string>() : Opening(offer.BuyerKind, day, offer.Ordinal,
            offer.ItemName, offer.RequiredUnits, offer.ProposedTotalBudget);
    public static string[] Opening(NightOrderActive? entry, int day) => entry?.Offer == null
        ? Array.Empty<string>() : entry.DeliveredUnits > 0 ? Revisit(entry, day) : Opening(entry.Offer, day);
    public static string[] Partial(NightOrderActive? entry) => entry?.Offer == null
        ? Array.Empty<string>() : Partial(entry.Offer.BuyerKind, entry.DeliveredUnits,
            entry.Offer.RequiredUnits, entry.ActualPaidTotal);
    public static string[] Completed(NightOrderActive? entry, int day) => entry?.Offer == null
        || entry.DeliveredUnits != entry.Offer.RequiredUnits ? Array.Empty<string>()
        : Completed(entry.Offer.BuyerKind, entry.Offer.RequiredUnits, entry.ActualPaidTotal, day, entry.Offer.Ordinal);
    public static string[] AcceptedDeal(NightOrderActive? entry, int day) => entry?.Offer == null
        ? Array.Empty<string>() : Receipt(entry.Offer.BuyerKind, entry.DeliveredUnits, entry.Offer.RequiredUnits,
            entry.ActualPaidTotal, entry.DeliveredUnits == entry.Offer.RequiredUnits, day, entry.Offer.Ordinal);
    public static string[] Revisit(NightOrderActive? entry, int day) => entry?.Offer == null
        ? Array.Empty<string>() : Revisit(entry.Offer.BuyerKind, entry.DeliveredUnits,
            entry.Offer.RequiredUnits, day, entry.Offer.Ordinal);
    public static string[] PriceRefused(NightOrderActive? entry) => entry?.Offer == null
        ? Array.Empty<string>() : PriceRefused(entry.Offer.BuyerKind);
    public static string[] NoStock(NightOrderActive? entry) => entry?.Offer == null
        ? Array.Empty<string>() : NoStock(entry.Offer.BuyerKind);
    public static string[] WrongItem(NightOrderActive? entry) => entry?.Offer == null
        ? Array.Empty<string>() : WrongItem(entry.Offer.BuyerKind);

    // Use only after the rule has confirmed that payment exhausted the agreed budget.
    // A partial close has a receipt, but is not represented as a completed order.
    public static string[] PartialClosed(NightOrderActive? entry)
    {
        if (entry?.Offer == null || !Known(entry.Offer.BuyerKind) || entry.DeliveredUnits <= 0
            || entry.DeliveredUnits >= entry.Offer.RequiredUnits || entry.ActualPaidTotal <= 0
            || entry.ActualPaidTotal != entry.Offer.ProposedTotalBudget)
            return Array.Empty<string>();
        string line = entry.Offer.BuyerKind switch
        {
            NightOrderBuyerKind.Warehouse => "“这趟预算用完了，余量取消。已收的先用，剩下的不记你欠货。”",
            NightOrderBuyerKind.Canteen => "“这趟预算用完了，就收这部分。余量取消，补给没凑齐也不找你追账。”",
            NightOrderBuyerKind.Repair => "“预算花完，就先收这些。余量取消，别另拿一件来替我填数。”",
            _ => ""
        };
        return new[] { $"“实际收到{entry.DeliveredUnits}/{entry.Offer.RequiredUnits}，共付{entry.ActualPaidTotal}现金。”", line };
    }

    private static string[] Intro(NightOrderBuyerKind kind, int day, int orderNumber) =>
        (kind switch { NightOrderBuyerKind.Warehouse => WarehouseOpenings,
            NightOrderBuyerKind.Canteen => CanteenOpenings,
            NightOrderBuyerKind.Repair => RepairOpenings,
            _ => Array.Empty<string[]>() })[Variant(day, orderNumber)];
    private static int Variant(int day, int orderNumber) =>
        (int)(((long)Math.Max(0, day) + (long)Math.Max(0, orderNumber) * 2) % VariantCount);
    private static bool Known(NightOrderBuyerKind kind) =>
        kind is NightOrderBuyerKind.Warehouse or NightOrderBuyerKind.Canteen or NightOrderBuyerKind.Repair;
    private static string ShortItem(string itemName)
    {
        string clean = itemName.Trim().Replace("\r", " ").Replace("\n", " ");
        return clean.Length <= 14 ? clean : clean[..13] + "…";
    }
}
