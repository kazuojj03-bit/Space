using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpacePhone;

/// <summary>Original story clients, using the same counter, arrival and conversation systems as the game.</summary>
internal sealed class PhoneVisitors
{
    internal const string IdPrefix = "spacephone.visitor.";
    private static readonly string[] SpriteKeys = { "spacephone.rain", "spacephone.medic", "spacephone.runner" };
    private static readonly string[] BaseSpriteNames = { "femaleSci_1", "femaleMed_0", "maleEng_0" };
    internal static PhoneVisitors? Live;
    private PlayerStore? store;
    private StoryBook? stories;
    private FollowupBook? followups;
    private ShopActivity activity;
    private int day;
    private IntPtr owner;
    private string run = "";
    private Action<int, int, int>? onChoice;
    private Action<int>? onFollowupChoice;
    private Action? save, closePhone;
    private readonly HashSet<IntPtr> arrived = new();
    // A choice owns its native reply/departure chain until the visitor actually leaves.
    private readonly HashSet<IntPtr> answering = new();
    private readonly Dictionary<IntPtr, string> configured = new();
    private readonly Dictionary<IntPtr, List<Delegate>> pinned = new();
    private readonly Dictionary<IntPtr, float> dismissCleanup = new();
    private static IntPtr spriteOwner;
    private static readonly Sprite?[] sprites = new Sprite?[3];
    private static readonly List<Texture2D> textures = new();

    internal PhoneVisitors() => Live = this;

    internal static bool TryParse(string? identifier, out int index, out int scene)
    {
        index = scene = -1;
        if (identifier == null || !identifier.StartsWith(IdPrefix, StringComparison.Ordinal)) return false;
        var parts = identifier[IdPrefix.Length..].Split('.');
        return parts.Length == 2 && int.TryParse(parts[0], out index) && index is >= 0 and < 3 &&
            int.TryParse(parts[1], out scene) && scene is >= 0 and < 4;
    }

    internal static string Identifier(int index, int scene) => IdPrefix + index + "." + scene;

    internal static bool SceneAllowed(StoryBook book, int index, int scene, int day, ShopActivity activity)
    {
        if (index is < 0 or > 2 || scene is < 0 or > 3 || book.Threads == null || book.Threads.Length != 3) return false;
        var thread = book.Threads[index];
        if (thread == null) return false;
        if (scene == 3) return thread.Phase >= 1 && thread.LastVisitDay != day;
        if (thread.Completed || thread.Rewarded) return false;
        return scene switch
        {
            0 => thread.Phase == 0,
            1 => thread.Phase == 2 || thread.Phase == 1 && StoryRules.Choices(book, index, day, activity).Any(c => c.Key == 2),
            2 => thread.Phase == 3 && StoryRules.Choices(book, index, day, activity).Any(c => c.Key == 3),
            _ => false
        };
    }

    internal static string QueueVisit(StoryBook book, int index, int scene, int day, ShopActivity activity)
    {
        if (!SceneAllowed(book, index, scene, day, activity)) return "这场见面还未准备好，请先完成任务提示。";
        if (book.Threads.Any(t => t.QueuedVisitScene >= 0)) return "已有一位联系人正在等候，请先完成或取消这次预约。";
        book.Threads[index].QueuedVisitScene = scene;
        return StoryRules.GetTitle(index) + "已收到邀请，会在营业且柜台空闲时前来。";
    }

    internal static string CancelVisit(StoryBook book, int index)
    {
        if (index is < 0 or > 2 || book.Threads[index].QueuedVisitScene < 0) return "没有待处理的到访预约。";
        book.Threads[index].QueuedVisitScene = -1;
        return "已取消预约，任务进度保留。";
    }

