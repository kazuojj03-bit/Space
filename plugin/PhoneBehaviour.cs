using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace SpacePhone;

public sealed partial class PhoneBehaviour : MonoBehaviour
{
    public PhoneBehaviour(IntPtr pointer) : base(pointer) { }
    public static PhoneBehaviour? Live;
    private const string SaveKey = "kazuo.spacephone.v1";
    private PhoneState? state;
    private IntPtr owner;
    private string ownerRun = "";
    private bool visible,mouseLocked;
    private bool previousCursorVisible;
    private CursorLockMode previousCursorLock;
    public bool IsOpen => visible;
    private readonly WorldPhone worldPhone=new();
    private readonly PhoneVisitors visitors=new();
    private readonly PhoneCommissionClients commissionClients=new();
    private StoreUIManager? lockedUI;
    private string page = "主页", message = "欢迎使用深空手机";
    private PhoneView? view;
    private float nextDraw;
    private float nextCheck;
    private Action? pendingAction;
    private AudioSource? music;
    private AudioClip? generatedClip, localClip;
    private UnityWebRequest? musicRequest;
    private UnityWebRequestAsyncOperation? musicOperation;
    private string[] tracks = Array.Empty<string>();
    private int trackIndex, videoIndex;
    private bool autoVideo = true;
    private float videoStarted;
    private int forecastOffset=1;
    private int selectedStock,tradeQuantity=1,stockTab,selectedTrack;
    private bool stockDetail;
    private int ledgerPage;
    private int homeTab,businessPage,contactsPage,cargoPage,selectedContact=-1,selectedStory=-1;
    private string mediaTab="station",confirmTitle="",confirmBody="";
    private Action? confirmAction;
    private readonly BrowserClient browser=new();
    private AudioSource? uiAudio;
    private AudioClip? clickClip;
    private readonly Dictionary<int,AudioClip> synthClips=new();
    private float toastUntil;
    private string previousMessage="";
    private int contactInFlight=-1;
    private bool internalCashChange;
    private readonly Dictionary<(int,int),int> prices = new();
    public bool Ready => EmporiumEntry.Instance != null && StoreUIManager.Instance != null && PlayerStore.IsInstanceExist();
    private PlayerStore Store => PlayerStore.Instance;
    private int Day => Math.Max(0, StoreStation.GetDayCounter());

