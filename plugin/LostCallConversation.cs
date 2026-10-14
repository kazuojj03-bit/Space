namespace SpacePhone;

/// <summary>Original fictional dialogue only. It never advances a task, moves an item or awards anything.</summary>
public static class LostCallConversation
{
    public const int MemoryVariantCount = 3;
    public const string OriginalPublicMessage = "夜航到站。灯没灭，放心。桌上给你留了热的。";

    private static readonly string[] Introduction = {
        "余灯抹掉袖口的雨，把一张皱纸压在柜台边。",
        "“处置单在这。原主人留了自己的磁带，盒子和公开播报交给我处理。”",
        "“货梯缝里捡回来的。那地方吞东西很准，吐东西得靠我。”",
        "“先听来历，别急着买。你这里是柜台，不是替夜班人付账的地方。”"
    };
    private static readonly string[] Acquisition = {
        "余灯从工具袋里掏出录音匣，挪开旁边那只掉漆的保温杯。",
        "“铝边磕过，扣环还紧。播报有一点破音，不是你耳朵坏了。”",
        "“参考四十。你手机里留的七十只是提醒，别为了听下半段故事硬买。”",
        "“摆上台，照你柜台的规矩谈。不合适我带回去，晚饭钱还是晚饭钱。”"
    };
    private static readonly string[] DeliveryProfit = {
        "余灯翻出收藏人的回执，边角夹着一张旧夜航班票。",
        "“这一趟替收藏人来收，拟定预算一百四十。不是已经给你报了这个价。”",
        "“同一只匣子摆上台，我们正常谈。私人磁带没在里面，也没写进收货单。”"
    };
    private static readonly string[] DeliveryHuman = {
        "余灯带来的回执上写着公共听音台，下面是一排不太整齐的签名。",
        "“他们凑的预算暂按八十。你照常报价，不用因为我讲了旧船员就白送。”",
        "“成交后我带过去。林芦说装在茶水间，听得见，也方便骂我的手艺。”"
    };
    private static readonly string[] DeliveryInformation = {
        "余灯展开维修站的收货单，一枚短铅笔从袖口滚出来。",
        "“测试架这边拟定预算八十，照常议价。录音匣成交后带走，不是借去摆两天。”",
        "“他们会留一页匿名周转记录。不是稳赚秘籍，我连保温杯的盖子都常找不着。”"
    };
    private static readonly string[] DebriefProfit = {
        "余灯把最后一张回执放下，这次工具袋比上回轻了一点。",
        "“收藏人本来拿着放大镜，听完那句报平安，就把镜子放下了。”",
        "“他说忘了旧夜班也有人等回家。我说，是啊，回家还得洗杯子。”",
        "“物和钱都对上了，回执收好。挣多少照柜台记，别听我替故事抬价。”"
    };
    private static readonly string[] DebriefHuman = {
        "余灯把听音台的回执递来，纸背沾着一点茶水。",
        "“林芦听完先骂我修得太清楚，连当年的破音都没了。骂完又听了一遍。”",
        "“那句是他们同意留给公共播报的：‘夜航到站。灯没灭，放心。桌上给你留了热的。’”",
        "“回执收好。我给你留这段留言，忙的时候不用听，它不会催你回来。”"
    };
    private static readonly string[] DebriefInformation = {
        "余灯递来维修站回执，一角画着三层楼和几只歪箱子。",
        "“他们说一直以为缺料，后来发现东西压在不同楼层。货也会坐错电梯。”",
        "“这页匿名周转记录给你留着。它能帮你多问一句，不能替你保证涨价。”",
        "“回执收好，资料就这一页。柜子满了先夹在这里，腾出位置再归档。”"
    };
    private static readonly string[] SoldElsewhere = {
        "余灯看了你留下的成交记录，把三张接收回执叠回工具袋。",
        "“你已经卖给别人了。它是你花钱收的货，自行找买家也是一种决定。”",
        "“那三张回执不用了，也不找你另买一只。照实收好记录，继续做生意。”"
    };
    private static readonly string[] Destroyed = {
        "余灯把核对过的记录放在柜台上，手指抚平纸角。",
        "“这只匣子确实留不住了。三张回执我收回，不找你赔，也不拿别的货抵。”",
        "“把这次记录收好就行。下回线路真坏了，电话还是可以打给我。”"
    };