    /// <summary>Call after phone state ownership has been established. Scheduling never dismisses another client.</summary>
    internal void Tick(PlayerStore player, StoryBook book, FollowupBook followupBook, ShopActivity observedActivity, int currentDay,
        Action<int, int, int> choice, Action<int> followupChoice, Action write, Action close, bool allowScheduling = true)
    {
        if (owner != player.Pointer || run != player.runID)
        {
            owner = player.Pointer; run = player.runID;
            arrived.Clear(); answering.Clear(); configured.Clear(); pinned.Clear(); dismissCleanup.Clear();
        }
        if (!ReferenceEquals(stories, book) || !ReferenceEquals(followups, followupBook))
        {
            // A reload may replace normalized books without changing the native store/run identity.
            // Rebind waiting menus to those books, while retaining a selected reply/departure chain.
            configured.Clear(); arrived.Clear();
        }
        store = player; stories = book; followups = followupBook; activity = observedActivity; day = currentDay;
        onChoice = choice; onFollowupChoice = followupChoice; save = write; closePhone = close;
        var current = player.currentClientInstance?.storeClient;
        foreach (var entry in dismissCleanup.ToArray())
        {
            if (Time.unscaledTime < entry.Value || current?.Pointer == entry.Key || StoreClientMono.Instance?.IsMoving() == true) continue;
            dismissCleanup.Remove(entry.Key); configured.Remove(entry.Key); arrived.Remove(entry.Key); answering.Remove(entry.Key); pinned.Remove(entry.Key);
        }
        if (current != null && TryParse(current.identifier, out int currentIndex, out int currentScene))
        {
            EnsureSprites();
            var oldMenu = current.mainDialogue;
            var manager = DialogUIManager.Instance;
            var activeMenu = manager?.currentDialog;
            bool ownMenuActive = manager != null && (manager.IsChoice() ||
                oldMenu != null && activeMenu != null && oldMenu.Pointer == activeMenu.Pointer);
            bool rebound = Configure(current, currentIndex, currentScene);
            // Loading can start the placeholder before phone state is ready. Replace only this client's
            // old main menu, never a normal customer or the answer/departure chain of a selected choice.
            if (rebound && ownMenuActive && DialogUIManager.Instance != null)
            {
                DialogUIManager.Instance.InterruptDialog();
                current.isMainDialogueStarted = false;
                current.StartMainDialogue();
            }
            if (player.isClientArrived) RecordArrival(current, currentIndex, currentScene);
            return;
        }
        // Keep an already arrived conversation synchronized, but never let a new arrival
        // discard a queued phone click or take over an open confirmation.
        if (!allowScheduling) return;
        if (player.IsClientPresent() || player.storeState != PlayerStore.StoreState.OPEN || player.playerLocation != PlayerStore.PlayerLocation.IN_STORE ||
            player.lockAllFlag > 0 || player.lockNextBellFlag > 0 || player.isHandlingShootout ||
            player.isClientBeingArrested || player.isSecurityAlarmActive ||
            DialogUIManager.Instance?.isConversationActiveFlag > 0 || StoreClientMono.Instance?.IsMoving() == true) return;
        if (!EnsureSprites()) return;
        for (int index = 0; index < book.Threads.Length; index++)
        {
            int scene = book.Threads[index].QueuedVisitScene;
            if (scene < 0) continue;
            if (!SceneAllowed(book, index, scene, day, activity))
            { book.Threads[index].QueuedVisitScene = -1; write(); continue; }
            string id = Identifier(index, scene);
            // AddNextClient retains all normal queued clients. Avoid adding a second copy after a blocked bell.
            bool queued = false;
            foreach (var queuedClient in player.storeClientManager.clientStack) if (queuedClient.identifier == id) { queued = true; break; }
            if (!queued) player.storeClientManager.AddNextClient(Create(index, scene));
            close();
            player.TryCallNextClient();
            // Keep the native entrance animation; no teleport or client replacement is used.
            Plugin.Instance.Log.LogInfo("Story visitor requested: " + id);
            break;
        }
    }

    internal static StoreClient Create(int index, int scene)
    {
        var client = new StoreClient
        {
            identifier = Identifier(index, scene), displayName = StoryRules.GetTitle(index),
            spriteName = SpriteKeys[index], clientIntent = StoreClient.ClientIntent.DIALOGUE,
            clientFaction = StoreClient.FACTION_MIDDLE, bubbleColor = index == 1 ? "#729c91" : "#b3a277",
            dismissable = true, arrestable = false, nonShootable = true,
            isNoContributeToEvidence = true, clientCash = 0, clientBudget = 0,
            useClientBudget = true, isRegularSellDisable = true, cooldown = 0
        };
        client.CompleteClientCreation(true);
        // Set original dialogue after native default initialization.
        if (Live != null && Live.CurrentOwner(Live.owner, Live.run)) Live.Configure(client, index, scene);
        else
        {
            client.mainDialogue = LoadingDialogue(index, client);
            client.repeatDialogue = client.mainDialogue;
        }
        return client;
    }

    private bool CurrentOwner(IntPtr expectedOwner, string expectedRun) =>
        stories != null && store != null && PlayerStore.IsInstanceExist() &&
        PlayerStore.Instance.Pointer == expectedOwner && PlayerStore.Instance.runID == expectedRun &&
        owner == expectedOwner && run == expectedRun;

    private bool CurrentConversation(StoreClient client, IntPtr expectedOwner, string expectedRun,
        StoryBook? expectedStories, FollowupBook? expectedFollowups) =>
        CurrentOwner(expectedOwner, expectedRun) && ReferenceEquals(stories, expectedStories) &&
        ReferenceEquals(followups, expectedFollowups) && store?.currentClientInstance?.storeClient.Pointer == client.Pointer;