    public void Awake() { Live = this; Object.DontDestroyOnLoad(gameObject); }
    public void Update()
    {
        try
        {
            if (!Ready) { Close(); state = null; owner = IntPtr.Zero; ownerRun = ""; prices.Clear(); return; }
            if (Input.GetKeyDown(KeyCode.F6)) Toggle();
            if(visible&&Input.GetKeyDown(KeyCode.Escape)&&!(page=="短视频"&&mediaTab!="station")){if(confirmAction!=null)confirmAction=null;else if(page!="主页")Navigate("主页");else Close();nextDraw=0;}
            if (Time.unscaledTime >= nextCheck)
            {
                nextCheck = Time.unscaledTime + 1;
                EnsureState();
                ReplaceWorldPhone();
                if (state != null && Economy.Settle(state, Day)) WriteState();
                if(state!=null)visitors.Tick(Store,state.Stories,Activity(),Day,OnVisitorChoice,WriteState,Close);
                if(state!=null)TickCommissions();
                if(state!=null && state.PendingSupplier>=0 && !Store.IsClientPresent() && Store.storeState==PlayerStore.StoreState.OPEN)Summon(state.PendingSupplier,state.PendingSupplierFeeCap);
                if(state?.PendingRecommendation==true && !Store.IsClientPresent() && Store.storeState==PlayerStore.StoreState.OPEN)RecommendCustomer(state.PendingRecommendationFeeCap);
            }
            var action = pendingAction; pendingAction = null;
            if(action!=null) { Plugin.Instance.Log.LogInfo("Executing phone action on "+page); action.Invoke(); nextDraw=0; }
            if (musicOperation != null && musicOperation.isDone)
            {
                if (musicRequest!.result == UnityWebRequest.Result.Success)
                {
                    if (localClip != null) Object.Destroy(localClip);
                    localClip = DownloadHandlerAudioClip.GetContent(musicRequest);
                    music!.clip = localClip; music.loop = true; music.Play(); message = "正在播放本地音乐";
                }
                else message = "音乐加载失败：" + musicRequest.error;
                musicRequest.Dispose(); musicRequest = null; musicOperation = null;
            }
            if (visible && page == "短视频" && autoVideo && Time.unscaledTime - videoStarted > 12) NextVideo(1);
            if(message!=previousMessage){previousMessage=message;toastUntil=Time.unscaledTime+5;nextDraw=0;}
            if(view!=null){view.Tick(Time.unscaledDeltaTime);view.Scale(state?.UiScale??1);}
            browser.Tick(visible&&page=="短视频"&&mediaTab!="station"&&view!=null?view.ScreenArea(36,235,368,405):null);

            if (visible && Time.unscaledTime >= nextDraw)
            {
                nextDraw=Time.unscaledTime+(page=="短视频"?.15f:1f);
                try { Draw(); }
                catch(Exception ex) { Close(); Plugin.Error(ex); }
            }
        }
        catch (Exception ex) { message = "操作失败：" + ex.Message; Plugin.Error(ex); }
    }
    [HideFromIl2Cpp]
    private void EnsureState()
    {
        var player = Store;
        if (owner == player.Pointer && ownerRun == player.runID) { if(state!=null && Economy.Settle(state,Day))WriteState(); return; }
        Close(); owner = player.Pointer; ownerRun = player.runID; prices.Clear(); cargo.Clear();nextCargoScan=0;state = null;
        if (player.modData == null) player.modData = new Il2CppSystem.Collections.Generic.Dictionary<string,string>();
        try
        {
            state = player.modData.ContainsKey(SaveKey) ? Economy.Decode(player.modData[SaveKey]) : new PhoneState { InterestDay = Day };
            message = "欢迎使用深空手机";
            for(int i=0;i<4;i++)if(state.Shares[i]>0&&state.CostBasis[i]==0)state.CostBasis[i]=(long)state.Shares[i]*Economy.Price(state.Seed,Day,i);
            WriteState();
        }
        catch (Exception ex) { message = "手机存档读取失败，交易已停用"; Plugin.Error(ex); }
    }
    public void WriteState()
    {
        if (state == null || !PlayerStore.IsInstanceExist() || owner != Store.Pointer || ownerRun != Store.runID) return;
        Store.modData[SaveKey] = Economy.Encode(state);
    }
    public void Toggle()
    {
        if (visible) { Close(); return; }
        if (!Ready) return;
        EnsureState();
        // Keep the native calling and story conversation flow in control until hangup.
        if (PhoneUIManager.Instance?.isCalling == true || PhoneUIManager.Instance?.isTalking == true ||
            DialoguePhoneUIManager.Instance?.isConversationActiveFlag > 0) return;
        if (PhoneUIManager.Instance != null && PhoneUIManager.Instance.phonePanel.activeSelf) PhoneUIManager.Instance.CloseUI();
        visible = true; page = "主页";confirmAction=null;
        previousCursorVisible=Cursor.visible;previousCursorLock=Cursor.lockState;
        AcquirePhoneInput();
        nextDraw=0;
        Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
    }
    public void Close()
    {
        bool wasVisible=visible;visible=false;pendingAction=null;
        ReleasePhoneInput(wasVisible);
        view?.Show(false);browser.Hide();confirmAction=null;
        if (mouseLocked) { mouseLocked = false; if (lockedUI != null) lockedUI.DecrementMouseFlag(); lockedUI = null; }
        if(wasVisible){Cursor.visible=previousCursorVisible;Cursor.lockState=previousCursorLock;}
    }
    public void LateUpdate() { TickPhoneInput();if(visible){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;view?.ProcessPointer();} }
    [HideFromIl2Cpp]
    private void ReplaceWorldPhone() => worldPhone.Refresh();
    [HideFromIl2Cpp]
    private int Price(int index, int day)
    {
        var key = (index,day);
        if (!prices.TryGetValue(key,out int p)) prices[key] = p = Economy.Price(state!.Seed,day,index);
        return p;
    }
    [HideFromIl2Cpp]
    private void ChangeMoney(int delta)
    {
        long after = (long)Store.GetCash() + delta;
        if (after < 0) throw new InvalidOperationException("余额不足");
        if (after > int.MaxValue) throw new InvalidOperationException("钱包已达上限");
        internalCashChange=true;try{Store.ModCash(delta,false);}finally{internalCashChange=false;}
    }
    [HideFromIl2Cpp] internal void RecordShopCash(int delta)
    {if(internalCashChange||state==null||delta==0||!Ready||owner!=Store.Pointer)return;Economy.Record(state,Day,delta>0?"店铺收入":"店铺支出",delta);WriteState();nextDraw=0;}
    [HideFromIl2Cpp]
    private void Borrow(int amount)
    {
        EnsureState();
        if (state == null) return;
        if ((long)state.Debt + amount > 10000) { message = "借贷额度不足，请先还款"; return; }
        ChangeMoney(amount);state.Debt+=amount;Economy.Record(state,Day,"信用社借款",amount);WriteState();message=$"借款 {amount} 已到账";
    }
    [HideFromIl2Cpp]
    private void Repay(int amount)
    {
        EnsureState();
        if (state == null) return;
        amount = Math.Min(amount,state.Debt);
        if (amount <= 0) { message = "当前没有手机贷款"; return; }
        if (Store.GetCash() < amount) { message = "现金不足，暂时无法还款"; return; }
        ChangeMoney(-amount);state.Debt-=amount;Economy.Record(state,Day,"信用社还款",-amount);WriteState();message=$"已还款 {amount}";
    }
    [HideFromIl2Cpp]
    private void Trade(int company,int quantity)
    {
        if (state == null) return;
        if (quantity < 0 && state.Shares[company] < -quantity) { message = "持仓不足"; return; }
        if ((long)state.Shares[company] + quantity > 1000000) { message = "持仓已达上限"; return; }
        int gross=checked(Price(company,Day)*Math.Abs(quantity)),fee=Economy.Fee(gross);
        if(quantity>0){int total=checked(gross+fee);ChangeMoney(-total);state.CostBasis[company]+=total;state.Shares[company]+=quantity;Economy.Record(state,Day,"买入 "+Economy.Tickers[company],-total);}
        else {int proceeds=gross-fee;long basis=state.Shares[company]>0?state.CostBasis[company]*(-quantity)/state.Shares[company]:0;ChangeMoney(proceeds);state.CostBasis[company]-=basis;state.Shares[company]+=quantity;state.RealizedProfit+=proceeds-basis;Economy.Record(state,Day,"卖出 "+Economy.Tickers[company],proceeds);}
        WriteState();message=$"{(quantity>0?"买入":"卖出")} {Math.Abs(quantity)} 股成交 · 手续费 {fee}";
    }
    [HideFromIl2Cpp]
    private void Summon(int kind,int agreedFee=-1)
    {
        var player = Store;
        if (player.IsClientPresent() || player.lockNextBellFlag > 0 || player.lockAllFlag > 0 || player.storeState!=PlayerStore.StoreState.OPEN)
        { if(state!=null){state.PendingSupplier=kind;state.PendingSupplierFeeCap=Math.Max(0,agreedFee);WriteState();}message="预约已保存，营业空闲后到访";return; }
        int serviceFee=SupplierFee();
        if(agreedFee>=0&&serviceFee>agreedFee){message="联络费变化，请重新确认预约";return;}
        if(player.GetCash()<serviceFee){message="联络费不足";return;}
        var client = kind switch {
            0 => StoreClientListSupplier.CreateScavFoodSeller(),
            1 => StoreClientListSupplier.CreateScavHouseholdSeller(),
            2 => StoreClientListSupplier.CreateScavMaterialSeller(),
            _ => StoreClientListSupplier.CreateScavMedSeller()
        };
        contactInFlight=kind;player.storeClientManager.AddNextClient(client);
        if(state!=null){if(serviceFee>0){ChangeMoney(-serviceFee);Economy.Record(state,Day,"供应商联络费",-serviceFee);}if(state.SupplierCallDay!=Day){state.SupplierCallDay=Day;state.SupplierCallsToday=0;}state.SupplierCallsToday++;state.PendingSupplier=-1;WriteState();}
        Plugin.Instance.Log.LogInfo("Supplier queued: "+kind);
        Close(); player.TryCallNextClient();
        if (player.currentClientInstance != null && player.currentClientInstance.storeClient.identifier == client.identifier)
            StoreClientMono.Instance?.SetImmediatlyArrive();
        message = "供应商已呼叫";
        Plugin.Instance.Log.LogInfo("Supplier current="+player.currentClientInstance?.storeClient.identifier);
    }
    [HideFromIl2Cpp]
    private void BuyTicket()
    {
        if (state == null) return;
        if (state.Tickets.Count >= 100 || state.TicketSerial >= int.MaxValue - 1) { message = "当前不能再购买"; return; }
        ChangeMoney(-10);state.TicketSerial++;state.Tickets.Add(new Ticket {Day=Day,Number=state.TicketSerial});Economy.Record(state,Day,"购买轨道幸运票",-10);
        WriteState(); message = $"票号 {state.TicketSerial:D6}，下个游戏日开奖";
    }

