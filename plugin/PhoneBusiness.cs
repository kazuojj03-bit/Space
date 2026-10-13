using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
namespace SpacePhone;
public sealed partial class PhoneBehaviour
{
    private BusinessState B=>state!.Business;
    private sealed record CargoEntry(string Uid,string Id,string Name,string Category,int Units,long? Cost);
    private readonly List<CargoEntry> cargo=new();
    private float nextCargoScan;
    private int contactTab;
    [HideFromIl2Cpp] internal void RecordNativeTrade(bool purchase,string uid,string id,string name,string category,int units,int amount,string customerId,string publicName)
    {
        if(!Ready)return;EnsureState();if(state==null)return;
        // A native factory may represent a customer group; only public identifiers/names are recorded.
        var lot=B.Lots.FirstOrDefault(l=>l.ItemUid==uid&&l.ItemId==id);
        if(!purchase&&lot?.CostKnown==true&&lot.Units!=units)BusinessRules.InvalidateLot(B,uid,id);
        if(!purchase)RecordCommissionTrade(uid,id,units,amount,customerId);
        if(BusinessRules.RecordTrade(B,Day,purchase,uid,id,name,category,units,amount,customerId,publicName))
        {StoryRules.RecordTrade(state.Stories,Day,purchase,customerId,category,amount);WriteState();nextDraw=0;nextCargoScan=0;Plugin.Instance.Log.LogInfo($"Native merchandise trade: {(purchase?"buy":"sell")} {id} x{units} value={amount}");}
    }
    [HideFromIl2Cpp] private ShopActivity Activity()=>new(B.PurchaseCount,B.SaleCount,B.KnownResaleCount,B.UniqueClientIds.Count,B.PurchasedCategories.Count);
    [HideFromIl2Cpp] private void SyncStoryBenefits()=>B.ResearchDailyBonus=state!.Stories.Threads.Any(t=>t.Completed&&t.Outcome==1)?1:0;
    [HideFromIl2Cpp] private string NextGoal()
    {
        if(state==null)return "正在同步店铺资料";
        if(state.PendingSupplier>=0)return "供应商已预约 · 空闲后到访";
        if(state.PendingRecommendation)return "熟客推荐已预约 · 空闲后到访";
        if(Store.dayUntilRent<=2&&Store.GetCash()<Store.GetRentValue())return $"租金临近 · 还差 {Money(Store.GetRentValue()-Store.GetCash())}";
        int preferred=PreferredCommission();
        if(preferred>=0){var commission=state.Commissions.Entries[preferred];return commission.QueuedStage>=0?$"实物委托 · {CommissionRules.GetName(preferred)} 已预约到访":$"实物委托 · {commission.ItemName} {commission.DeliveredUnits}/{commission.RequiredUnits}\n最迟 D{commission.Deadline} 当天收工前交付";}
        return TaskGuide.GetSummary(state.Stories,state.ActiveStory,Day,Activity(),B.ResearchPoints);

    }
    [HideFromIl2Cpp] private int FreeSupplierCalls()=>1+(StoryRules.HasCommunityBenefit(state!.Stories,2)?1:0)+(B.Career==2&&BusinessRules.Tier(B)>0?1:0);
    [HideFromIl2Cpp] private int SupplierFee()
    {
        if(state==null||B.FreeMode)return 0;
        int used=state.SupplierCallDay==Day?state.SupplierCallsToday:0;
        return used<FreeSupplierCalls()?0:BusinessRules.ServicePrice(B,20);
    }
    [HideFromIl2Cpp] private int RecommendationFee()=>Math.Max(1,BusinessRules.ServicePrice(B,40)-(StoryRules.HasCommunityBenefit(state!.Stories,1)?10:0));
    [HideFromIl2Cpp] internal void InvalidateNativeLot(GameItem item)
    {if(state==null||item==null||!Ready)return;var lot=B.Lots.FirstOrDefault(l=>l.ItemUid==item.uniqueId.ToString()&&l.ItemId==item.identifier);if(lot?.CostKnown==true){BusinessRules.InvalidateLot(B,lot.ItemUid,lot.ItemId);WriteState();}}
    [HideFromIl2Cpp] internal void InheritSplitResearch(GameItem source,GameItem child)
    {if(state!=null&&Ready&&BusinessRules.InheritResearchIdentity(B,source.uniqueId.ToString(),child.uniqueId.ToString()))WriteState();}
    [HideFromIl2Cpp] private void RequestSupplier(int kind)
    {int fee=SupplierFee();if(fee==0)Summon(kind,0);else Ask("联络供应商",$"本次最多支付 {fee} 联络费。\n忙碌时先预约，实际到访时按不高于此价结算。\n每天有免费额度，超额仍可付费联络。",()=>Summon(kind,fee));}
    [HideFromIl2Cpp] private void AskRecommendation()
    {
        if(B.Career!=0||BusinessRules.Tier(B)<1){message="取得商脉经纪执照后开放推荐";return;}
        if(state!.RecommendedDay==Day||state.PendingRecommendation){message="今天已预约过熟客推荐";return;}
        int fee=RecommendationFee();Ask("熟客推荐",$"联络费最多 {fee} · 每日一次\n由普通顾客正常挑选商品与议价。\n当前顾客在场时，推荐会排队等待。",()=>RecommendCustomer(fee));
    }
    [HideFromIl2Cpp] private void RecommendCustomer(int agreedFee)
    {
        if(state==null||B.Career!=0||BusinessRules.Tier(B)<1){if(state!=null){state.PendingRecommendation=false;WriteState();}return;}
        if(state.RecommendedDay==Day){state.PendingRecommendation=false;WriteState();return;}
        if(Store.IsClientPresent()||Store.lockAllFlag>0||Store.lockNextBellFlag>0||Store.storeState!=PlayerStore.StoreState.OPEN)
        {state.PendingRecommendation=true;state.PendingRecommendationFeeCap=agreedFee;WriteState();message="已预约普通顾客推荐";return;}
        int fee=RecommendationFee();if(fee>agreedFee){message="推荐联络费变化，请取消后重新预约";return;}if(Store.GetCash()<fee){message="联络费不足，推荐预约保留";return;}
        var client=StoreClientList.CreateSpacerClientFood();
        Store.storeClientManager.AddNextClient(client);ChangeMoney(-fee);Economy.Record(state,Day,"商脉推荐联络费",-fee);
        state.RecommendedDay=Day;state.PendingRecommendation=false;WriteState();Close();Store.TryCallNextClient();
        if(Store.currentClientInstance?.storeClient.identifier==client.identifier)StoreClientMono.Instance?.SetImmediatlyArrive();
        message="普通顾客已推荐，照常营业与议价";
    }
    [HideFromIl2Cpp] private void Pager(float y,int count,int perPage,int current,Action<int> change)
    {
        int pages=Math.Max(1,(count+perPage-1)/perPage);if(pages<=1)return;
        Btn(36,y,116,34,"上一页",()=>change(Math.Max(0,current-1)),current>0);Small(178,y+7,105,$"{current+1} / {pages}");Btn(288,y,116,34,"下一页",()=>change(Math.Min(pages-1,current+1)),current+1<pages);
    }
    [HideFromIl2Cpp] private string CategoryName(string category)=>category switch
    {"food"=>"食品","medicine" or "medical"=>"医药","water"=>"饮水","weapon" or "gun"=>"武器","material"=>"材料","amenity" or "household"=>"日用品","tool"=>"工具","contraband"=>"管制品","document"=>"资料","other"=>"其他",_=>category};
    [HideFromIl2Cpp] private void DrawContacts()
    {
        Btn(36,185,179,34,"成交档案",()=>{contactTab=0;selectedContact=-1;});Btn(225,185,179,34,"站内声望",()=>{contactTab=1;selectedContact=-1;});Fill(contactTab==0?36:225,217,179,2,Gold);
        if(contactTab==1)
        {
            var list=Store.storeReputations;
            if(list==null||list.Count==0){Label(42,270,350,90,"暂无阵营声望记录。正常经营后这里会同步。");return;}
            for(int i=0;i<Math.Min(5,list.Count);i++){var rep=list[i];if(rep==null)continue;view!.Card(36,239+i*83,368,72,StoreReputation.GetFactionNameByID(rep.factionId),rep.GetReputation().ToString(),"",Accent);}
            return;
        }
        if(selectedContact>=0&&selectedContact<B.Contacts.Count)
        {
            var c=B.Contacts[selectedContact];view!.Card(36,236,368,111,c.PublicName,$"成交 {c.Purchases+c.Sales} 笔",$"首次 D{c.FirstDay} · 最近 D{c.LastDay}",Gold);
            Label(42,369,350,130,$"收购 {c.Purchases} 笔 · 售出 {c.Sales} 笔\n累计金额 {Money(c.Turnover)}\n接触品类：{string.Join(" / ",c.Categories.Select(CategoryName))}");
            Label(42,530,350,72,"档案记录公开的成交对象。\n普通顾客群会归并到同一档案。");Btn(36,625,368,37,"返回成交档案",()=>selectedContact=-1);return;
        }
        if(B.Contacts.Count==0)Label(42,263,350,180,"在柜台完成一笔真实收购或出售，\n这里会留下公开的顾客档案。\n\n信任和经验来自经营本身。");
        contactsPage=Math.Clamp(contactsPage,0,Math.Max(0,(B.Contacts.Count-1)/4));
        for(int n=0;n<4;n++){int i=contactsPage*4+n;if(i>=B.Contacts.Count)break;var c=B.Contacts[i];float y=238+n*87;Surface(36,y,368,76,"contact:"+i,()=>selectedContact=i);Label(50,y+10,300,28,c.PublicName);Small(50,y+43,335,$"{c.Purchases+c.Sales} 笔 · {Money(c.Turnover)} · 最近 D{c.LastDay}");}
        Pager(597,B.Contacts.Count,4,contactsPage,p=>contactsPage=p);
        Btn(36,644,368,34,state!.PendingRecommendation?"取消熟客推荐预约":"预约熟客推荐",()=>{if(state.PendingRecommendation){state.PendingRecommendation=false;WriteState();message="预约已取消";}else AskRecommendation();},state.PendingRecommendation||(B.Career==0&&BusinessRules.Tier(B)>0&&state.RecommendedDay!=Day));
    }
    [HideFromIl2Cpp] private void DrawCareer()
    {
        Small(42,187,350,$"实物成交 {B.PurchaseCount+B.SaleCount} 笔 · 研究 {B.ResearchPoints} / 5");
        string[] abilities={"每日一次普通顾客推荐，联络费用更低","每日首份研究免资料点，核对转售来源","每日增加一次免费供应商联络"};
        for(int i=0;i<3;i++){int career=i;float y=232+i*132;Surface(36,y,368,119,"career:"+i,()=>ChooseCareer(career));Fill(36,y,3,119,B.Career==i?Gold:Accent);Label(50,y+10,330,33,BusinessRules.CareerNames[i]+(B.Career==i?" · 已选":""),true);Small(50,y+49,330,BusinessRules.CareerProgress(B,i));Small(50,y+82,330,abilities[i]);}
        Label(42,637,350,46,"专长优惠最高 15%。首次取得免费；\n更换需次日，费用 120。");
    }
    [HideFromIl2Cpp] private void ChooseCareer(int career)
    {
        if(!BusinessRules.CanSelectCareer(B,Day,career,out string reason)){message=reason;return;}
        int day=Day,fee=B.Career<0?0:120;
        Ask("取得经营专长",$"{BusinessRules.CareerNames[career]}\n费用 {fee}\n同时专精一项；真实经营继续积累等级。",()=>{if(Day!=day||!BusinessRules.CanSelectCareer(B,Day,career,out string why)){message="条件已变化，请重新选择";return;}if(Store.GetCash()<fee){message="现金不足";return;}if(fee>0)ChangeMoney(-fee);BusinessRules.SelectCareer(B,Day,career,out _);if(fee>0)Economy.Record(state!,Day,"更换经营专长",-fee);WriteState();message="经营专长已取得";});
    }
    [HideFromIl2Cpp] private void DrawStories()
    {
        if(selectedStory>=0)
        {
            int i=selectedStory;Label(42,189,232,39,StoryRules.GetTitle(i),true);Btn(288,186,116,34,"通讯录",()=>selectedStory=-1);
            Small(42,238,350,StoryRules.GetRole(i));Fill(36,276,368,335,Panel);Label(50,288,340,309,StoryRules.GetMessage(state!.Stories,i,Day,Activity()));
            var choices=StoryRules.Choices(state.Stories,i,Day,Activity());
            for(int n=0;n<choices.Length;n++){int key=choices[n].Key;string label=choices[n].Label;Btn(36+n*189,634,choices.Length==1?368:179,44,label,()=>RequestStoryReply(i,key));}
            if(choices.Length==0)Small(42,645,350,state.Stories.Threads[i].Completed?"这份联系已经留下长期影响":"继续营业或等到次日，再查看回信");
            return;
        }
        Small(42,186,350,"三条来信，三种经营伙伴关系");
        string[] portraits={"rain","medic","runner"};
        for(int i=0;i<3;i++){int story=i;float y=233+i*136;var t=state!.Stories.Threads[i];bool ready=StoryRules.Choices(state.Stories,i,Day,Activity()).Length>0;
            Surface(36,y,368,119,"story:"+i,()=>selectedStory=story);view!.Icon(48,y+19,58,portraits[i],()=>pendingAction=()=>selectedStory=story);Label(121,y+13,265,31,StoryRules.GetTitle(i),true);Small(121,y+50,268,StoryRules.GetRole(i));Small(121,y+83,268,t.Completed?(t.Outcome==0?"长期合作已建立":"研究路线已完成"):ready?"有新消息 · 点击回复":"等待经营记录或次日回信");Fill(36,y,3,119,ready?Gold:Accent);
        }
        Small(42,653,350,"路线一经回复就保留，回报只结算一次");
    }
    [HideFromIl2Cpp] private void RequestStoryReply(int i,int choice)
    {
        int phase=state!.Stories.Threads[i].Phase;
        if(phase!=0&&phase!=2){ReplyStory(i,choice);return;}
        int day=Day;string label=StoryRules.Choices(state.Stories,i,Day,Activity()).First(c=>c.Key==choice).Label;
        string benefit=i==0?"情报研究费用减免 10%":i==1?"熟客推荐费用减免 10":"每日增加一次免费供应商联络";
        string body=phase==0?$"{label}\n确认后记录新的实物经营，路线会保留。\n既有成交不补算，至少次日才可汇总。":$"{label}\n"+(choice==0?benefit:"获得 1 点资料，每日报告上限增至 3")+"\n回报互斥；次日收到回信再结算。";
        Ask("回复 "+StoryRules.GetTitle(i),body,()=>{if(Day!=day||state.Stories.Threads[i].Phase!=phase){message="来信状态已变化，请重新回复";return;}ReplyStory(i,choice);});
    }
    [HideFromIl2Cpp] private void ReplyStory(int i,int choice)
    {
        var result=StoryRules.Reply(state!.Stories,i,choice,Day,Activity());message=result.Message;
        if(result.Changed){BusinessRules.StoreResearchReward(B,result.ResearchGain);SyncStoryBenefits();WriteState();}
    }
    [HideFromIl2Cpp] private void RefreshCargo()
    {
        if(Time.unscaledTime<nextCargoScan)return;nextCargoScan=Time.unscaledTime+3;cargo.Clear();
        var owned=EmporiumEntry.Instance.GetAllOwnedItems(false,true);var seen=new HashSet<string>();
        for(int i=0;i<owned.Count&&i<3000;i++)
        {
            var item=owned[i];if(item==null||!GeneralHelper.IsItemEffectivelyOwned(item)||item.unitCount<=0)continue;
            string uid=item.uniqueId.ToString();if(!seen.Add(uid))continue;
            string category="other";var types=item.GetGameItemType();if(types.Count>0)category=types[0].ToLowerInvariant();
            var lot=B.Lots.FirstOrDefault(l=>l.ItemUid==uid&&l.ItemId==item.identifier);
            cargo.Add(new CargoEntry(uid,item.identifier,item.GetDisplayName(false),category,item.unitCount,lot?.CostKnown==true&&lot.Units==item.unitCount?lot.Cost:null));
        }
        cargo.Sort((a,b)=>string.Compare(a.Name,b.Name,StringComparison.Ordinal));
    }
    [HideFromIl2Cpp] private void DrawCargo()
    {
        RefreshCargo();Small(42,188,350,$"自有货品 {cargo.Count} 项 · 总单位 {cargo.Sum(c=>(long)c.Units)}");
        cargoPage=Math.Clamp(cargoPage,0,Math.Max(0,(cargo.Count-1)/4));
        for(int n=0;n<4;n++){int i=cargoPage*4+n;if(i>=cargo.Count)break;var c=cargo[i];float y=233+n*97;Fill(36,y,368,85,Panel);Label(49,y+8,335,26,c.Name+" ×"+c.Units);Small(49,y+37,334,$"{CategoryName(c.Category)} · 自有库存");Small(49,y+60,334,$"已知采购成本 {(c.Cost.HasValue?Money(c.Cost.Value):"来源待核对")}");}
        if(cargo.Count==0)Label(42,267,350,140,"暂无可读取的自有货品。\n在柜台收购商品后，这里会同步。");
        Pager(632,cargo.Count,4,cargoPage,p=>cargoPage=p);Small(42,675,350,"采购记录供经营参考，货品仍需在柜台检查");
    }
    [HideFromIl2Cpp] private void DrawBusinessLedger()
    {
        Small(42,187,350,$"收购 {Money(B.PurchaseAmount)} · 售出 {Money(B.SaleAmount)}");
        businessPage=Math.Clamp(businessPage,0,Math.Max(0,(B.Trades.Count-1)/4));
        for(int n=0;n<4;n++){int i=businessPage*4+n;if(i>=B.Trades.Count)break;var t=B.Trades[i];float y=233+n*97;Fill(36,y,368,85,Panel);Label(49,y+8,335,26,(t.Purchase?"收购 · ":"出售 · ")+t.ItemName+" ×"+t.Units);Small(49,y+37,334,$"D{t.Day} · {t.PublicName} · {Money(t.Amount)}");Small(49,y+60,334,t.Purchase?"来源成本已登记":t.Profit.HasValue?$"已知成本 {Money(t.KnownCost!.Value)} · 毛利 {t.Profit:+#,0;-#,0;0}":"成本来源待核对，不推断利润");}
        if(B.Trades.Count==0)Label(42,259,350,180,"真实柜台买卖会留下商品流转记录。\n\n借款、证券和彩票在现金账本中。\n无法核对来源的库存不虚构成本。");
        Pager(632,B.Trades.Count,4,businessPage,p=>businessPage=p);Small(42,675,350,"保留最近 60 笔；库存拆合后可能需要重新核对");
    }
    [HideFromIl2Cpp] private int ResearchFee()
    {
        int fee=BusinessRules.ServicePrice(B,30);
        return StoryRules.HasCommunityBenefit(state!.Stories,0)?Math.Max(1,(int)Math.Ceiling(fee*.9)):fee;
    }
    [HideFromIl2Cpp] private int ScheduledDay(StoreEvent scheduled,int offset)=>scheduled.startDay>0?scheduled.startDay:(int)Math.Min(int.MaxValue,(long)Day+Math.Max(0,offset));
    [HideFromIl2Cpp] private void DrawIntelligence()
    {
        SyncStoryBenefits();int used=B.ResearchDay==Day?B.ResearchToday:0;
        view!.Card(36,188,368,101,$"资料研究 · 今日 {used}/{BusinessRules.DailyResearchLimit(B)} 份",$"{B.ResearchPoints} / 5 点",B.StoredResearchPoints>0?$"已归档 {B.StoredResearchPoints} 点 · 研究消耗后自动补入":"新实物成交获得资料，每日最多 3 点",Accent);
        Btn(36,302,179,36,"前一天",()=>forecastOffset=Math.Max(1,forecastOffset-1));Btn(225,302,179,36,"后一天",()=>forecastOffset=Math.Min(7,forecastOffset+1));
        for(int i=0;i<4;i++){int company=i;Btn(36+i*94,352,86,35,Economy.Companies[i],()=>selectedStock=company);}Fill(36+selectedStock*94,385,86,2,Gold);
        var report=BusinessRules.FindReport(B,Day,selectedStock,forecastOffset);Fill(36,402,368,145,Panel);
        Small(49,414,340,$"{Economy.Tickers[selectedStock]} · 第 {Day+forecastOffset} 天");
        if(report==null){Label(49,446,340,47,"尚未研究这份行情",true);Small(49,507,340,"研判给出区间与信号，不显示完整答案");}
        else{Label(49,444,340,45,$"{report.Low} — {report.High}",true);Small(49,496,340,$"{report.Signal} · 资料强度 {report.Confidence} / 100");Small(49,522,340,"同日固定研判；重开手机不会刷新结果");}
        int fee=ResearchFee(),pointCost=BusinessRules.ResearchPointCost(B,Day);Btn(36,559,368,40,report==null?$"研究报告 · {fee} 现金 + {pointCost} 点":"报告已归档",AskResearch,report==null&&Store.GetCash()>=fee&&BusinessRules.CanResearch(B,Day,out _));
        Small(42,611,350,"空间站已排定事件");var events=StoreStation.Instance.storeEventManager;int count=Math.Min(events.futurEvents.Count,events.futurEventsDay.Count),shown=0;
        for(int e=0;e<count&&shown<2;e++){int scheduled=ScheduledDay(events.futurEvents[e],events.futurEventsDay[e]);if(scheduled<Day)continue;Small(42,640+shown*27,350,$"D{scheduled} · {events.futurEvents[e].GetDisplayName()}");shown++;}
        if(shown==0)Small(42,641,350,"暂无已排定事件；研判仅针对手机证券模型");
    }
    [HideFromIl2Cpp] private void ResearchMarket()
    {
        if(BusinessRules.FindReport(B,Day,selectedStock,forecastOffset)!=null){message="报告已归档，不重复收费";return;}
        if(!BusinessRules.CanResearch(B,Day,out string reason)){message=reason;return;}
        int fee=ResearchFee();if(Store.GetCash()<fee){message="现金不足";return;}
        var report=BusinessRules.CreateReport(B,state!.Seed,Day,selectedStock,forecastOffset);if(report==null){message="研究暂不可用";return;}
        ChangeMoney(-fee);Economy.Record(state,Day,"行情研究 "+Economy.Tickers[selectedStock],-fee);WriteState();message="研判已归档，结果和资料已保存";
    }
    [HideFromIl2Cpp] private void AskResearch()
    {int day=Day,company=selectedStock,offset=forecastOffset,fee=ResearchFee(),points=BusinessRules.ResearchPointCost(B,Day);Ask("委托行情研究",$"目标 D{day+offset} · {Economy.Companies[company]}\n支付 {fee} 现金与 {points} 点资料。\n同日同一报告保留结果，无重复扣款。",()=>{if(Day!=day||selectedStock!=company||forecastOffset!=offset||ResearchFee()!=fee||BusinessRules.ResearchPointCost(B,Day)!=points){message="研究报价已更新，请重新提交";return;}ResearchMarket();});}
}