    // Ten short responses bring the core visit/reply script to 45 original bubbles.
    private static readonly string[] Invite = { "“行。哪天柜台空一点，我带处置单来。先见面，不替你做决定。”" };
    private static readonly string[] Later = { "“先忙。我这边还有一条线路要修，号码留着，谁也不用赶谁。”" };
    private static readonly string[] Decline = { "“明白。我把单子收好。线路以后真有杂音，照样可以找我。”" };
    private static readonly string[] Inspect = { "“行，明天起我把东西带来。你按规矩报价，谈不拢也不耽误咱们说话。”" };
    private static readonly string[] Pause = { "“先把现金留够。我可不想修完一只盒子，顺手修坏了你的账。”" };
    private static readonly string[] CloseOffer = { "“没问题。这次就到这。我带走单子，你照常营业。”" };
    private static readonly string[] SelectProfit = { "“那就替收藏人准备回执。预算暂按一百四十，最后还得在柜台谈。”" };
    private static readonly string[] SelectHuman = { "“好，听音台这边预算暂按八十。你不是在捐货，照常做生意。”" };
    private static readonly string[] SelectInformation = { "“我给维修站留话，预算暂按八十。资料等事情办完再讲，不拿它替价钱撑场面。”" };
    private static readonly string[] PauseRoute = { "“先看账。哪张回执都没催你，匣子也还在你手里。”" };

    private static readonly string[][] ProfitMemories = {
        new[] { "“收藏人给匣子做了个底座，底下垫旧班票。我看那票比底座还金贵。”", "“他昨晚又问外壳哪年产的。我说，别问我，我只负责把它从货梯缝里救出来。”" },
        new[] { "“那人把播报声调得很小。说晚上听着，屋里像还有人在等下一班。”", "“今天没收货单，路过说一声。我也得提醒自己，进店不一定是来找螺丝。”" },
        new[] { "“收藏人把柜子往里挪了，怕窗边潮。东西换了地方，人还是一有雨就操心。”", "“上回那笔账已经结清。我今天只想借门口躲半分钟雨，不另收你一只匣子。”" }
    };
    private static readonly string[][] HumanMemories = {
        new[] { "“听音台昨夜还亮着。林芦嫌茶太淡，倒没再嫌那段播报太清楚。”", "“他说那句‘灯没灭’应该留着。我说先让灯泡留着，我才换没两天。”" },
        new[] { "“茶水间多了张凳子，大家说是给设备留的。设备哪会坐，谁坐了也没签名。”", "“你手机里的留言留着就好。今天不用听，热的那份也不会因为你晚一点就跟你生气。”" },
        new[] { "“林芦把匣子外面的旧刻痕擦干净了，没补漆。说擦掉尘就行，别把年头也擦了。”", "“这趟没带新委托。人偶尔来打个招呼，不必每次都欠你一张回执。”" }
    };
    private static readonly string[][] InformationMemories = {
        new[] { "“维修站给箱子贴了楼层牌。头一天还有人把牌贴在自己杯子上。”", "“那页记录还是上次那页，没凭空长出第二份。遇到新情况，得重新看现场。”" },
        new[] { "“他们终于找到了压在三楼的旧箱子，找了半班，原来一直坐在上面喝茶。”", "“我现在听人说缺货，会多问一句：真缺，还是放哪儿忘了？这比拍胸口管用。”" },
        new[] { "“测试架跑得挺稳，连那支短铅笔都有固定位置了。只有我又把保温杯盖落在柜台。”", "“我今天只报个近况。那页资料已经留给你，咱们聊天不用重新签收货单。”" }
    };
    private static readonly string[][] SelfSaleMemories = {
        new[] { "“有人问那只匣子后来去哪了，我说你自己找了买家，账也记清楚了。”", "“我不替你编另一个结局。做过的生意，照做过的说就行。”" },
        new[] { "“三张旧回执我收起来了，背面还能记工单。纸没白跑，匣子也没再卡货梯。”", "“这次路过，不是来让你凑一只新的。你柜台上的别的货照常卖。”" },
        new[] { "“我今天又捡到一枚螺丝。放心，这回不打陌生来电，我知道它是从哪掉的。”", "“上次匣子那笔生意已经照实收好，咱们不用每次见面都把账翻回来。”" }
    };
    private static readonly string[][] LossMemories = {
        new[] { "“那次记录已经收好了。东西没留下，咱们也不用把每次见面都变成追责。”", "“我今天检查门口线路，顺便看看你这灯还亮不亮。亮着就好。”" },
        new[] { "“我给工具袋换了扣环。丢过东西的人，总会多摸一下袋口。”", "“没带替代匣子，也没带账单。只是路过，问声夜班还顺不顺。”" },
        new[] { "“货梯今天难得没卡。维修班说要合影，我说趁它还开着，先把货运完。”", "“那张旧记录就在那儿，照实留着。今天还有今天的生意。”" }
    };