    private bool Configure(StoreClient client, int index, int scene)
    {
        // Timer polling can update availability or remaining calls while an answer is on screen.
        // Never interrupt that answer, including the native player-reply bubble before it.
        if (answering.Contains(client.Pointer)) return false;
        int expectedPhase = stories?.Threads[index].Phase ?? -1;
        var expectedStories = stories; var expectedFollowups = followups;
        string followupSignature = index == 2 && scene == 3 && followups != null
            ? ":" + followups.Phase + ":" + followups.Disabled + ":" + followups.Route + ":" + followups.Calls.Count +
              ":" + string.Join(",", FollowupRules.Choices(followups, day).Select(c => c.Key))
            : "";
        string signature = owner + ":" + run + ":" + expectedPhase + followupSignature;
        if (configured.TryGetValue(client.Pointer, out var old) && old == signature) return false;
        configured[client.Pointer] = signature;
        if (!pinned.TryGetValue(client.Pointer, out var keep)) { keep = new List<Delegate>(); pinned[client.Pointer] = keep; }
        IntPtr expectedOwner = owner; string expectedRun = run;
        Action arrival = () =>
        {
            try { if (CurrentOwner(expectedOwner, expectedRun)) RecordArrival(client, index, scene); }
            catch (Exception ex) { Plugin.Error(ex); }
        };
        keep.Add(arrival); client.OnArrival = (Il2CppSystem.Action)arrival;
        Action dismiss = () => dismissCleanup[client.Pointer] = Time.unscaledTime + 3;
        keep.Add(dismiss); client.OnDismiss = (Il2CppSystem.Action)dismiss;
        bool allowed = stories != null && (scene == 3 ? stories.Threads[index].Phase >= 1 : SceneAllowed(stories, index, scene, day, activity));
        if (!allowed)
        { client.mainDialogue = DeferredDialogue(index, client); client.repeatDialogue = client.mainDialogue; return true; }
        if (scene == 3)
        {
            client.mainDialogue = CheckInDialogue(client, index, keep, expectedOwner, expectedRun);
            client.repeatDialogue = client.mainDialogue;
            return true;
        }
        var dialog = new Dialogue().SetText("CLIENT", SceneText(stories!, index, scene));
        dialog.isMainDialog = true;
        var keys = scene == 2 ? new[] { 3 } : new[] { 0, 1 };
        foreach (int key in keys)
        {
            int capturedKey = key;
            string label = ChoiceLabel(index, scene, key);
            Action action = () =>
            {
                try
                {
                    if (!CurrentConversation(client, expectedOwner, expectedRun, expectedStories, expectedFollowups) ||
                        stories!.Threads[index].Phase != expectedPhase || !SceneAllowed(stories, index, scene, day, activity)) return;
                    answering.Add(client.Pointer);
                    onChoice?.Invoke(index, scene, capturedKey);
                    save?.Invoke();
                }
                catch (Exception ex) { Plugin.Error(ex); }
            };
            keep.Add(action);
            var answer = new Dialogue().SetText("CLIENT", AnswerText(index, scene, key));
            Action leave = () =>
            {
                if (CurrentOwner(expectedOwner, expectedRun) && store?.currentClientInstance?.storeClient.Pointer == client.Pointer)
                    DialogUIManager.Instance?.CurrentClientLeave(.6f);
            };
            keep.Add(leave); answer.SetEndAction((Il2CppSystem.Action)leave);
            AddNativeChoice(dialog, new GameItem("spacephone.choice"), label, ChoiceDetail(index, scene, key), answer,
                (Il2CppSystem.Action)action);
        }
        var defer = new Dialogue().SetText("CLIENT", "先忙你的柜台。邀请还在手机里，等你准备好了再联系我。");
        Action deferLeave = () =>
        {
            if (CurrentOwner(expectedOwner, expectedRun) && store?.currentClientInstance?.storeClient.Pointer == client.Pointer)
                DialogUIManager.Instance?.CurrentClientLeave(.6f);
        };
        keep.Add(deferLeave); defer.SetEndAction((Il2CppSystem.Action)deferLeave);
        Action deferAnswer = () =>
        {
            if (CurrentConversation(client, expectedOwner, expectedRun, expectedStories, expectedFollowups)) answering.Add(client.Pointer);
        };
        keep.Add(deferAnswer);
        AddNativeChoice(dialog, new GameItem("spacephone.choice.defer"), "改天再谈", "保留任务进度，不做选择。", defer, (Il2CppSystem.Action)deferAnswer);
        client.mainDialogue = dialog; client.repeatDialogue = dialog;
        client.allDoneDialogue = defer; client.interogationDialogue = defer;
        return true;
    }

