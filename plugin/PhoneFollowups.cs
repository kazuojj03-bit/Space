using Il2CppInterop.Runtime.Attributes;
namespace SpacePhone;

public sealed partial class PhoneBehaviour
{
    private bool showFollowupTask;
    [HideFromIl2Cpp] private bool OwnsPhoneRun()=>Ready&&state!=null&&owner==Store.Pointer&&ownerRun==Store.runID;
    [HideFromIl2Cpp] private void ObserveFollowup()
    {
        if(state==null)return;
        if(FollowupRules.Unlock(state.Followups,Day,state.Commissions.Entries[2].Status==CommissionStatus.Completed&&state.Stories.Threads[2].Phase>=1))WriteState();
    }
    [HideFromIl2Cpp] private void OnFollowupChoice(int choice)
    {
        if(!Ready||state==null||owner!=Store.Pointer||ownerRun!=Store.runID||
            state.Stories.Threads[2].Phase<1||state.Commissions.Entries[2].Status!=CommissionStatus.Completed||
            !PhoneVisitors.TryParse(Store.currentClientInstance?.storeClient.identifier,out int index,out int scene)||index!=2||scene!=3)return;
        var reply=FollowupRules.Reply(state.Followups,choice,Day);message=reply.Message;
        if(reply.Changed){WriteState();nextDraw=0;}
    }
    [HideFromIl2Cpp] private int SupplierFeeFor(int kind)=>state==null?0:FollowupRules.Quote(state.Followups,Day,kind,SupplierFee());
    [HideFromIl2Cpp] private string SupplierFollowupQuote(int kind,int baseFee,int fee)
    {
        if(state?.Business.FreeMode==true)return "自由模式不收联络费；洛克的安排保留。";
        if(state==null||state.Followups.Phase!=FollowupPhase.Active)return baseFee==0?"今日免费额度可用。":"今天的免费额度已用完。";
        if(baseFee==0)return "今日免费额度先用；洛克的安排保留。";
        return fee<baseFee?$"洛克安排减免 {baseFee-fee}；本次成功联络后消耗1次。":"这条货源不使用洛克的材料线安排。";
    }
    [HideFromIl2Cpp] private void RecordSupplierFollowup(int kind,int baseFee,int finalFee,int feeCap)
    {
        if(state==null||baseFee==0||finalFee>=baseFee)return;
        string key=$"supplier:{Day}:{state.SupplierCallsToday}:{kind}";
        var result=FollowupRules.RecordCall(state.Followups,Day,kind,baseFee,finalFee,key,feeCap);
        if(!result.Changed)Plugin.Instance.Log.LogWarning("Supplier follow-up quote could not be recorded: "+result.Message);
        else Plugin.Instance.Log.LogInfo("Supplier follow-up used: "+result.Message);
    }
    [HideFromIl2Cpp] private bool FollowupVisible()=>state!=null&&state.Followups.Phase!=FollowupPhase.Locked;
    [HideFromIl2Cpp] private bool PreferFollowup()=>FollowupVisible()&&state!.ActiveStory==2&&state.Stories.Threads[2].Phase==4&&state.Followups.Phase!=FollowupPhase.Completed;
    [HideFromIl2Cpp] private void OpenFollowupTask()
    {if(state==null)return;state.ActiveStory=2;taskListMode=false;showFollowupTask=true;Navigate("任务");WriteState();}
    [HideFromIl2Cpp] private void DrawFollowupTask()
    {
        var book=state!.Followups;var guide=FollowupRules.GetGuide(book,Day);var thread=state.Stories.Threads[2];
        bool alreadyMet=guide.CanInvite&&thread.LastVisitDay==Day&&thread.QueuedVisitScene!=3;
        Label(42,188,142,37,"洛克 · 回访",true);
        Btn(189,187,105,34,"原伙伴路线",()=>showFollowupTask=false);
        Btn(304,187,100,34,"伙伴列表",()=>{showFollowupTask=false;taskListMode=true;});
        view!.Card(36,236,368,112,"货路还要有人跑",guide.Step,guide.Progress,Gold);
        Fill(36,362,368,176,Panel);Small(49,373,340,"接下来这样做");
        string instruction=book.Phase==FollowupPhase.Active&&state.Business.FreeMode?"自由模式的联络本来免费，洛克的安排会保留。切回经营模式后，符合品类的付费联络才会消耗；不急着使用也没有惩罚。":alreadyMet?"今天已经见过洛克了。明天再联系他继续这段回访；安排没有截止日期，当前顾客照常交易。":thread.QueuedVisitScene==3?"洛克已收到邀请。先处理当前顾客，柜台空闲时他会来；到店后在原生柜台对白中继续。":guide.Instruction;
        view.Label(49,408,340,119,instruction,false,false,17);
        Btn(36,553,368,43,thread.QueuedVisitScene==3?"回柜台等候":alreadyMet?"今天已经见过 · 明天再联系":guide.PrimaryLabel,()=>
        {
            if(thread.QueuedVisitScene==3){Close();return;}
            if(alreadyMet){Close();return;}
            if(guide.CanInvite){InviteStoryVisitor(2,3);return;}
            if(book.Phase==FollowupPhase.Active){Navigate("供应商");return;}
            if(book.Phase==FollowupPhase.Locked){OpenCommissions(2);return;}Close();
        });
        if(book.Calls.Count>0)
        {
            Small(49,609,340,$"实际联络 · 总减免 {FollowupRules.TotalSaved(book)}");
            for(int i=0;i<Math.Min(3,book.Calls.Count);i++){var call=book.Calls[i];Small(49,637+i*24,340,$"D{call.Day} {FollowupRules.SupplierNames[call.Kind]} · {call.BaseFee} → {call.FinalFee}");}
        }
        else
        {
            Btn(36,614,179,36,"查看实物委托",()=>OpenCommissions(2));
            Btn(225,614,179,36,"查看货柜",()=>Navigate("货柜"));
            Small(49,668,340,"免费联络先用 · 安排没有倒计时");
        }
        if(thread.QueuedVisitScene==3)Btn(36,705,368,31,"取消本次到访预约",()=>{message=PhoneVisitors.CancelVisit(state.Stories,2);WriteState();});
    }
}
