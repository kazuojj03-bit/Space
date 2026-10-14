using Il2CppInterop.Runtime.Attributes;
namespace SpacePhone;

public sealed partial class PhoneBehaviour
{
    private int selectedCommission=-1;
    [HideFromIl2Cpp] internal bool OwnsCommissionBook(CommissionBook book)=>Ready&&state!=null&&ReferenceEquals(state.Commissions,book)&&owner==Store.Pointer&&ownerRun==Store.runID;
    [HideFromIl2Cpp] private void TickCommissions()
    {
        bool changed=PhoneCommissionClients.ConfigureOffers(state!.Commissions,Day);
        changed|=CommissionRules.Settle(state.Commissions,Day);
        changed|=FlushCommissionRewards();
        if(changed)WriteState();
        commissionClients.Tick(Store,state.Commissions,Day,OnCommissionChoice,WriteState,Close,pendingAction==null&&confirmAction==null&&!NativePhoneBusy());
    }
    [HideFromIl2Cpp] private void OpenCommissions(int index)
    {selectedCommission=index is >=0 and <3?index:-1;Navigate("实物委托");}
    [HideFromIl2Cpp] private void OnCommissionChoice(int index,int choice)
    {
        if(!Ready||state==null||owner!=Store.Pointer||ownerRun!=Store.runID||index is <0 or >2||state.Stories.Threads[index].Phase<1)return;
        var result=CommissionRules.Reply(state.Commissions,index,choice,Day);
        message=result.Message;
        if(result.Changed){if(choice==0)CommissionRules.BindDelivery(state.Commissions,index,"1");WriteState();nextDraw=0;}
    }
    [HideFromIl2Cpp] private void RecordCommissionTrade(string uid,string itemId,int units,int amount,string clientId)
    {
        if(state==null||owner!=Store.Pointer||ownerRun!=Store.runID||Store.currentClientInstance?.storeClient.identifier!=clientId||
            !PhoneCommissionClients.TryParse(clientId,out int index,out int stage)||stage!=1)return;
        string token=$"{uid.Length}:{uid}:{itemId.Length}:{itemId}:{Day}:{units}:{amount}";
        var result=CommissionRules.RecordPayment(state.Commissions,index,Day,clientId,itemId,units,amount,token);
        if(!result.Changed)return;
        FlushCommissionRewards();ObserveFollowup();var entry=state.Commissions.Entries[index];message=entry.Status!=CommissionStatus.Completed?result.Message:entry.PendingResearch?"委托完成 · 1点资料待归档，研究消耗后自动领取":"委托完成 · 原版柜台已付款，1点研究资料已归档";WriteState();nextDraw=0;nextCargoScan=0;
    }
    [HideFromIl2Cpp] private bool FlushCommissionRewards()
    {
        bool changed=false;
        foreach(var entry in state!.Commissions.Entries)
            if(entry.Status==CommissionStatus.Completed&&entry.PendingResearch&&BusinessRules.StoreResearchReward(B,1)==1)
            {entry.PendingResearch=false;changed=true;}
        return changed;
    }
    [HideFromIl2Cpp] private void InviteCommission(int index,int stage)
    {
        if(state==null||index is <0 or >2||state.Stories.Threads[index].Phase<1){message="先在柜台认识这位伙伴，再谈实物委托";return;}
        if(NightOrderHasPending()){message="夜班买家已有到访安排，先完成或取消它";return;}
        if(LostCallHasPending()){message="余灯已有来店安排，先完成或取消它";return;}
        if(state.Stories.Threads.Any(t=>t.QueuedVisitScene>=0)){message="已有伙伴到访预约，先完成或取消它";return;}
        message=PhoneCommissionClients.QueueVisit(state.Commissions,index,stage,Day);WriteState();
        if(state.Commissions.Entries[index].QueuedStage==stage)Close();
    }
    [HideFromIl2Cpp] private long CommissionStock(CommissionEntry entry)
    {
        RefreshCargo();
        if(entry!=state!.Commissions.Entries[0])return cargo.Where(c=>c.Id==entry.ItemId).Sum(c=>(long)c.Units);
        long eligible=0;var seen=new HashSet<int>();var owned=EmporiumEntry.Instance.GetAllOwnedItems(false,true);
        foreach(var item in owned)
            if(item!=null&&item.identifier==entry.ItemId&&GeneralHelper.IsItemEffectivelyOwned(item)&&item.unitCount>0&&seen.Add(item.uniqueId)&&PhoneCommissionClients.MeetsGoodsRequirement(0,item))eligible+=item.unitCount;
        return eligible;
    }
    [HideFromIl2Cpp] private void DrawCommissions()
    {
        if(selectedCommission<0)
        {
            Btn(36,183,179,34,"熟人委托",()=>{});Btn(225,183,179,34,"夜班求购",OpenNightOrders);Fill(36,216,179,2,Gold);
            Small(42,231,350,"熟人带着具体需求来，不再只看成交次数");
            for(int i=0;i<3;i++)
            {
                int index=i;var entry=state!.Commissions.Entries[i];bool known=state.Stories.Threads[i].Phase>=1;float y=267+i*124;
                Surface(36,y,368,109,"commission:"+i,()=>selectedCommission=index);
                Label(50,y+11,330,31,CommissionRules.GetName(i),true);
                Small(50,y+48,330,known?entry.ItemName+" ×"+entry.RequiredUnits:"先在柜台认识这位伙伴");
                Small(50,y+83,330,known?CommissionStatusText(entry):"保留原任务 · 见面后开放实物委托");
            }
            Small(42,652,350,"每位伙伴一个委托；原版议价和付款照常");return;
        }
        int selected=selectedCommission;var order=state!.Commissions.Entries[selected];
        bool unlocked=state.Stories.Threads[selected].Phase>=1;
        Label(42,187,235,37,CommissionRules.GetName(selected),true);Btn(288,187,116,34,"委托列表",()=>selectedCommission=-1);
        view!.Card(36,234,368,110,"要找的实物",order.ItemName.Length>0?order.ItemName+" ×"+order.RequiredUnits:"正在同步货品目录",CommissionStatusText(order),Gold);
        long owned=order.ItemId.Length>0?CommissionStock(order):0;
        int remaining=Math.Max(0,order.RequiredUnits-order.DeliveredUnits);
        Fill(36,359,368,181,Panel);Small(49,372,340,$"可交同款 {owned} · 已交 {order.DeliveredUnits}/{order.RequiredUnits}");
        string instruction=!unlocked?"先回任务页，邀请这位伙伴来柜台，选一条经营路线。认识之后再谈这份具体委托；旧任务无需重做。":CommissionInstruction(selected,order,owned);
        Label(49,407,340,120,instruction);
        if(!unlocked)Btn(36,556,368,41,"先去认识伙伴",()=>{state.ActiveStory=selected;taskListMode=false;Navigate("任务");WriteState();});
        else if(order.QueuedStage>=0)Btn(36,556,368,41,"回柜台等候到访",Close);
        else if(order.Status==CommissionStatus.Accepted)
            Btn(36,556,368,41,$"预约来店收货 · 还需 {remaining} 件",()=>InviteCommission(selected,1),owned>0&&Day<=order.Deadline);
        else if(order.Status is CommissionStatus.Unaccepted or CommissionStatus.Deferred)
            Btn(36,556,368,41,Day<order.AvailableDay?$"D{order.AvailableDay} 再谈委托":"邀请来柜台谈委托",()=>InviteCommission(selected,0),Day>=order.AvailableDay&&order.ItemId.Length>0);
        else if(selected==2&&order.Status==CommissionStatus.Completed&&FollowupVisible())Btn(36,556,368,41,"洛克修好货梯了 · 查看回访",OpenFollowupTask);
        else Btn(36,556,368,41,"回柜台继续营业",Close);
        Btn(36,611,179,35,"查看货柜",()=>Navigate("货柜"));Btn(225,611,179,35,"联系供应商",()=>Navigate("供应商"));
        if(order.QueuedStage>=0)Btn(36,660,368,34,"取消本次到访预约",()=>{message=PhoneCommissionClients.CancelVisit(state.Commissions,selected);WriteState();});
        else if(unlocked&&order.Status==CommissionStatus.Accepted)
            Btn(36,660,368,34,"商量延期或取消",()=>InviteCommission(selected,0));
    }
    [HideFromIl2Cpp] private string CommissionStatusText(CommissionEntry order)=>order.Status switch
    {
        CommissionStatus.Accepted=>$"已接单 · D{order.Deadline} 当天收工前交齐",
        CommissionStatus.Deferred=>$"已约改天再谈 · D{order.AvailableDay} 开放",
        CommissionStatus.Declined=>"已婉拒 · 旧任务与合作保留",
        CommissionStatus.Completed=>order.PendingResearch?"交付完成 · 1点资料待归档":"交付完成 · 资料已归档",
        CommissionStatus.Expired=>"时限已过 · 对方已另找货源",
        _=>"尚未接单 · 到柜台再决定"
    };
    [HideFromIl2Cpp] private string CommissionInstruction(int index,CommissionEntry order,long owned)
    {
        if(order.QueuedStage>=0)return "先照顾柜台前的顾客，保持营业。对方会在柜台空闲时到访；这次预约可以取消，委托进度保留。";
        string person=StoryRules.GetTitle(index);
        if(order.Status==CommissionStatus.Completed)return person+"已经把这批货带回去了。实物按柜台成交价付款。"+(order.PendingResearch?"资料已满，1点额外奖励会保留，研究消耗后自动归档；重开手机不会丢失。":"1点额外研究资料只发一次；后续继续走原来的伙伴路线。");
        if(order.Status==CommissionStatus.Declined)return person+"理解你要优先顾自己的生意，已经去找其他货源。拒绝这次供货不会取消原任务或扣钱。";
        if(order.Status==CommissionStatus.Expired)return person+"没有等到全部补给，已经找了替代货源。交出的部分按实际成交保留收入；未交出的货仍属于你。";
        if(order.Status==CommissionStatus.Accepted)
        {
            if(index==0)return "需满装至少500ml的标准瓶装水，普通饮水或更好。预约雨鸦收货，把商品放到原版称重柜台，正常议价付款；空瓶、白送、卖给别人都不算。大组先拆分。";
            return (owned==0?"先找齐指定的同款实物；可从供应商进货。":$"货柜中已有同款，最多还要 {order.RequiredUnits-order.DeliveredUnits} 件。")+
                "预约对方收货，把商品放到原版称重柜台，照常议价后出售。卖给别的顾客不算交付；数量大时先拆分。";
        }
        return index switch
        {
            0=>"雨鸦要给两位老人送饮水，找两瓶同款，每瓶满装至少500ml普通饮水或更好。到柜台再决定接单、改天再谈或婉拒；接单后有两天找货。",
            1=>"阿岚需要一份普通医疗物资补诊所储备。到柜台再决定是否帮忙；接单后正常议价收款，供货不要求白送。",
            _=>"洛克要找一份电子元件修卡住的货梯。到柜台谈好再找货；没有现货也可婉拒，原来的合作不会受影响。"
        };
    }
    [HideFromIl2Cpp] private void OpenDailyGoal()
    {
        if(state==null)return;
        if(state.PendingSupplier>=0){Navigate("供应商");return;}
        if(state.PendingRecommendation){Navigate("商脉");return;}
        if(Store.dayUntilRent<=2&&Store.GetCash()<Store.GetRentValue()){Navigate("周转指引");return;}
        int active=PreferredCommission();
        if(active>=0){OpenCommissions(active);return;}if(PreferNightOrder()){OpenNightOrders();return;}if(PreferLostCall()){OpenLostCall();return;}if(PreferFollowup()){OpenFollowupTask();return;}Navigate("任务");
    }
    [HideFromIl2Cpp] private int PreferredCommission()
    {
        int best=-1;
        for(int i=0;i<3;i++)
        {
            var entry=state!.Commissions.Entries[i];if(entry.QueuedStage>=0)return i;
            if(entry.Status==CommissionStatus.Accepted&&(best<0||entry.Deadline<state.Commissions.Entries[best].Deadline))best=i;
        }
        return best;
    }
    [HideFromIl2Cpp] private void DrawWorkingCapital()
    {
        long gap=Math.Max(0,(long)Store.GetRentValue()-Store.GetCash());RefreshCargo();
        view!.Card(36,188,368,108,"距离下次租金",gap>0?$"还差 {Money(gap)}":"现金已够租金",$"余 {Math.Max(0,Store.dayUntilRent)} 天 · 先留出经营现金",Gold);
        Fill(36,313,368,199,Panel);Small(49,328,340,$"货柜现有 {cargo.Count} 种实物");
        Label(49,367,340,131,"先卖现有货，再决定要不要进货。\n\n货柜可以查同款数量与已知采购成本。实物委托可能有买家，但仍要在柜台议价成交；股票和借款需要考虑费用与还款。");
        Btn(36,534,179,41,"查看货柜",()=>Navigate("货柜"));Btn(225,534,179,41,"实物委托",()=>OpenCommissions(-1));
        Btn(36,593,179,35,"查看现金流水",()=>Navigate("账本"));Btn(225,593,179,35,"查看借款条件",()=>Navigate("借钱"));
        Small(42,653,350,"指引只核对现状，商品和现金仍由你决定");
    }
}