    private void RecordArrival(StoreClient client, int index, int scene)
    {
        if (stories == null || store?.currentClientInstance?.storeClient.Pointer != client.Pointer || !arrived.Add(client.Pointer)) return;
        var thread = stories.Threads[index];
        if (thread.QueuedVisitScene < 0 && thread.LastVisitScene == scene && thread.LastVisitDay == day) return;
        thread.QueuedVisitScene = -1; thread.LastVisitScene = scene; thread.LastVisitDay = Math.Max(0, day);
        thread.VisitCount = Math.Min(9999, thread.VisitCount + 1);
        save?.Invoke();
        Plugin.Instance.Log.LogInfo($"Story visitor arrived: {client.identifier}, sprite={StoreClientMono.Instance?.image?.sprite?.name}");
    }

    private static Dialogue DeferredDialogue(int index, StoreClient client)
    {
        var dialog = new Dialogue().SetText("CLIENT", StoryRules.GetTitle(index) + "：这件事先放一放吧。手机上的任务提示会告诉你下一步该做什么。");
        IntPtr expectedOwner = PlayerStore.IsInstanceExist() ? PlayerStore.Instance.Pointer : IntPtr.Zero;
        string expectedRun = PlayerStore.IsInstanceExist() ? PlayerStore.Instance.runID : "";
        Action leave = () =>
        {
            if (PlayerStore.IsInstanceExist() && PlayerStore.Instance.Pointer == expectedOwner && PlayerStore.Instance.runID == expectedRun &&
                PlayerStore.Instance.currentClientInstance?.storeClient.Pointer == client.Pointer)
                DialogUIManager.Instance?.CurrentClientLeave(.6f);
        };
        dialog.SetEndAction((Il2CppSystem.Action)leave);
        return dialog;
    }

    private static Dialogue LoadingDialogue(int index, StoreClient client)
    {
        // A fresh or switched save must not inherit another run's route choices. This harmless menu
        // remains available until Tick binds the correct book and replaces the active main dialogue.
        var dialog = new Dialogue().SetText("CLIENT", StoryRules.GetTitle(index) + "正在整理带来的文件。手机同步好任务记录后，我们就继续聊。");
        dialog.isMainDialog = true;
        AddNativeChoice(dialog, new GameItem("spacephone.choice.loading"), "稍等，我先整理柜台", "任务进度保持不变。", DeferredDialogue(index, client));
        return dialog;
    }