    public static string[] Opening(LostCallVisitKind kind, LostCallRoute route, LostCallBook book)
    {
        if (!LostCallRules.IsValid(book)) return Array.Empty<string>();
        return kind switch {
            LostCallVisitKind.Introduction when book.Phase == LostCallPhase.AwaitingIntroduction => Copy(Introduction),
            LostCallVisitKind.Acquisition when book.Phase == LostCallPhase.AwaitingAcquisition => Copy(Acquisition),
            LostCallVisitKind.Delivery when book.Phase == LostCallPhase.Acquired && route == book.ProposedRoute => Copy(route switch {
                LostCallRoute.Profit => DeliveryProfit, LostCallRoute.Human => DeliveryHuman,
                LostCallRoute.Information => DeliveryInformation, _ => Array.Empty<string>() }),
            LostCallVisitKind.Debrief when book.Phase is LostCallPhase.Delivered or LostCallPhase.Closed => Ending(book),
            LostCallVisitKind.Memory => Memory(book, book.LastVisitDay),
            _ => Array.Empty<string>() };
    }

    // Call after the corresponding rule has validated/applied the player's choice; these strings are not authorization.
    public static string[] Reply(int key, LostCallBook book)
    {
        if (!LostCallRules.IsValid(book)) return Array.Empty<string>();
        return key switch {
            LostCallRules.InviteChoice when book.Phase == LostCallPhase.AwaitingIntroduction => Copy(Invite),
            LostCallRules.LaterChoice when book.Phase == LostCallPhase.Offered => Copy(Later),
            LostCallRules.DeclineChoice when book.Phase == LostCallPhase.Declined => Copy(Decline),
            LostCallRules.InspectChoice when book.Phase == LostCallPhase.AwaitingAcquisition => Copy(Inspect),
            LostCallRules.PauseChoice when book.Phase == LostCallPhase.AwaitingIntroduction => Copy(Pause),
            LostCallRules.CloseOfferChoice when book.Phase == LostCallPhase.Declined => Copy(CloseOffer),
            LostCallRules.ProfitChoice when book.Phase == LostCallPhase.Acquired && book.ProposedRoute == LostCallRoute.Profit => Copy(SelectProfit),
            LostCallRules.HumanChoice when book.Phase == LostCallPhase.Acquired && book.ProposedRoute == LostCallRoute.Human => Copy(SelectHuman),
            LostCallRules.InformationChoice when book.Phase == LostCallPhase.Acquired && book.ProposedRoute == LostCallRoute.Information => Copy(SelectInformation),
            LostCallRules.PauseRouteChoice when book.Phase == LostCallPhase.Acquired => Copy(PauseRoute),
            LostCallRules.ArchiveChoice when book.Phase == LostCallPhase.Closed => ClosingLine(book),
            LostCallRules.AskMemoryChoice when book.Phase is LostCallPhase.Delivered or LostCallPhase.Closed => Ending(book),
            _ => Array.Empty<string>() };
    }