    [HideFromIl2Cpp]
    private void EnsureMusic()
    {
        if(music!=null)return;
        music=gameObject.AddComponent<AudioSource>();music.volume=state?.MusicVolume??.25f;music.loop=true;
        string directory=Path.Combine(BepInEx.Paths.PluginPath,"SpacePhone","Music");
        Directory.CreateDirectory(directory);
        tracks=Directory.GetFiles(directory).Where(p=>new[]{".wav",".ogg",".mp3"}.Contains(Path.GetExtension(p).ToLowerInvariant())).OrderBy(p=>p).ToArray();
    }
    [HideFromIl2Cpp]
    private void PlaySynth()
    {
        EnsureMusic();
        if(!synthClips.ContainsKey(selectedTrack))
        {
            const int sampleRate=22050, seconds=16;
            var data=new float[sampleRate*seconds];
            int[] notes={57,60,64,67,64,60,55,59,62,65,62,59,53,57,60,64,60,57,55,59,62,67,62,59,57,60,64,69,67,64,60,57};
            for(int i=0;i<data.Length;i++)
            {
                double time=(double)i/sampleRate, part=time%0.5;
                double frequency=440*Math.Pow(2,(notes[(int)(time*2)%notes.Length]+selectedTrack*3-69)/12.0);
                double envelope=Math.Min(1,part/.02)*Math.Max(0,1-part/.5);
                data[i]=(float)((Math.Sin(time*frequency*2*Math.PI)*.20+Math.Sin(time*frequency*Math.PI)*.12)*envelope);
            }
            generatedClip=AudioClip.Create("SpacePhone Theme "+selectedTrack,data.Length,1,sampleRate,false);generatedClip.SetData(data,0);synthClips[selectedTrack]=generatedClip;
        }
        music!.clip=synthClips[selectedTrack];music.Play();message="正在播放："+new[]{"轨道之夜","霓虹回声","流浪星尘"}[selectedTrack%3];
    }
    [HideFromIl2Cpp]
    private void PlayLocal()
    {
        EnsureMusic(); if(tracks.Length==0) { message="Music 文件夹内还没有音乐"; return; }
        if(musicRequest!=null) { message="音乐正在加载"; return; }
        string file=tracks[trackIndex%tracks.Length];
        var type=Path.GetExtension(file).ToLowerInvariant() switch { ".ogg"=>AudioType.OGGVORBIS,".mp3"=>AudioType.MPEG,_=>AudioType.WAV };
        musicRequest=UnityWebRequestMultimedia.GetAudioClip(new Uri(file).AbsoluteUri,type); musicOperation=musicRequest.SendWebRequest();
    }
    [HideFromIl2Cpp]
    private void NextVideo(int direction) { videoIndex=Math.Max(0,videoIndex+direction); videoStarted=Time.unscaledTime; }
    public void OnDestroy()
    {
        Close();DisposePhoneInput(); if(Live==this)Live=null;
        view?.Destroy();browser.Dispose();
        worldPhone.Destroy();
        musicRequest?.Abort();musicRequest?.Dispose();
        foreach(var clip in synthClips.Values)Object.Destroy(clip);if(localClip!=null)Object.Destroy(localClip);if(clickClip!=null)Object.Destroy(clickClip);
    }
}