    private Dialogue CheckInDialogue(StoreClient client, int index, List<Delegate> keep, IntPtr expectedOwner, string expectedRun)
    {
        var thread = stories!.Threads[index];
        var expectedStories = stories; var expectedFollowups = followups;
        int expectedStoryPhase = thread.Phase;
        var extraChoices = index == 2 && followups != null ? FollowupRules.Choices(followups, day) : Array.Empty<FollowupChoice>();
        bool hasFollowup = extraChoices.Length > 0;
        string opening = hasFollowup ? FollowupRules.OfferText(followups!, day) : index switch
        {
            0 => "雨鸦把旧复核表翻到新的一页。\n“我路过楼下，来看看你的柜台。哪一步还有疑问？我们把它说清楚。”",
            1 => "阿岚在门口擦了擦鞋底的雨水。\n“诊所那边暂时安静了。你这边怎么样？想聊聊补给，还是接下来该做什么？”",
            _ => "洛克把货运手套塞回口袋。\n“正好经过这一层，进来打个招呼。货路和合作，哪一头还让你犯难？”"
        };
        var first = TextPages(opening, out var dialog);
        first.isMainDialog = true; dialog.isMainDialog = true;
        Action leave = () =>
        {
            if (CurrentOwner(expectedOwner, expectedRun) && store?.currentClientInstance?.storeClient.Pointer == client.Pointer)
                DialogUIManager.Instance?.CurrentClientLeave(.6f);
        };
        keep.Add(leave);
        Action markAnswer = () =>
        {
            if (CurrentConversation(client, expectedOwner, expectedRun, expectedStories, expectedFollowups)) answering.Add(client.Pointer);
        };
        keep.Add(markAnswer);
        if (hasFollowup)
        {
            var expectedBook = followups!;
            var expectedPhase = expectedBook.Phase;
            foreach (var extraChoice in extraChoices)
            {
                int key = extraChoice.Key;
                // A single answer bubble is changed by the validated callback before native playback.
                // Stale clicks therefore cannot announce a route choice that was never committed.
                var answer = new Dialogue().SetText("CLIENT", "我们先把这页记录对上。");
                bool handled = false;
                Action action = () =>
                {
                    try
                    {
                        if (handled) return;
                        if (!CurrentConversation(client, expectedOwner, expectedRun, expectedStories, expectedBook) ||
                            stories!.Threads[index].Phase != expectedStoryPhase || expectedBook.Phase != expectedPhase ||
                            !FollowupRules.Choices(expectedBook, day).Any(c => c.Key == key))
                        {
                            answer.SetText("CLIENT", "这页记录已经更新。这次没有更改路线，也没有消耗联络优惠。先继续营业，下次看手机里的新提示。");
                            answer.SetEndAction((Il2CppSystem.Action)leave);
                            return;
                        }
                        handled = true;
                        answering.Add(client.Pointer);
                        onFollowupChoice?.Invoke(key);
                        bool committed = key == 2 || key == 3 && expectedBook.Phase == FollowupPhase.Completed ||
                            (key is 0 or 1) && expectedBook.Phase == FollowupPhase.Active && expectedBook.Route == key;
                        if (!committed)
                        {
                            answer.SetText("CLIENT", "这页安排暂时没有确认。路线和余下次数保持原样，先回手机查看当前提示，我们改天再谈。");
                            answer.SetEndAction((Il2CppSystem.Action)leave);
                            return;
                        }
                        save?.Invoke();
                        FillTextPages(answer, FollowupAnswer(expectedBook, key), (Il2CppSystem.Action)leave);
                    }
                    catch (Exception ex)
                    {
                        answer.SetText("CLIENT", "这页联络单暂时没能写完。先继续营业，手机里的记录会保留已经确认的进度。");
                        answer.SetEndAction((Il2CppSystem.Action)leave);
                        Plugin.Error(ex);
                    }
                };
                keep.Add(action);
                AddNativeChoice(dialog, new GameItem("spacephone.checkin.followup." + key), extraChoice.Label, extraChoice.Detail,
                    answer, (Il2CppSystem.Action)action);
            }
        }
        string next = thread.Phase switch
        {
            1 => StoryRules.GetMessage(stories, index, day, activity),
            2 => "记录已经准备好了。打开手机任务，邀请我来做一次正式汇报，我们再当面决定长期合作还是独立研究。",
            3 => day > thread.FollowupDay ? "确认材料已经到了。打开手机任务，邀请我正式交接，我们在柜台把最后一步做完。" : "我需要一夜整理确认材料。明天打开手机任务，邀请我来柜台做最后的交接。",
            _ => "这次委托已经完成。合作与研究回报都在手机记录里，接下来的普通顾客和原有故事照常继续。"
        };
        if (index == 2 && followups != null && followups.Phase != FollowupPhase.Locked && !followups.Disabled)
        {
            var guide = FollowupRules.GetGuide(followups, day);
            next = guide.Step + "\n" + guide.Progress + "\n" + guide.Instruction;
        }
        string cooperation = index switch
        {
            0 => "成本、售价和利润要能对上。研究行情只给你一个判断区间，不是必赚的保证；商品的情况仍得你自己查看。",
            1 => "社区需要的是能持续开门的小店。不要为了凑任务把货白送，适当留些现金给租金和下一轮进货。",
            _ => "新货源会占住现金，旧货转不出去也一样。先看手机货柜里有什么，再决定联络哪类供应商。"
        };
        if (thread.Phase == 4) cooperation += thread.Outcome == 0 ? "\n我们约好的长期服务优惠已经生效，每次联络都按手机标明的价格结算。" : "\n你选择了独立研究。材料已归档，不会在每次见面时重复获得。";
        if (index == 2 && followups != null && followups.Phase == FollowupPhase.Completed && !followups.Disabled)
            cooperation += "\n" + FollowupRules.GetOutcomeText(followups);
        AddNativeChoice(dialog, new GameItem("spacephone.checkin.guide"), "再讲讲我该做什么", "把当前目标和下一步说清楚。", Explain(index, next, (Il2CppSystem.Action)leave), (Il2CppSystem.Action)markAnswer);
        AddNativeChoice(dialog, new GameItem("spacephone.checkin.chat"), "聊聊合作", "只聊经营，不更改路线或发放奖励。", Explain(index, cooperation, (Il2CppSystem.Action)leave), (Il2CppSystem.Action)markAnswer);
        AddNativeChoice(dialog, new GameItem("spacephone.checkin.leave"), "我先继续营业", "保留任务进度。", Explain(index, "好，先照顾你的柜台。需要时再从手机邀请我。", (Il2CppSystem.Action)leave), (Il2CppSystem.Action)markAnswer);
        return first;
    }

    private static string FollowupAnswer(FollowupBook book, int key) => key switch
    {
        0 => "那就把材料线接稳。三次有费用的材料供应商联络，每次少十块。先用当天原有的免费额度；我这张单子只记真正付费且成功的联络。",
        1 => "给你留一回机动联络，任一供应商都能用，最多少二十块。先用当天原有的免费额度；等确实需要付费联络时，再用它。",
        2 => "不急着签。你先看看柜台缺什么，这页还留着。明天或以后需要时，再从手机邀请我。",
        _ => "这页货路记录收好了。" + FollowupRules.GetOutcomeText(book)
    };

