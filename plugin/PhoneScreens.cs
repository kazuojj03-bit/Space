using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
namespace SpacePhone;
public sealed partial class PhoneBehaviour
{
    private static readonly Color Accent=new(.42f,.72f,.66f),Gold=new(.79f,.65f,.39f),Up=new(.84f,.49f,.38f),Down=new(.38f,.67f,.54f),Panel=new(.07f,.14f,.16f,.98f);
    [HideFromIl2Cpp] private void Fill(float x,float y,float w,float h,Color color)=>view!.Fill(x,y,w,h,color);
    [HideFromIl2Cpp] private void Label(float x,float y,float w,float h,string value,bool large=false)=>view!.Label(x,y,w,h,value,large);
    [HideFromIl2Cpp] private void Small(float x,float y,float w,string value)=>view!.Label(x,y,w,23,value,false,false,13);
    [HideFromIl2Cpp] private void Btn(float x,float y,float w,float h,string value,Action action,bool enabled=true)=>view!.Button(x,y,w,h,value,()=>{if(pendingAction==null)pendingAction=action;},enabled);
    [HideFromIl2Cpp] private void Surface(float x,float y,float w,float h,string key,Action action,Color? color=null,bool enabled=true)=>view!.Surface(x,y,w,h,key,()=>{if(pendingAction==null)pendingAction=action;},color??Panel,enabled);
    [HideFromIl2Cpp] private void Navigate(string destination)
    {
        if(page=="短视频")browser.Hide();page=destination;videoStarted=Time.unscaledTime;nextDraw=0;confirmAction=null;ledgerPage=0;businessPage=0;contactsPage=0;selectedContact=-1;selectedStory=-1;message=previousMessage="";toastUntil=0;
        if(destination=="股票"){stockDetail=false;stockTab=0;}
        if(destination=="短视频")mediaTab="station";
        Plugin.Instance.Log.LogInfo("Phone app opened: "+page);
    }
    [HideFromIl2Cpp] private void Ask(string title,string body,Action action){confirmTitle=title;confirmBody=body;confirmAction=action;nextDraw=0;}
    [HideFromIl2Cpp] private string Money(long amount)=>amount.ToString("N0");
    [HideFromIl2Cpp] private long Portfolio()=>state==null?0:Enumerable.Range(0,4).Sum(i=>(long)state.Shares[i]*Price(i,Day));
    [HideFromIl2Cpp] private string Headlines()
    {
        var events=StoreStation.Instance.storeEventManager?.GetActiveEvents();
        if(events!=null&&events.Count>0)return events[0].GetDisplayName();
        return Store.IsClientPresent()?"店门提示：当前顾客正在等候":"供应商通讯在线 · 店铺数据已同步";
    }
    [HideFromIl2Cpp] private void PlayFeedback()
    {
        if(state?.ClickSounds!=true)return;
        uiAudio??=gameObject.AddComponent<AudioSource>();
        if(clickClip==null){var data=new float[1102];for(int i=0;i<data.Length;i++)data[i]=(float)(Math.Sin(i/22050.0*1250*Math.PI*2)*.045*(1-i/(double)data.Length));clickClip=AudioClip.Create("Phone key",data.Length,1,22050,false);clickClip.SetData(data,0);}
        uiAudio.PlayOneShot(clickClip,.65f);
    }
    [HideFromIl2Cpp] private void Draw()
    {
        view??=new PhoneView(gameObject);view.Feedback=PlayFeedback;view.Begin();
        Small(42,42,255,$"ORBIT OS   /   第 {Day:D3} 天");Btn(371,42,30,27,"×",Close);
        if(page=="主页")DrawHome();
        else
        {
            Fill(28,91,384,636,new Color(.032f,.079f,.095f,.98f));
            Label(42,102,310,40,page=="股票"?"轨道交易所":page=="借钱"?"轨道信用社":page=="未来资讯"?"情报台":page,true);
            Small(42,145,355,$"现金 {Money(Store.GetCash())}   ·   资产 {Money(Portfolio())}");
            if(state==null)Label(42,220,350,120,"手机数据暂不可用，请查看运行日志。");
            else switch(page)
            {
                case "借钱":DrawLoans();break;case "供应商":DrawSuppliers();break;
                case "股票":DrawStocks();break;case "未来资讯":DrawFuture();break;
                case "彩票":DrawLottery();break;case "音乐":DrawMusic();break;
                case "短视频":DrawVideos();break;case "设置":DrawSettings();break;case "账本":DrawLedger();break;
                case "商脉":DrawContacts();break;case "履历":DrawCareer();break;case "通讯":case "任务":DrawTasks();break;case "来信":DrawLetter();break;case "更多":DrawMore();break;case "货柜":DrawCargo();break;case "流转簿":DrawBusinessLedger();break;
                case "实物委托":DrawCommissions();break;case "周转指引":DrawWorkingCapital();break;
            }
            Btn(36,756,179,40,"返回主屏",()=>Navigate("主页"));Btn(225,756,179,40,"收起手机",Close);
        }
        if(Time.unscaledTime<toastUntil && !(page=="短视频"&&mediaTab!="station")){float y=page=="主页"?674:690;Fill(36,y,368,page=="主页"?30:49,new Color(.15f,.27f,.27f,.98f));Small(49,y+(page=="主页"?3:12),342,message);}
        if(confirmAction!=null)DrawConfirmation();
        view.End();
    }
    [HideFromIl2Cpp] private void DrawConfirmation()
    {
        view!.ClearHits();Fill(28,91,384,706,new Color(.015f,.025f,.035f,.82f));
        Fill(36,286,368,286,new Color(.07f,.16f,.18f));Fill(36,286,368,3,Gold);
        Label(54,310,330,42,confirmTitle,true);Label(54,361,330,125,confirmBody);
        Btn(52,507,158,46,"取消",()=>confirmAction=null);
        Btn(226,507,158,46,"确认",()=>{var action=confirmAction;confirmAction=null;action?.Invoke();});
    }
    [HideFromIl2Cpp] private void DrawHome()
    {
        Label(42,93,350,36,"深空手机",true);
        view!.Card(36,144,179,111,"可用现金",Money(Store.GetCash()),Store.IsClientPresent()?"当前顾客正在等候":"店铺通讯在线",Accent);
        view.Card(225,144,179,111,"下次租金",Money(Store.GetRentValue()),$"余 {Math.Max(0,Store.dayUntilRent)} 天",Gold);
        Surface(36,269,368,111,"home-goal",OpenDailyGoal);Small(48,280,345,"今天先做这一件 · 点击查看指引");Label(48,315,345,48,NextGoal());
        string[] apps={"任务","拨号","供应商","货柜","股票","更多"};string[] icons={"stories","dialer","supplier","cargo","stocks","more"};
        for(int i=0;i<apps.Length;i++){string app=apps[i];float x=47+i%3*112,y=404+i/3*118;view.Icon(x,y,72,icons[i],()=>{if(pendingAction==null)pendingAction=app=="拨号"?OpenOriginalDialer:()=>Navigate(app);});view.Label(x-9,y+76,90,24,app,false,true,15);}
        Fill(36,636,368,69,Panel);Small(48,645,345,Headlines());Small(48,677,345,"名片号码在拨号里；经营工具都保留在更多");
        Btn(36,756,179,40,"查看任务",()=>Navigate("任务"));Btn(225,756,179,40,"收起手机",Close);
    }
    [HideFromIl2Cpp] private void DrawLoans()
    {
        view!.Card(36,186,368,114,"信用账户",$"待还 {Money(state!.Debt)}",$"每日利息约 {Math.Ceiling(state.Debt*.01):N0} · 日利率 1%",Gold);
        Small(42,316,350,$"可借额度 {Money(Math.Max(0,10000-state.Debt))} / 10,000");
        int[] amounts={100,500,1000};for(int i=0;i<3;i++){int amount=amounts[i];Btn(36+i*126,353,116,48,"借 "+Money(amount),()=>Ask("确认借款",$"到账 {Money(amount)}\n每日按欠款余额计息 1%。\n借款后欠款 {Money(state.Debt+amount)}",()=>Borrow(amount)),state.Debt+amount<=10000);}
        Btn(36,419,179,48,"还款 100",()=>Repay(100),state.Debt>0);Btn(225,419,179,48,"全部还款",()=>Ask("结清贷款",$"支付 {Money(state.Debt)} 后结清手机贷款。",()=>Repay(state.Debt)),state.Debt>0);
        Small(42,489,350,"最近账户流水");
        var entries=state.Ledger.Where(e=>e.Title.Contains("信用社")).Take(3).ToArray();
        for(int i=0;i<entries.Length;i++){Small(42,522+i*34,245,entries[i].Title);Small(290,522+i*34,112,entries[i].Amount.ToString("+0;-0;0"));}
        if(entries.Length==0)Small(42,528,350,"暂无借还款记录");
    }
    [HideFromIl2Cpp] private void DrawSuppliers()
    {
        Small(42,183,350,$"联络费 {SupplierFee()} · {FreeSupplierCalls()} 次免费/日"+(Store.IsClientPresent()?" · 忙碌可预约":""));
        string[] names={"食品供应商","日用品供应商","材料供应商","药品供应商"};string[] goods={"食品 · 饮水 · 农产品","居家用品 · 店铺补给","矿料 · 零件 · 工业材料","药品 · 急救物资"};
        for(int i=0;i<4;i++){int kind=i;float y=223+i*98;Surface(36,y,368,84,"supplier:"+i,()=>RequestSupplier(kind));view!.Icon(48,y+15,46,"supplier",()=>{pendingAction=()=>RequestSupplier(kind);});Label(106,y+13,235,26,names[i]);Small(106,y+47,240,goods[i]);}
        if(state!.PendingSupplier>=0)Btn(36,624,368,38,"取消当前预约",()=>{state.PendingSupplier=-1;WriteState();message="预约已取消";});
    }
    [HideFromIl2Cpp] private void DrawStocks()
    {
        Btn(36,181,116,35,"行情",()=>{stockTab=0;stockDetail=false;});Btn(162,181,116,35,"持仓",()=>{stockTab=1;stockDetail=false;});Btn(288,181,116,35,"成交记录",()=>{stockTab=2;stockDetail=false;ledgerPage=0;});
        Fill(36+stockTab*126,214,116,2,Gold);
        if(stockTab==1){DrawPortfolio();return;}if(stockTab==2){DrawLedger(true);return;}
        if(stockDetail){DrawStockDetail();return;}
        for(int i=0;i<4;i++)
        {
            int company=i,p=Price(i,Day),previous=Price(i,Math.Max(0,Day-1));float change=(p-previous)/(float)previous*100,y=239+i*95;
            Surface(36,y,368,82,"quote:"+i,()=>{selectedStock=company;stockDetail=true;});
            Label(49,y+12,225,28,Economy.Companies[i]);Small(49,y+45,215,Economy.Tickers[i]+$"  ·  持仓 {state!.Shares[i]}");
            view!.Label(285,y+9,105,39,p.ToString("N0"),true,true,23);view.Label(284,y+46,105,23,change.ToString("+0.00;-0.00;0.00")+"%",false,true,14);
            Fill(36,y,3,82,change>=0?Up:Down);
        }
        Small(42,641,352,"日线更新 · 0.2% 手续费 · 游戏模拟行情");
    }
    [HideFromIl2Cpp] private void DrawStockDetail()
    {
        int i=selectedStock,p=Price(i,Day),previous=Price(i,Math.Max(0,Day-1));
        Label(42,231,270,37,Economy.Companies[i],true);Small(42,274,350,Economy.Tickers[i]+$"  最新 {p}  昨收 {previous}");
        CandleChart(i,42,301,354,132);
        Small(42,444,350,$"持有 {state!.Shares[i]} 股 · 成本 {(state.Shares[i]>0?(state.CostBasis[i]/(double)state.Shares[i]).ToString("0.00"):"--")}");
        int[] quantities={1,10,100};for(int q=0;q<3;q++){int quantity=quantities[q];Btn(36+q*126,482,116,38,(tradeQuantity==quantity?"[ ":"")+quantity+" 股"+(tradeQuantity==quantity?" ]":""),()=>tradeQuantity=quantity);}
        int gross=checked(p*tradeQuantity),fee=Economy.Fee(gross);
        Small(42,534,350,$"委托金额 {Money(gross)} · 手续费 {fee}");
        Btn(36,573,179,48,"买入",()=>OrderStock(i,tradeQuantity),Store.GetCash()>=gross+fee);
        Btn(225,573,179,48,"卖出",()=>OrderStock(i,-tradeQuantity),state.Shares[i]>=tradeQuantity);
        Btn(36,635,368,34,"返回行情",()=>stockDetail=false);
    }
    [HideFromIl2Cpp] private void OrderStock(int company,int quantity)
    {
        int day=Day,price=Price(company,day),gross=checked(price*Math.Abs(quantity)),fee=Economy.Fee(gross);
        Ask(quantity>0?"确认买入":"确认卖出",$"{Economy.Companies[company]} ({Economy.Tickers[company]})\n{Math.Abs(quantity)} 股 × {price}\n手续费 {fee}\n{(quantity>0?"合计支付":"预计到账")} {Money(quantity>0?gross+fee:gross-fee)}",()=>{if(Day!=day){message="报价已更新，请重新提交";return;}Trade(company,quantity);});
    }
    [HideFromIl2Cpp] private void CandleChart(int company,float x,float y,float w,float h)
    {
        Fill(x,y,w,h,new Color(.03f,.09f,.11f));
        var bars=Enumerable.Range(Math.Max(0,Day-13),Math.Min(14,Day+1)).Select(d=>Economy.Bar(state!.Seed,d,company)).ToArray();
        int low=bars.Min(b=>b.Low)-2,high=bars.Max(b=>b.High)+2;
        for(int g=1;g<4;g++)view!.Line(x,y+h*g/4,x+w,y+h*g/4,new Color(.16f,.26f,.27f),1);
        for(int b=0;b<bars.Length;b++){var bar=bars[b];float xx=x+12+b*(w-24)/bars.Length;float Y(int v)=>y+h-10-(v-low)/(float)Math.Max(1,high-low)*(h-20);Color color=bar.Close>=bar.Open?Up:Down;view!.Line(xx,Y(bar.High),xx,Y(bar.Low),color,1);Fill(xx-3,Math.Min(Y(bar.Open),Y(bar.Close)),6,Math.Max(2,Math.Abs(Y(bar.Open)-Y(bar.Close))),color);}
        Small(x+6,y+5,w-12,$"日 K     高 {high-2} / 低 {low+2}");
    }
    [HideFromIl2Cpp] private void DrawPortfolio()
    {
        long value=Portfolio(),basis=state!.CostBasis.Sum();view!.Card(36,237,368,110,"证券总市值",Money(value),$"浮动盈亏 {value-basis:+#,0;-#,0;0} · 已实现 {state.RealizedProfit:+#,0;-#,0;0}",Gold);
        for(int i=0;i<4;i++){int company=i;float y=369+i*68;Surface(36,y,368,56,"position:"+i,()=>{selectedStock=company;stockDetail=true;stockTab=0;});Small(49,y+8,239,Economy.Companies[i]+$" · {state.Shares[i]} 股");Small(49,y+31,330,$"市值 {Money((long)state.Shares[i]*Price(i,Day))}   成本 {Money(state.CostBasis[i])}");}
    }
    [HideFromIl2Cpp] private void DrawFuture()
    {
        if(state?.Business.FreeMode!=true){DrawIntelligence();return;}
        view!.Card(36,187,368,96,"未来视界",$"第 {Day+forecastOffset} 天",$"提前 {forecastOffset} 天 · 手机市场模型",Accent);
        Btn(36,302,179,36,"前一天",()=>forecastOffset=Math.Max(1,forecastOffset-1));Btn(225,302,179,36,"后一天",()=>forecastOffset=Math.Min(7,forecastOffset+1));
        for(int i=0;i<4;i++){int p=Price(i,Day),future=Price(i,Day+forecastOffset);float y=357+i*49;Small(42,y,236,Economy.Companies[i]);Small(270,y,134,$"{p} → {future}");Fill(36,y,3,26,future>=p?Up:Down);}
        Small(42,566,350,"已排定的空间站事件");var events=StoreStation.Instance.storeEventManager;
        int count=Math.Min(events.futurEvents.Count,events.futurEventsDay.Count),shown=0;
        for(int e=0;e<count&&shown<2;e++){int scheduled=ScheduledDay(events.futurEvents[e],events.futurEventsDay[e]);if(scheduled<Day)continue;Small(42,597+shown*30,350,$"第 {scheduled} 天 · {events.futurEvents[e].GetDisplayName()}");shown++;}
        if(shown==0)Small(42,600,350,"暂无已排定事件");
    }
    [HideFromIl2Cpp] private void DrawLottery()
    {
        view!.Card(36,188,368,112,"轨道幸运票",state!.PendingPrize>0?$"奖金 {Money(state.PendingPrize)}":"下一天开奖",$"待开奖 {state.Tickets.Count} 张 · 单价 10",Gold);
        Btn(36,321,179,45,"买一张",()=>BuyTickets(1),Store.GetCash()>=10&&state.Tickets.Count<100);
        Btn(225,321,179,45,"买五张",()=>Ask("购买彩票","购买 5 张，共支付 50。\n下一个游戏日开奖。",()=>BuyTickets(5)),Store.GetCash()>=50&&state.Tickets.Count<=95);
        Fill(36,389,368,140,Panel);Small(50,402,339,"最新开奖记录");Label(50,443,339,66,state.LotteryResult,true);
        Btn(36,552,368,49,state.PendingPrize>0?"领取奖金":"等待开奖",ClaimPrize,state.PendingPrize>0);
        Small(42,621,350,"中奖率 30% · 奖金 10 / 50 / 200 / 2000");
    }
    [HideFromIl2Cpp] private void BuyTickets(int quantity){for(int i=0;i<quantity;i++)BuyTicket();}
    [HideFromIl2Cpp] private void ClaimPrize(){int prize=state!.PendingPrize;if(prize<=0)return;ChangeMoney(prize);state.PendingPrize=0;Economy.Record(state,Day,"领取彩票奖金",prize);WriteState();message=$"奖金 {Money(prize)} 已到账";}
    [HideFromIl2Cpp] private void DrawMusic()
    {
        EnsureMusic();string current=music!.clip!=null?music.clip.name:"尚未播放";
        view!.Card(36,187,368,107,"正在播放",music.isPlaying?"播放中":"已暂停",current,Accent);
        string[] themes={"轨道之夜","霓虹回声","流浪星尘"};for(int i=0;i<3;i++){int track=i;Btn(36,315+i*50,368,40,"播放 · "+themes[i],()=>{selectedTrack=track;PlaySynth();});}
        Btn(36,475,116,40,"暂停/继续",()=>{if(music.isPlaying)music.Pause();else music.UnPause();});Btn(162,475,116,40,"下一首",()=>{selectedTrack=(selectedTrack+1)%3;PlaySynth();});Btn(288,475,116,40,"停止",()=>music.Stop());
        Btn(36,530,179,35,"本地音乐",PlayLocal,tracks.Length>0);Btn(225,530,179,35,"下一首本地",()=>{trackIndex++;PlayLocal();},tracks.Length>0);
        Btn(36,580,116,36,"音量 −",()=>SetMusicVolume(-.1f));Small(172,589,111,$"音量 {(int)(music.volume*100)}%");Btn(288,580,116,36,"音量 +",()=>SetMusicVolume(.1f));
        Small(42,638,350,$"本地曲库 {tracks.Length} 首 · Music 文件夹");
    }
    [HideFromIl2Cpp] private void SetMusicVolume(float delta){EnsureMusic();music!.volume=Math.Clamp(music.volume+delta,0,1);state!.MusicVolume=music.volume;WriteState();}
    [HideFromIl2Cpp] private void SetMedia(string service)
    {mediaTab=service;if(service=="station")browser.Hide();else {view?.ClearHits();browser.Open(service,view!.ScreenArea(36,235,368,405));}nextDraw=0;}
    [HideFromIl2Cpp] private void DrawVideos()
    {
        Btn(36,183,116,35,"站内",()=>SetMedia("station"));Btn(162,183,116,35,"抖音",()=>SetMedia("douyin"));Btn(288,183,116,35,"TikTok",()=>SetMedia("tiktok"));
        Fill(mediaTab=="station"?36:mediaTab=="douyin"?162:288,216,116,2,Accent);
        if(mediaTab!="station")
        {
            Fill(36,235,368,405,new Color(.02f,.06f,.08f));Label(52,268,335,125,browser.Status);
            Btn(36,651,86,36,"刷新",()=>browser.Send("reload"));Btn(130,651,86,36,"后退",()=>browser.Send("back"));Btn(224,651,86,36,"放大",()=>browser.Send("expand"));Btn(318,651,86,36,"回手机",()=>browser.Send("attach"));
            Small(42,704,350,"点击放大登录和浏览 · 会话保留在本机");return;
        }
        string[] captions={"仓库机器人下班前的最后一单","空间站矿工：今天也是满载而归","当铺日常：这个零件真的不是偷的","轨道天气：窗外永远是星空","供应商：老板，我到门口了"};int index=videoIndex%5;
        Fill(36,238,368,282,new Color(.025f,.065f,.085f));float time=Time.unscaledTime-videoStarted;
        for(int s=0;s<18;s++){uint hash=Economy.Hash(381,s,index);Fill(45+hash%340,248+(hash/341)%201,2,2,new Color(.43f,.61f,.62f));}
        float x=70+(Mathf.Sin(time*.8f)*.5f+.5f)*215;
        Fill(36,460,368,60,new Color(.1f,.18f,.2f));Fill(x,376+Mathf.Sin(time*5)*3,40,64,new Color(.18f,.57f,.6f));Fill(x+5,387,30,17,new Color(.03f,.07f,.09f));Fill(x+11,391,4,4,Gold);Fill(x+26,391,4,4,Gold);Fill(x+48,411,31,30,new Color(.64f,.46f,.28f));
        Small(49,255,343,$"@轨道生活 · 第 {videoIndex+1} 条");Label(49,488,343,26,captions[index]);
        Btn(36,543,116,39,"上一条",()=>NextVideo(-1));Btn(162,543,116,39,autoVideo?"暂停刷屏":"自动刷屏",()=>autoVideo=!autoVideo);Btn(288,543,116,39,"下一条",()=>NextVideo(1));
        Btn(36,598,368,39,state!.LikedVideos.Contains(index)?"已点赞 · 点击取消":"点赞这条视频",()=>{if(state.LikedVideos.Contains(index))state.LikedVideos.Remove(index);else state.LikedVideos.Add(index);WriteState();message="点赞状态已保存";});
        Small(42,654,350,"切换抖音/TikTok 可浏览真实官方网页");
    }
    [HideFromIl2Cpp] private void DrawSettings()
    {
        view!.Card(36,188,368,106,"系统信息","ORBIT OS 2.0","商脉、履历、情报与通讯",Accent);
        Btn(36,321,368,43,state!.ClickSounds?"按键声音：开启":"按键声音：关闭",()=>{state.ClickSounds=!state.ClickSounds;WriteState();});
        Btn(36,380,179,40,"缩小手机",()=>{state.UiScale=Math.Max(.8f,state.UiScale-.05f);WriteState();});Btn(225,380,179,40,"放大手机",()=>{state.UiScale=Math.Min(1.15f,state.UiScale+.05f);WriteState();});
        Btn(36,438,368,40,"停止背景音乐",()=>music?.Stop());
        Btn(36,496,368,40,"退出网页账户",()=>Ask("退出网页登录","清除本机网页会话，将退出抖音和 TikTok。",()=>{browser.Logout();message="正在清除网页登录会话";}));
        Btn(36,551,368,40,state.Business.FreeMode?"自由模式：开启":"经营模式：开启",()=>Ask("切换手机玩法",state.Business.FreeMode?"启用经营模式：有成本的情报与联络。\n已有现金、股票和存档保留。":"启用自由模式：供应商免费联络、精确未来价。\n经营履历继续记录真实实物交易。",()=>{state.Business.FreeMode=!state.Business.FreeMode;WriteState();message=state.Business.FreeMode?"自由模式已开启":"经营模式已开启";}));
        Label(42,612,350,54,"手机进度随原存档保存。\n网页登录在官方页面完成。");Small(42,666,350,browser.Status);
    }
    [HideFromIl2Cpp] private void DrawLedger(bool trading=false)
    {
        float start=trading?244:194;Small(42,start,350,trading?"已成交委托":"最近现金流水");
        var all=state!.Ledger.Where(e=>!trading||e.Title.Contains("买入")||e.Title.Contains("卖出")).ToArray();
        int pages=Math.Max(1,(all.Length+6)/7);ledgerPage=Math.Clamp(ledgerPage,0,pages-1);var entries=all.Skip(ledgerPage*7).Take(7).ToArray();
        for(int e=0;e<entries.Length;e++){float y=start+37+e*49;Fill(36,y,368,42,Panel);Small(48,y+11,237,$"D{entries[e].Day:D3} · {entries[e].Title}");Small(291,y+11,106,entries[e].Amount.ToString("+#,0;-#,0;0"));}
        if(entries.Length==0)Label(42,start+78,350,80,"暂无记录。完成一次操作后，这里会显示现金变化。");
        if(pages>1){Btn(36,639,116,34,"上一页",()=>ledgerPage--,ledgerPage>0);Small(184,646,94,$"{ledgerPage+1} / {pages}");Btn(288,639,116,34,"下一页",()=>ledgerPage++,ledgerPage+1<pages);}
    }
}