    // Read-only, deterministic for a saved ending and game day. Reopening a phone does not reroll or reward.
    public static string[] Memory(LostCallBook book, int day)
    {
        if (!LostCallRules.IsValid(book) || book.Phase != LostCallPhase.Closed || day <= book.ClosedDay || day > LostCallRules.MaxDay)
            return Array.Empty<string>();
        int index = (day - book.ClosedDay - 1) % MemoryVariantCount;
        string[][]? memories = book.ItemDisposition switch {
            LostCallDisposition.SoldElsewhere => SelfSaleMemories,
            LostCallDisposition.Destroyed => LossMemories,
            LostCallDisposition.Delivered => book.CommittedRoute switch {
                LostCallRoute.Profit => ProfitMemories, LostCallRoute.Human => HumanMemories,
                LostCallRoute.Information => InformationMemories, _ => null },
            _ => null };
        return memories == null ? Array.Empty<string>() : Copy(memories[index]);
    }

    // Short receipt text: finance is shown by the phone's separate cost/revenue cards.
    public static string OutcomeSummary(LostCallBook book)
    {
        if (!LostCallRules.IsValid(book)) return "这通来电的记录暂时对不上，先照常营业。";
        if (book.Phase == LostCallPhase.Declined) return "这次没有接陌生委托。余灯收好了处置单，你照常营业。";
        if (book.Phase != LostCallPhase.Closed) return book.Phase == LostCallPhase.Delivered
            ? "收货回执还在路上。闲时请余灯来讲结果，先照常做生意。"
            : "这通来电还没收尾。下一步留在手机里，想忙完柜台再看也行。";
        return book.ItemDisposition switch {
            LostCallDisposition.SoldElsewhere => "你另找买家卖出了录音匣。余灯收起三张回执，照实留下这段来电；以后仍能约来聊聊。",
            LostCallDisposition.Destroyed => "录音匣确实没能留下。这次记录已经收好，余灯不追账、不索替代货；以后照常打个招呼。",
            LostCallDisposition.Delivered => book.CommittedRoute switch {
                LostCallRoute.Profit => "录音匣到了夜航收藏人手里。回执已收好；想听近况时，再约余灯聊聊。",
                LostCallRoute.Human => "录音匣回到公共听音台，原创报平安留言留在手机里。余灯偶尔会带来林芦的近况。",
                LostCallRoute.Information when book.PendingResearch == 1 => "录音匣已交维修站。一页匿名周转研究还夹在回执里，等资料柜腾出位置后归档。",
                LostCallRoute.Information => "录音匣已交维修站，那一页匿名周转研究已收好。余灯偶尔还会讲维修班的近况。",
                _ => "回执已收好，余灯留着这段来电。" },
            _ => "回执已收好，想聊天时再约余灯。" };
    }

    private static string[] Ending(LostCallBook book) => Copy(book.ItemDisposition switch {
        LostCallDisposition.SoldElsewhere => SoldElsewhere,
        LostCallDisposition.Destroyed => Destroyed,
        LostCallDisposition.Delivered => book.CommittedRoute switch {
            LostCallRoute.Profit => DebriefProfit, LostCallRoute.Human => DebriefHuman,
            LostCallRoute.Information => DebriefInformation, _ => Array.Empty<string>() },
        _ => Array.Empty<string>() });
    private static string[] ClosingLine(LostCallBook book)
    {
        var ending = Ending(book);
        return ending.Length == 0 ? ending : new[] { ending[^1] };
    }
    private static string[] Copy(string[] source) => (string[])source.Clone();
}