    private static void AddNativeChoice(Dialogue dialog, GameItem item, string label, string description, Dialogue next,
        Il2CppSystem.Action? action = null)
    {
        // Let the native implementation construct its own tuple/closure layout. The narrowly scoped
        // prefix below skips only the inventory transform of our non-physical speech choice items.
        // The native speech buttons display the next dialogue's text, rather than the item's name.
        // A short player reply therefore owns the visible choice and then leads into the NPC answer.
        var reply = new Dialogue().SetText(DialogUIManager.PLAYER, label);
        reply.SetNextDialogue(next);
        dialog.AddChoice(item, label, description, reply, action);
    }

    private static List<string> TextChunks(string text)
    {
        // Keep original dialogue pages short enough for the game's own counter bubbles.
        var chunks = new List<string>();
        text = (text ?? "").Trim();
        while (text.Length > 110)
        {
            int end = text.LastIndexOf('。', 109, 90);
            if (end < 20) end = 109;
            chunks.Add(text[..(end + 1)]); text = text[(end + 1)..].TrimStart();
        }
        if (text.Length > 0) chunks.Add(text);
        if (chunks.Count == 0) chunks.Add("先继续营业，需要时再联系我。");
        return chunks;
    }

    private static Dialogue TextPages(string text, out Dialogue last)
    {
        var chunks = TextChunks(text);
        var first = new Dialogue().SetText("CLIENT", chunks[0]); last = first;
        for (int i = 1; i < chunks.Count; i++)
        {
            var next = new Dialogue().SetText("CLIENT", chunks[i]); last.SetNextDialogue(next); last = next;
        }
        return first;
    }

    private static void FillTextPages(Dialogue first, string text, Il2CppSystem.Action leave)
    {
        var chunks = TextChunks(text);
        first.SetText("CLIENT", chunks[0]); var current = first;
        for (int i = 1; i < chunks.Count; i++)
        {
            var next = new Dialogue().SetText("CLIENT", chunks[i]); current.SetNextDialogue(next); current = next;
        }
        current.SetEndAction(leave);
    }

    private static Dialogue Explain(int index, string text, Il2CppSystem.Action leave)
    {
        var first = TextPages(text, out var last); last.SetEndAction(leave); return first;
    }

    private static string SceneText(StoryBook book, int index, int scene)
    {
        var thread = book.Threads[index];
        if (scene == 0) return index switch
        {
            0 => "雨鸦把折角的复核表放在柜台上。\n“价格漂亮不代表赚到了钱。你想追两笔同一件货的转售，还是先看看四笔销售的节奏？”\n选好路线后，手机会记下每一步。",
            1 => "阿岚摘下诊所的工作证。\n“我要找一家能持续开门的铺子。先把三次收购和三次销售接起来，还是先认识两类顾客？”\n照常报价，别为了我白送货。",
            _ => "洛克用一枚货运封签压住路线图。\n“断了一条货路，就要多留一个出口。你想买入两种品类再卖两笔，还是先让五笔货转起来？”\n手机会记下新记录，旧生意不补算。"
        };
        if (scene == 1) return index switch
        {
            0 => thread.Route == 0 ? "雨鸦逐笔对上货号和成本。\n“这两件货说得清楚。以后我给你的行情研究留个合作价；或者，我把这套复核方法交给你自己研究。”\n长期合作与研究材料，只能选一种。" : "雨鸦在四行成交记录旁画了道线。\n“柜台还在转，这是好事。长期咨询能补上细节，自己研究也能留住判断。你想走哪条路？”",
            1 => thread.Route == 0 ? "阿岚翻完进货和销售记录，松了口气。\n“两头接起来了。诊所可以替你理顺顾客联络，让推荐便宜些；也可以把经验整理成你自己的研究材料。”" : "阿岚认出成交记录里的两类客人。\n“让不同的人愿意回来，比无条件答应所有人难。我能帮你缩短联络流程，或者把这段经验交给你研究。”",
            _ => thread.Route == 0 ? "洛克把两种货源涂成不同颜色。\n“现在一条路断了，你还有另一条。以后每天多给你一通免费供应商联络，还是把这张货路图交给你自己研究？”" : "洛克沿着五笔出货画出一条实线。\n“货开始移动了。我的长期联络可以省下一点成本；不想绑在一起，我就把记录整理成研究材料。”"
        };
        return index switch
        {
            0 => thread.Outcome == 0 ? "雨鸦递来一张盖过章的复核联络单。\n“合作价从今天生效。情报费用少一成，商品鉴定依旧由你决定。”" : "雨鸦把复核方法装订成册。\n“读完再作判断。它能帮你研究行情，却不能替你承担风险。”",
            1 => thread.Outcome == 0 ? "阿岚递回诊所联络名单。\n“流程已经理顺了。商脉推荐费少十块，我们继续各做各的判断。”" : "阿岚把补给记录的名字遮去。\n“经验交给你，别让进货和售货的任何一头拖垮另一头。”",
            _ => thread.Outcome == 0 ? "洛克在货运单上留下自己的签名。\n“每天多一次免费供应商联络。买什么、押多少现金，仍然由你决定。”" : "洛克把货路图留在柜台。\n“地图会过时。以后哪条路断了，回来看看你自己的记录。”"
        };
    }

