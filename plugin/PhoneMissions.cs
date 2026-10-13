using Il2CppInterop.Runtime.Attributes;
namespace SpacePhone;

public sealed partial class PhoneBehaviour
{
    private bool taskListMode;
    [HideFromIl2Cpp] private void OpenOriginalDialer()
    {
        var original=PhoneUIManager.Instance;
        if(original==null){message="电话线路正在初始化，请稍后再试";return;}
        Close();original.OpenUI();
    }
    [HideFromIl2Cpp] private void InviteStoryVisitor(int index,int scene)
    {
        if(state!.Commissions.Entries.Any(c=>c.QueuedStage>=0)){message="已有实物委托到访预约，先完成或取消它";return;}
        state!.ActiveStory=index;taskListMode=false;
        message=PhoneVisitors.QueueVisit(state.Stories,index,scene,Day,Activity());WriteState();
        if(state.Stories.Threads[index].QueuedVisitScene==scene)Close();
    }
    [HideFromIl2Cpp] private void OnVisitorChoice(int index,int scene,int choice)
    {
        if(!Ready||state==null||owner!=Store.Pointer||ownerRun!=Store.runID||index is <0 or >2)return;
        if(choice<0||scene==3)return;
        if(!PhoneVisitors.SceneAllowed(state.Stories,index,scene,Day,Activity())){message="任务状态已变化，回到手机查看下一步";return;}
        var thread=state.Stories.Threads[index];
        if(scene==1&&thread.Phase==1)
        {
            var report=StoryRules.Reply(state.Stories,index,2,Day,Activity());
            if(!report.Changed){message=report.Message;return;}
        }
        var result=StoryRules.Reply(state.Stories,index,choice,Day,Activity());message=result.Message;
        if(result.Changed)
        {
            state.ActiveStory=index;BusinessRules.StoreResearchReward(B,result.ResearchGain);
            SyncStoryBenefits();WriteState();nextDraw=0;
        }
    }
    [HideFromIl2Cpp] private void DrawMore()
    {
        Small(42,187,350,"常用功能在主屏，经营细节和休闲在这里");
        Btn(36,223,179,35,"经营工具",()=>homeTab=0);Btn(225,223,179,35,"休闲与设置",()=>homeTab=1);Fill(homeTab==0?36:225,256,179,2,Gold);
        string[] apps=homeTab==0?new[]{"借钱","未来资讯","商脉","履历","流转簿","账本"}:new[]{"彩票","音乐","短视频","设置"};
        string[] icons=homeTab==0?new[]{"loan","future","contacts","career","trades","ledger"}:new[]{"lottery","music","video","settings"};
        for(int i=0;i<apps.Length;i++)
        {
            string app=apps[i];float x=47+i%3*112,y=300+i/3*124;
            view!.Icon(x,y,72,icons[i],()=>{if(pendingAction==null)pendingAction=()=>Navigate(app);});
            view.Label(x-9,y+79,90,30,app=="未来资讯"?"情报台":app,false,true,15);
        }
        Label(42,598,350,65,homeTab==0?"货柜与流转簿帮你核对进货。\n专长等熟悉经营后再选择也来得及。":"营业之余，听歌或看看短视频。\n网页账号仍在平台自己的页面登录。");
    }
    [HideFromIl2Cpp] private void DrawTasks()
    {
        int index=taskListMode?-1:state!.ActiveStory;
        if(index<0)
        {
            Small(42,187,237,"先在柜台认识一位伙伴");Btn(288,184,116,34,"实物委托",()=>OpenCommissions(-1));
            string[] portraits={"rain","medic","runner"};
            for(int i=0;i<3;i++)
            {
                int selected=i;var guide=TaskGuide.Build(state!.Stories,i,Day,Activity(),B.ResearchPoints);float y=230+i*138;
                Surface(36,y,368,122,"task:"+i,()=>{state.ActiveStory=selected;taskListMode=false;WriteState();});
                view!.Icon(48,y+18,58,portraits[i],()=>{if(pendingAction==null)pendingAction=()=>{state.ActiveStory=selected;taskListMode=false;WriteState();};});
                Label(120,y+13,266,31,StoryRules.GetTitle(i),true);Small(120,y+51,267,guide.Step);Label(120,y+79,267,32,guide.Progress);
            }
            Small(42,657,350,"交易照常议价；所有路线在接取后开始计数");return;
        }
        var card=TaskGuide.Build(state!.Stories,index,Day,Activity(),B.ResearchPoints);var thread=state.Stories.Threads[index];
        Label(42,188,142,37,card.Heading,true);Btn(189,187,105,34,"实物委托",()=>OpenCommissions(index));Btn(304,187,100,34,"伙伴列表",()=>taskListMode=true);
        view!.Card(36,236,368,112,"当前步骤",card.Step,card.Progress,Gold);
        Fill(36,362,368,151,Panel);Small(49,373,340,"接下来这样做");Label(49,408,340,91,card.Instruction);
        Btn(36,529,368,43,card.PrimaryLabel,()=>RunGuideAction(index,card));
        Btn(36,585,179,36,"联系供应商",()=>Navigate("供应商"));Btn(225,585,179,36,"查看货柜",()=>Navigate("货柜"));
        if(thread.QueuedVisitScene>=0)Btn(36,636,368,35,"取消到访预约",()=>{message=PhoneVisitors.CancelVisit(state.Stories,index);WriteState();});
        else if(thread.Phase>0&&thread.LastVisitDay!=Day)Btn(36,636,179,35,"当面问问",()=>InviteStoryVisitor(index,3));
        if(thread.QueuedVisitScene<0)Btn(225,636,179,35,"查看来信",()=>{selectedStory=index;NavigateLetter();});
    }
    [HideFromIl2Cpp] private void NavigateLetter(){page="来信";confirmAction=null;nextDraw=0;message=previousMessage="";toastUntil=0;}
    [HideFromIl2Cpp] private void DrawLetter()
    {
        int index=Math.Clamp(selectedStory,0,2);Small(42,188,350,StoryRules.GetTitle(index)+" · "+StoryRules.GetRole(index));
        Fill(36,233,368,399,Panel);Label(50,248,340,365,StoryRules.GetMessage(state!.Stories,index,Day,Activity()));
        Btn(36,650,368,35,"回到任务指引",()=>Navigate("任务"));
    }
    [HideFromIl2Cpp] private void RunGuideAction(int index,GuideCard card)
    {
        switch(card.PrimaryKind)
        {
            case GuidePrimaryKind.Invite:case GuidePrimaryKind.CheckIn:
                Ask("邀请 "+StoryRules.GetTitle(index),"对方会来柜台当面谈。\n店铺营业、柜台空闲时到访。\n原来的顾客照常交易；路线在见面时选择。",()=>InviteStoryVisitor(index,card.Scene));break;
            case GuidePrimaryKind.Research:Navigate("未来资讯");break;
            default:Close();break;
        }
    }
}