    private static string ChoiceLabel(int index, int scene, int key) => scene switch
    {
        0 => index switch { 0 => key == 0 ? "追踪两笔转售" : "观察四笔销售", 1 => key == 0 ? "接上补给周转" : "认识两类顾客", _ => key == 0 ? "拓宽两类货源" : "完成五笔周转" },
        1 => key == 0 ? "建立长期合作" : "保留独立研究",
        _ => "收下，继续营业"
    };
    private static string ChoiceDetail(int index, int scene, int key) => scene switch
    {
        0 => "接取后才开始计数。随时在手机任务里查看进度。",
        1 => key == 0 ? index switch { 0 => "情报研究费用减免 10%。", 1 => "商脉推荐费减免 10。", _ => "每日增加一次免费供应商联络。" } : "获得 1 点研究材料，每日研判上限增至 3。",
        _ => "结局回报只领取一次。"
    };
    private static string AnswerText(int index, int scene, int key) => scene switch
    {
        0 => "就这么定了。你照常经营，手机会留下接取之后的新记录。齐了再邀请我，我想在柜台上亲眼看看。",
        1 => key == 0 ? "那我们把合作写下来。我明天带确认单过来，你在手机任务里邀请我就行。" : "好，保留你的独立判断。我需要一夜整理，明天来柜台把研究材料交给你。",
        _ => "这次合作算落了地。柜台还有别的人等着，下一笔生意由你自己决定。"
    };

    internal static Sprite? RegisteredSprite(SpriteDict dictionary, string key)
    {
        int index = Array.IndexOf(SpriteKeys, key);
        if (index < 0) return null;
        return EnsureSprites(dictionary) ? sprites[index] : null;
    }
    private static bool EnsureSprites() => SpriteDict.Instance != null && EnsureSprites(SpriteDict.Instance);
    private static bool EnsureSprites(SpriteDict dictionary)
    {
        if (spriteOwner == dictionary.Pointer && sprites.All(s => s != null)) return true;
        if (dictionary.spriteDictionary == null) return false;
        if (spriteOwner != dictionary.Pointer)
        {
            foreach (var sprite in sprites) if (sprite != null) Object.Destroy(sprite);
            foreach (var texture in textures) Object.Destroy(texture);
            textures.Clear(); Array.Fill(sprites, null); spriteOwner = dictionary.Pointer;
        }
        for (int index = 0; index < 3; index++)
        {
            if (sprites[index] != null) continue;
            Sprite? source = null;
            foreach (var entry in dictionary.spriteDictionary)
                if (entry.Value != null && entry.Value.name == BaseSpriteNames[index]) { source = entry.Value; break; }
            if (source == null) return false;
            var texture = RenderSprite(source);
            var pixels = texture.GetPixels();
            for (int pixel = 0; pixel < pixels.Length; pixel++)
            {
                Color c = pixels[pixel];
                if (c.a < .01f) continue;
                int x = pixel % texture.width, y = pixel / texture.width;
                // Alter clothing palettes and accessories, preserving native skin, silhouette and shading.
                bool clothing = y < texture.height * .46f;
                if (index == 0)
                {
                    if (clothing && c.g > .32f && Math.Abs(c.r - c.g) < .16f && Math.Abs(c.b - c.g) < .17f)
                    { float shade = (c.r + c.g + c.b) / 3; c = new Color(shade * .93f, shade * .73f, shade * .43f, c.a); }
                    else if (clothing && c.r > c.g * 1.5f && c.r > c.b * 1.15f)
                    { float shade = c.r; c = new Color(shade * .32f, shade * .75f, shade * .72f, c.a); }
                }
                else if (index == 1 && clothing && c.g > c.r * 1.2f && c.g > c.b * 1.03f)
                { float shade = c.g; c = new Color(shade * .70f, shade * .80f, shade * 1.09f, c.a); }
                else if (index == 2 && clothing && c.r > c.g * 1.15f && c.r > c.b * 1.2f)
                { float shade = c.r; c = new Color(shade * .35f, shade * .67f, shade * .94f, c.a); }
                // A restrained native-scale pin/arm band gives each silhouette a recurring identifying detail.
                if (c.a > .98f && x is >= 179 and <= 185 && y is >= 37 and <= 41)
                    c = index == 1 ? new Color(.86f, .47f, .29f, c.a) : new Color(.83f, .73f, .43f, c.a);
                pixels[pixel] = c;
            }
            texture.SetPixels(pixels); texture.Apply(); texture.filterMode = FilterMode.Point;
            texture.name = SpriteKeys[index] + ".texture";
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(source.pivot.x / source.rect.width, source.pivot.y / source.rect.height), source.pixelsPerUnit,
                0, SpriteMeshType.FullRect, Vector4.zero, false);
            sprite.name = SpriteKeys[index]; sprites[index] = sprite; textures.Add(texture);
            dictionary.spriteDictionary[SpriteKeys[index]] = sprite;
        }
        return true;
    }

    private static Texture2D RenderSprite(Sprite source)
    {
        int width = Math.Max(1, Mathf.RoundToInt(source.rect.width)), height = Math.Max(1, Mathf.RoundToInt(source.rect.height));
        var root = new GameObject("SpacePhone visitor palette source"); root.layer = 31;
        var cameraRoot = new GameObject("SpacePhone visitor palette camera");
        RenderTexture? target = null; Material? material = null;
        var previous = RenderTexture.active;
        try
        {
            root.transform.position = new Vector3(12000, 12000, 0);
            var renderer = root.AddComponent(Il2CppType.Of<SpriteRenderer>()).Cast<SpriteRenderer>();
            renderer.sprite = source; material = new Material(Shader.Find("Sprites/Default")); renderer.sharedMaterial = material;
            var camera = cameraRoot.AddComponent(Il2CppType.Of<Camera>()).Cast<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = height / source.pixelsPerUnit / 2;
            camera.aspect = (float)width / height; camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.nearClipPlane = .1f; camera.farClipPlane = 20;
            camera.transform.position = root.transform.position + new Vector3((width / 2f - source.pivot.x) / source.pixelsPerUnit, (height / 2f - source.pivot.y) / source.pixelsPerUnit, -10);
            target = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32); target.antiAliasing = 1; target.Create(); camera.targetTexture = target;
            camera.Render(); RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); texture.Apply(); return texture;
        }
        finally
        {
            RenderTexture.active = previous;
            // Destroy is deferred until frame end. All three characters are generated in one frame,
            // so hide this source now before the next palette camera renders the same staging area.
            root.SetActive(false); cameraRoot.SetActive(false);
            if (target != null) { target.Release(); Object.Destroy(target); }
            if (material != null) Object.Destroy(material);
            Object.Destroy(root); Object.Destroy(cameraRoot);
        }
    }
}

// Native saves remember client identifiers. These factories rebuild dialogue and appearance during load,
// even when normal state binding has not yet reached the phone's next update.
[HarmonyPatch(typeof(StoreClientListDict), nameof(StoreClientListDict.CreateStoreClient))]
internal static class PhoneVisitorFactoryPatch
{
    public static bool Prefix(string __0, ref StoreClient __result)
    {
        if (!PhoneVisitors.TryParse(__0, out int index, out int scene)) return true;
        __result = PhoneVisitors.Create(index, scene); return false;
    }
}

[HarmonyPatch(typeof(StoreClientListDict), nameof(StoreClientListDict.GetClientDisplayName))]
internal static class PhoneVisitorNamePatch
{
    public static bool Prefix(string __0, ref string __result)
    {
        if (!PhoneVisitors.TryParse(__0, out int index, out _)) return true;
        __result = StoryRules.GetTitle(index); return false;
    }
}

[HarmonyPatch(typeof(SpriteDict), nameof(SpriteDict.GetSprite))]
internal static class PhoneVisitorSpritePatch
{
    public static bool Prefix(SpriteDict __instance, string __0, ref Sprite __result)
    {
        if (!__0.StartsWith("spacephone.", StringComparison.Ordinal)) return true;
        var sprite = PhoneVisitors.RegisteredSprite(__instance, __0);
        if (sprite == null) return true;
        __result = sprite; return false;
    }
}

[HarmonyPatch(typeof(OverlayHelper), nameof(OverlayHelper.TransformItemChoice))]
internal static class PhoneVisitorChoiceTransformPatch
{
    public static bool Prefix(GameItem __0)
    {
        string identifier = __0?.identifier ?? "";
        // These identifiers belong exclusively to this mod's native counter dialogue. The original
        // choices and all physical items still use the game's transformation unchanged.
        return !(identifier == "spacephone.choice" || identifier.StartsWith("spacephone.choice.", StringComparison.Ordinal) ||
            identifier.StartsWith("spacephone.checkin.", StringComparison.Ordinal));
    }
}
