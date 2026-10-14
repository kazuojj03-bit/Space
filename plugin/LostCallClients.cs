using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpacePhone;

/// <summary>Four original Yu Deng visits. Native customer movement, negotiation and payments remain authoritative.</summary>
internal sealed class LostCallClients
{
    internal const string IdPrefix = "spacephone.lostcall.visit.";
    internal const string SpriteKey = "spacephone.lostcall.yudeng.original.v1";
    internal static LostCallClients? Live;
    private PlayerStore? store;
    private LostCallBook? book;
    private IntPtr owner;
    private string run = "";
    private int day;
    private Action<int>? onChoice;
    private Action? save;
    private LostCallVisitPlan? pending;
    private string pendingIdentifier = "";
    private bool stockBlocked;
    private readonly Dictionary<IntPtr, Binding> bindings = new();
    // Keep callbacks captured by a departing / stale native client alive. They cannot act on another run.
    private readonly List<Binding> retired = new();
    private const int RetirementLimit = 64;
    private readonly List<NativeTradeCapture> pendingTrades = new();
    private static readonly List<Texture2D> spriteTextures = new();
    private static readonly List<Sprite> spritePins = new();
    private static IntPtr spriteOwner;
    private static Sprite? originalSprite;
    private static readonly HashSet<string> warnings = new(StringComparer.Ordinal);

    private sealed class Binding
    {
        internal StoreClient Client = null!;
        internal LostCallBook Book = null!;
        internal IntPtr Owner;
        internal string Run = "", Token = "", Signature = "";
        internal LostCallVisitPlan Plan;
        internal int SellerUid, BuyerSourceUid;
        internal IntPtr SellerInventory;
        internal bool Arrived, SellerPrepared, SellerFailed, Answering, Cold;
        internal float RetireAfter = -1;
        internal Il2CppSystem.Action? NativeArrival, NativeDismiss, OwnedArrival, OwnedDismiss;
        internal bool NativeDismissObserved;
        internal readonly List<Dialogue> Dialogues = new();
        internal readonly Dictionary<IntPtr, Il2CppSystem.Action> EndActions = new();
        internal readonly Dictionary<IntPtr, Il2CppSystem.Action> ChoiceActions = new();
        internal readonly List<Delegate> Pins = new();
    }

    internal LostCallClients() => Live = this;
    internal static bool TryEnsureContentReady()
    {
        try { return Live?.stockBlocked != true && Live?.retired.Count < RetirementLimit &&
            (LostCallItemDefinition.Live?.TryEnsureReady() == true) && EnsureSprite(); }
        catch (Exception ex) { if (warnings.Add("content:" + ex.GetType().Name)) Plugin.Error(ex); return false; }
    }
    internal static bool TryParse(string? identifier, out LostCallVisitPlan plan)
    {
        plan = default;
        if (identifier == null || !identifier.StartsWith(IdPrefix, StringComparison.Ordinal)) return false;
        var parts = identifier[IdPrefix.Length..].Split('.');
        if (parts.Length != 6 || !int.TryParse(parts[0], out int kind) || kind < 1 || kind > 5 ||
            !int.TryParse(parts[1], out int route) || route < -1 || route > 2 ||
            !int.TryParse(parts[2], out int revision) || revision < 0 ||
            !int.TryParse(parts[3], out int uid) || uid < 0 ||
            !int.TryParse(parts[4], out int earliest) || earliest < 0 || earliest > LostCallRules.MaxDay ||
            parts[5].Length != 32 || parts[5].Any(c => !Uri.IsHexDigit(c))) return false;
        var visitKind = (LostCallVisitKind)kind; var destination = (LostCallRoute)route;
        if (visitKind == LostCallVisitKind.Delivery && (uid <= 0 || route < 0 || revision <= 0) ||
            visitKind != LostCallVisitKind.Delivery && (route != -1 || uid != 0)) return false;
        plan = new(visitKind, destination, revision,
            visitKind is LostCallVisitKind.Acquisition or LostCallVisitKind.Delivery ? LostCallRules.ItemId : "",
            uid, visitKind == LostCallVisitKind.Acquisition ? LostCallRules.SuggestedPurchaseBudget : 0,
            visitKind == LostCallVisitKind.Delivery ? LostCallRules.ProposedBuyerBudget(destination) : 0, earliest);
        return true;
    }

    private static string Identifier(LostCallVisitPlan plan) => IdPrefix + (int)plan.Kind + "." + (int)plan.Route + "." +
        plan.ProposalRevision + "." + plan.RequiredActualUniqueId + "." + plan.EarliestDay + "." + Guid.NewGuid().ToString("N");

    internal static bool HasPending(LostCallBook candidate) => Live != null && Live.CurrentOwner() && ReferenceEquals(Live.book, candidate) &&
        (Live.pending.HasValue || Live.HasNativeVisitor());

    internal static string QueueVisit(LostCallBook candidate, int currentDay)
    {
        var live = Live;
        if (live == null || !live.CurrentOwner() || !ReferenceEquals(live.book, candidate)) return "来电记录还在同步，先继续营业。";
        if (HasPending(candidate)) return "余灯已有一次来访安排，请先完成或取消这次预约。";
        if (live.retired.Count >= RetirementLimit) return "余灯正在收好旧来访记录。先正常结束柜台对白，再稍后预约；实物、收付和已选去向都保留。";
        if (LostCallRules.PlanVisit(candidate, currentDay) is not { } plan) return LostCallRules.GetGuide(candidate, currentDay).Instruction;
        if (!TryEnsureContentReady()) return "录音匣或人物资料尚未就绪，普通经营照常。";
        live.pending = plan; live.pendingIdentifier = Identifier(plan);
        return "余灯收到预约了。营业且柜台空闲时来，不扣款、不替换当前顾客。";
    }

    internal static string CancelVisit(LostCallBook candidate)
    {
        var live = Live;
        if (live == null || !live.CurrentOwner() || !ReferenceEquals(live.book, candidate)) return "来电记录还在同步。";
        if (TryParse(live.store?.currentClientInstance?.storeClient.identifier, out _)) return "余灯已经在来访流程中，请按原生柜台流程正常结束。";
        string token = live.pendingIdentifier;
        if (live.pending == null && !live.HasNativeVisitor()) return "没有待取消的余灯预约。";
        // Remove only our own queued visitor. Do not clear/reorder normal customers or another phone reservation.
        var stack = live.store?.storeClientManager?.clientStack;
        if (stack != null)
            for (int i = stack.Count - 1; i >= 0; i--)
                if (stack[i] != null && TryParse(stack[i].identifier, out _) && (token == "" || stack[i].identifier == token))
                    stack.RemoveAt(i);
        live.pending = null; live.pendingIdentifier = "";
        return "已取消这次预约。实物、实际收付和剧情进度保留，没有罚金。";
    }

    internal void Tick(PlayerStore player, LostCallBook candidate, int currentDay,
        Action<int> choice, Action write, Action close, bool allowScheduling = true)
    {
        bool identityChanged = owner != player.Pointer || run != player.runID;
        bool changedOwner = identityChanged || !ReferenceEquals(book, candidate);
        if (changedOwner)
        {
            foreach (var old in bindings.Values) { old.RetireAfter = Time.unscaledTime + 5; retired.Add(old); }
            bindings.Clear(); pendingTrades.Clear(); pending = null; pendingIdentifier = "";
            if (identityChanged) stockBlocked = false;
            owner = player.Pointer; run = player.runID ?? "";
        }
        store = player; book = candidate; day = currentDay; onChoice = choice; save = write;
        PruneRetired();
        if (!CurrentOwner()) return;
        FlushTrades();
        var current = player.currentClientInstance?.storeClient;
        foreach (var pair in bindings.ToArray())
        {
            var binding = pair.Value;
            if (current?.Pointer == pair.Key) continue;
            bool queued = HasQueued(binding.Token);
            if (!queued && !pendingTrades.Any(t => t.VisitToken == binding.Token))
            {
                if (binding.Arrived)
                {
                    var result = LostCallRules.FinishVisit(candidate, binding.Token);
                    if (result.Changed) write();
                }
                // An invitation canceled before arrival also owns native callbacks; retire it conservatively.
                if (binding.RetireAfter < 0) binding.RetireAfter = Time.unscaledTime + 5;
            }
            if (!queued && binding.RetireAfter >= 0 && Time.unscaledTime >= binding.RetireAfter && StoreClientMono.Instance?.IsMoving() != true)
            { bindings.Remove(pair.Key); retired.Add(binding); }
        }
        // A saved visit can be marked active while no corresponding current/queued native visitor remains.
        // Only a fully bound run and exact native ID absence can end it; a phone click cannot do so.
        if (!candidate.LastVisitFinished && current?.identifier != candidate.LastVisitToken && !HasQueued(candidate.LastVisitToken) &&
            !pendingTrades.Any(t => t.VisitToken == candidate.LastVisitToken))
        { var result = LostCallRules.FinishVisit(candidate, candidate.LastVisitToken); if (result.Changed) write(); }
        if (current != null && TryParse(current.identifier, out var currentPlan))
        {
            EnsureSprite();
            var binding = GetBinding(current, currentPlan);
            if (currentPlan.Kind == LostCallVisitKind.Acquisition) PrepareSeller(player.currentClientInstance!, binding);
            if (player.isClientArrived) RecordArrival(binding);
            var oldMenu = current.mainDialogue; var manager = DialogUIManager.Instance;
            bool conversationOnly = binding.Plan.Kind is LostCallVisitKind.Introduction or LostCallVisitKind.Debrief or LostCallVisitKind.Memory;
            bool ourMenu = manager != null && (conversationOnly && manager.IsChoice() ||
                oldMenu != null && manager.currentDialog != null && oldMenu.Pointer == manager.currentDialog.Pointer);
            bool rebound = Configure(binding);
            if (rebound && ourMenu && !binding.Answering && manager != null)
            { manager.InterruptDialog(); current.isMainDialogueStarted = false; current.StartMainDialogue(); }
            // Arrival/state/text continue syncing even when phone has a pending click or confirmation.
            return;
        }
        if (!allowScheduling || retired.Count >= RetirementLimit || pending is not { } plan) return;
        if (LostCallRules.PlanVisit(candidate, day) is not { } expected || expected != plan)
        { pending = null; pendingIdentifier = ""; return; }
        if (player.IsClientPresent() || player.storeState != PlayerStore.StoreState.OPEN ||
            player.playerLocation != PlayerStore.PlayerLocation.IN_STORE || player.lockAllFlag > 0 || player.lockNextBellFlag > 0 ||
            player.isHandlingShootout || player.isClientBeingArrested || player.isSecurityAlarmActive ||
            DialogUIManager.Instance?.isConversationActiveFlag > 0 || StoreClientMono.Instance?.IsMoving() == true ||
            !TryEnsureContentReady()) return;
        if (!HasQueued(pendingIdentifier)) player.storeClientManager.AddNextClient(Create(pendingIdentifier));
        close(); player.TryCallNextClient();
    }

    private bool CurrentOwner() => book != null && store != null && owner != IntPtr.Zero && PlayerStore.IsInstanceExist() &&
        PlayerStore.Instance.Pointer == owner && (PlayerStore.Instance.runID ?? "") == run &&
        PhoneBehaviour.Live?.OwnsLostCallBook(book) == true;
    private bool Current(Binding binding) => CurrentOwner() && ReferenceEquals(binding.Book, book) &&
        binding.Owner == owner && binding.Run == run && store?.currentClientInstance?.storeClient.Pointer == binding.Client.Pointer &&
        binding.Client.identifier == binding.Token;
    private bool HasQueued(string token)
    {
        var stack = store?.storeClientManager?.clientStack; if (stack == null) return false;
        foreach (var client in stack) if (client != null && client.identifier == token) return true;
        return false;
    }
    private bool HasNativeVisitor()
    {
        if (TryParse(store?.currentClientInstance?.storeClient.identifier, out _)) return true;
        var stack = store?.storeClientManager?.clientStack;
        if (stack != null) foreach (var client in stack) if (client != null && TryParse(client.identifier, out _)) return true;
        return false;
    }
    private void PruneRetired()
    {
        foreach (var binding in retired.ToArray())
        {
            try
            {
                if (binding.RetireAfter < 0) binding.RetireAfter = Time.unscaledTime + 5;
                if (Time.unscaledTime < binding.RetireAfter || pendingTrades.Any(c => c.ClientPointer == binding.Client.Pointer) ||
                    pendingIdentifier == binding.Token || StoreClientMono.Instance?.IsMoving() == true) continue;
                var currentStore = PlayerStore.IsInstanceExist() ? PlayerStore.Instance : null;
                if (currentStore?.currentClientInstance?.storeClient.Pointer == binding.Client.Pointer) continue;
                bool queued = false; var stack = currentStore?.storeClientManager?.clientStack;
                if (stack != null) foreach (var client in stack) if (client != null && client.Pointer == binding.Client.Pointer) { queued = true; break; }
                if (queued) continue;
                var manager = DialogUIManager.Instance;
                if (manager == null || manager.isConversationActiveFlag > 0 || manager.activeCoroutines?.Count > 0 ||
                    IsOurDialogue(binding, manager.currentDialog) || IsOurDialogue(binding, manager.GetCurrentDialog()) ||
                    IsOurDialogue(binding, manager.displayingUncompletedDialog)) continue;
                bool obsoleteRun = currentStore != null && (currentStore.Pointer != binding.Owner || (currentStore.runID ?? "") != binding.Run);
                if (!binding.NativeDismissObserved && binding.Arrived && !obsoleteRun) continue;
                if (!WithdrawOwnedCallbacks(binding)) continue;
                // Only after every installed native callback has been proven detached may its roots retire.
                binding.Dialogues.Clear(); binding.EndActions.Clear(); binding.ChoiceActions.Clear(); binding.Pins.Clear();
                binding.NativeArrival = null; binding.NativeDismiss = null; binding.OwnedArrival = null; binding.OwnedDismiss = null;
                retired.Remove(binding);
            }
            catch (Exception ex)
            { if (warnings.Add("retirement:" + ex.GetType().Name)) Plugin.Instance.Log.LogWarning("Lost-call callback retirement deferred: " + ex.GetType().Name); }
        }
        if (retired.Count >= RetirementLimit)
        {
            // Preserve current visitors, payments and already-owned merchandise. Withdraw only an uncreated invitation.
            if (pending is { } && !HasQueued(pendingIdentifier)) { pending = null; pendingIdentifier = ""; }
            if (warnings.Add("retirement capacity")) Plugin.Instance.Log.LogWarning("Lost-call invitations paused while old native callbacks remain live; existing items, routes and native trades are preserved.");
        }
    }
    private static bool IsOurDialogue(Binding binding, Dialogue? dialogue) => dialogue != null &&
        binding.Dialogues.Any(d => d.Pointer == dialogue.Pointer);
    private static bool SameAction(Il2CppSystem.Action? left, Il2CppSystem.Action? right) =>
        left == null ? right == null : right != null && left.Pointer == right.Pointer;
    private static bool WithdrawOwnedCallbacks(Binding binding)
    {
        // A third-party replacement can still wrap our delegate. Preserve it and keep this binding rooted.
        var arrival = binding.Client.OnArrival;
        if (!SameAction(arrival, binding.OwnedArrival) && !SameAction(arrival, binding.NativeArrival)) return false;
        var dismiss = binding.Client.OnDismiss;
        if (!SameAction(dismiss, binding.OwnedDismiss) && !SameAction(dismiss, binding.NativeDismiss)) return false;
        foreach (var dialogue in binding.Dialogues)
        {
            var end = dialogue.endAction;
            if (end != null && (!binding.EndActions.TryGetValue(dialogue.Pointer, out var owned) || !SameAction(end, owned))) return false;
            var choices = dialogue.choices;
            if (choices != null) foreach (var choice in choices)
            {
                var action = choice?.Item3;
                if (action != null && (!binding.ChoiceActions.TryGetValue(action.Pointer, out var ownedChoice) || !SameAction(action, ownedChoice))) return false;
            }
        }
        if (SameAction(arrival, binding.OwnedArrival)) binding.Client.OnArrival = binding.NativeArrival!;
        if (SameAction(dismiss, binding.OwnedDismiss)) binding.Client.OnDismiss = binding.NativeDismiss!;
        foreach (var dialogue in binding.Dialogues)
        {
            if (dialogue.endAction != null) { dialogue.SetEndAction(null!); if (dialogue.endAction != null) return false; }
            // This is our own inactive dialogue list, preflighted above for exact owned callbacks.
            // Unknown/replaced callback lists never reach this branch. No object is destroyed.
            var choices = dialogue.choices; if (choices != null) { choices.Clear(); if (choices.Count != 0) return false; }
        }
        return SameAction(binding.Client.OnArrival, binding.NativeArrival) && SameAction(binding.Client.OnDismiss, binding.NativeDismiss);
    }

    private bool PlanMatches(Binding binding)
    {
        if (!CurrentOwner() || !ReferenceEquals(binding.Book, book) || binding.Owner != owner || binding.Run != run) return false;
        if (book!.LastVisitToken == binding.Token && !book.LastVisitFinished)
            return book.LastVisitKind == binding.Plan.Kind && book.LastVisitRoute == binding.Plan.Route &&
                book.LastVisitProposalRevision == binding.Plan.ProposalRevision;
        return LostCallRules.PlanVisit(book, day) is { } expected && expected == binding.Plan;
    }

    internal static StoreClient Create(string identifier)
    {
        if (!TryParse(identifier, out var plan)) throw new ArgumentException("Unknown lost-call visitor.", nameof(identifier));
        var live = Live; bool ready = live != null && live.CurrentOwner() &&
            LostCallRules.PlanVisit(live.book!, live.day) is { } expected && expected == plan;
        var intent = ready ? plan.Kind switch { LostCallVisitKind.Acquisition => StoreClient.ClientIntent.SELL,
            LostCallVisitKind.Delivery => StoreClient.ClientIntent.BUY, _ => StoreClient.ClientIntent.DIALOGUE } : StoreClient.ClientIntent.DIALOGUE;
        var client = new StoreClient
        {
            identifier = identifier, displayName = "余灯", spriteName = SpriteKey, clientIntent = intent,
            clientFaction = StoreClient.FACTION_MIDDLE, bubbleColor = "#b39763", dismissable = true, arrestable = false,
            nonShootable = true, isNoContributeToEvidence = true, clientCash = 0, clientBudget = 0, useClientBudget = true,
            isRegularSellDisable = intent != StoreClient.ClientIntent.BUY, isMultiBuyDisabled = true, cooldown = 0
        };
        if (intent == StoreClient.ClientIntent.BUY)
        {
            client.clientBuyingTagList = new Il2CppSystem.Collections.Generic.List<string>();
            client.clientBuyingIdList = new Il2CppSystem.Collections.Generic.List<string>();
            client.clientBuyingIdList.Add(LostCallRules.ItemId); client.SetBudget(plan.ProposedBuyerBudget);
        }
        client.CompleteClientCreation(true);
        if (live != null && ready) live.Configure(live.GetBinding(client, plan));
        else
        {
            // Loading before the phone book binds must not borrow an item or BUY budget from another run.
            // This cold visit stays DIALOGUE for its entire life; it is never switched into SELL/BUY live.
            client.mainDialogue = new Dialogue().SetText("CLIENT", "处置单还在核对。先忙柜台，记录同步后再从手机约我。");
            client.repeatDialogue = client.mainDialogue;
        }
        return client;
    }

    private Binding GetBinding(StoreClient client, LostCallVisitPlan plan)
    {
        if (bindings.TryGetValue(client.Pointer, out var existing) && existing.Token == client.identifier) return existing;
        var nativeArrival = client.OnArrival; var nativeDismiss = client.OnDismiss;
        var previous = retired.LastOrDefault(b => b.Client.Pointer == client.Pointer && b.Token == client.identifier);
        if (previous != null)
        {
            if (SameAction(nativeArrival, previous.OwnedArrival)) nativeArrival = previous.NativeArrival;
            if (SameAction(nativeDismiss, previous.OwnedDismiss)) nativeDismiss = previous.NativeDismiss;
        }
        var binding = new Binding { Client = client, Book = book!, Owner = owner, Run = run, Token = client.identifier,
            Plan = plan, NativeArrival = nativeArrival, NativeDismiss = nativeDismiss,
            Cold = plan.Kind == LostCallVisitKind.Acquisition && client.clientIntent != StoreClient.ClientIntent.SELL ||
                plan.Kind == LostCallVisitKind.Delivery && client.clientIntent != StoreClient.ClientIntent.BUY };
        bindings[client.Pointer] = binding;
        Action arrival = () =>
        {
            try
            {
                if (!Current(binding)) return;
                if (binding.Plan.Kind == LostCallVisitKind.Acquisition && store?.currentClientInstance != null)
                    PrepareSeller(store.currentClientInstance, binding);
                RecordArrival(binding);
                Configure(binding);
                binding.NativeArrival?.Invoke();
            }
            catch (Exception ex) { Plugin.Error(ex); }
        };
        Action dismiss = () =>
        {
            binding.NativeDismissObserved = true;
            try { if (Current(binding)) binding.NativeDismiss?.Invoke(); }
            catch (Exception ex) { Plugin.Error(ex); }
            finally { binding.RetireAfter = Time.unscaledTime + 5; }
        };
        binding.Pins.Add(arrival); binding.Pins.Add(dismiss);
        binding.OwnedArrival = (Il2CppSystem.Action)arrival; binding.OwnedDismiss = (Il2CppSystem.Action)dismiss;
        client.OnArrival = binding.OwnedArrival; client.OnDismiss = binding.OwnedDismiss;
        return binding;
    }

    internal static void PrepareNativeInstance(StoreClient client, StoreClientInstance instance)
    {
        var live = Live;
        if (live == null || !live.CurrentOwner() || !TryParse(client.identifier, out var plan) || plan.Kind != LostCallVisitKind.Acquisition) return;
        live.PrepareSeller(instance, live.GetBinding(client, plan));
    }

    private void PrepareSeller(StoreClientInstance instance, Binding binding)
    {
        if (binding.SellerPrepared || binding.SellerFailed || binding.Cold || !PlanMatches(binding) ||
            binding.Client.clientIntent != StoreClient.ClientIntent.SELL || instance.storeClient?.Pointer != binding.Client.Pointer) return;
        var inventory = instance.gridInv;
        if (inventory == null || inventory.Pointer == IntPtr.Zero) { SellerFailure(binding, "native seller inventory unavailable"); return; }
        var existing = inventory.childItems;
        if (existing != null && existing.Count > 0)
        {
            if (existing.Count == 1 && existing[0] != null && existing[0].identifier == LostCallRules.ItemId &&
                existing[0].unitCount == 1 && existing[0].uniqueId > 0 && !GeneralHelper.IsItemEffectivelyOwned(existing[0]))
            { binding.SellerUid = existing[0].uniqueId; binding.SellerInventory = inventory.Pointer; binding.SellerPrepared = true; return; }
            SellerFailure(binding, "unexpected native seller stock; preserving it without replacement"); return;
        }
        var definition = LostCallItemDefinition.Live;
        var nativeStock = EmporiumEntry.Instance?.GetAllItems();
        if (nativeStock == null) { SellerFailure(binding, "native pre-spawn stock snapshot unavailable"); return; }
        var previousPointers = new HashSet<IntPtr>(); var previousUids = new HashSet<int>();
        foreach (var previous in nativeStock)
            if (previous != null)
            { previousPointers.Add(previous.Pointer); if (previous.uniqueId > 0) previousUids.Add(previous.uniqueId); }
        GameItem? item = null; string reason = "物品定义尚未绑定。";
        try
        {
            if (definition == null || !definition.TrySpawnForSeller(out item, out reason) || item == null)
            { SellerFailure(binding, reason); return; }
            // This exception applies only to the just-returned product of our verified factory.
            // A default-owned new definition is initialized as NPC stock, never as a player grant.
            // Existing inventory, unknown objects and any acquired UID bypass this branch entirely.
            int freshUid = item.uniqueId; IntPtr freshPointer = item.Pointer;
            if (freshPointer == IntPtr.Zero || freshUid <= 0 || item.identifier != LostCallRules.ItemId || item.unitCount != 1 ||
                item.parentInventory != null || previousPointers.Contains(freshPointer) || previousUids.Contains(freshUid) ||
                book?.AcquiredActualUniqueId == freshUid)
            { SellerFailure(binding, "new offer does not retain the proved fresh unplaced identity"); return; }
            if (GeneralHelper.IsItemEffectivelyOwned(item)) GeneralHelper.SetItemOwned(item, false);
            if (item.Pointer != freshPointer || item.uniqueId != freshUid || item.parentInventory != null ||
                GeneralHelper.IsItemEffectivelyOwned(item) || OwnsItem(freshUid))
            { SellerFailure(binding, "new NPC offer ownership initialization not proved"); return; }
            if (!inventory.UncheckedAccept(item) || item.parentInventory?.Pointer != inventory.Pointer ||
                GeneralHelper.IsItemEffectivelyOwned(item) || OwnsItem(freshUid))
            { SellerFailure(binding, "native seller inventory did not accept this unowned offer"); return; }
            binding.SellerUid = freshUid; binding.SellerInventory = inventory.Pointer; binding.SellerPrepared = true;
            Plugin.Instance.Log.LogInfo("Lost call seller stock ready: uid=" + binding.SellerUid + ", client=" + binding.Token);
        }
        catch (Exception ex)
        {
            // The definition retains a spawned object. Stop this run's further offers instead of guessing
            // that an exception means no object was created and silently manufacturing another one.
            SellerFailure(binding, "native new-offer setup paused: " + ex.GetType().Name);
        }
    }

    private void SellerFailure(Binding binding, string reason)
    {
        binding.SellerFailed = true; stockBlocked = true;
        if (warnings.Add(reason)) Plugin.Instance.Log.LogWarning("Lost call offer deferred: " + reason);
        // Do not remove anything, compensate money, create a second offer, or mutate this live client's intent.
    }

    private void RecordArrival(Binding binding)
    {
        if (!Current(binding) || binding.Arrived || !store!.isClientArrived) return;
        var reply = LostCallRules.ObserveVisit(book!, binding.Plan, day, binding.Token);
        if (!reply.Changed && (book!.LastVisitToken != binding.Token || book.LastVisitFinished)) return;
        binding.Arrived = true;
        if (pendingIdentifier == binding.Token) { pending = null; pendingIdentifier = ""; }
        if (reply.Changed) save?.Invoke();
    }

    internal bool Accepts(StoreClient client, GameItem item)
    {
        if (item == null || item.Pointer == IntPtr.Zero || item.identifier != LostCallRules.ItemId || item.unitCount != 1 ||
            !bindings.TryGetValue(client.Pointer, out var binding) || !Current(binding) || !binding.Arrived || binding.Cold ||
            book == null || book.Phase != LostCallPhase.Acquired || book.LastVisitFinished ||
            binding.Plan.Kind != LostCallVisitKind.Delivery || client.clientIntent != StoreClient.ClientIntent.BUY ||
            book.LastVisitToken != binding.Token || book.ProposedRoute != binding.Plan.Route ||
            book.ProposalRevision != binding.Plan.ProposalRevision || book.AcquiredActualUniqueId != item.uniqueId ||
            binding.Plan.RequiredActualUniqueId != item.uniqueId || !GeneralHelper.IsItemEffectivelyOwned(item)) return false;
        binding.BuyerSourceUid = item.uniqueId;
        return true;
    }

    // Snapshot only at an actual current native trade. The caller still proves wallet payment and transfer.
    internal bool TryGetTradeContext(IntPtr clientPointer, bool purchase, int actualUniqueId,
        out string visitToken, out LostCallVisitPlan plan)
    {
        visitToken = ""; plan = default;
        if (!bindings.TryGetValue(clientPointer, out var binding) || !Current(binding) || !binding.Arrived || binding.Cold ||
            book == null || book.LastVisitFinished || book.LastVisitToken != binding.Token || actualUniqueId <= 0) return false;
        if (purchase)
        {
            if (binding.Plan.Kind != LostCallVisitKind.Acquisition || binding.SellerFailed || !binding.SellerPrepared ||
                binding.SellerUid != actualUniqueId || book.Phase != LostCallPhase.AwaitingAcquisition ||
                binding.Client.clientIntent != StoreClient.ClientIntent.SELL) return false;
        }
        else if (binding.Plan.Kind != LostCallVisitKind.Delivery || book.Phase != LostCallPhase.Acquired ||
            book.AcquiredActualUniqueId != actualUniqueId || binding.Plan.RequiredActualUniqueId != actualUniqueId ||
            binding.Plan.Route != book.ProposedRoute || binding.Plan.ProposalRevision != book.ProposalRevision ||
            binding.BuyerSourceUid != actualUniqueId || binding.Client.clientIntent != StoreClient.ClientIntent.BUY) return false;
        visitToken = binding.Token; plan = binding.Plan; return true;
    }

    internal bool TryGetTradeBefore(IntPtr clientPointer, bool purchase, GameItem item,
        out string visitToken, out LostCallVisitPlan plan, out bool effectivelyOwned, out IntPtr parentInventory)
    {
        effectivelyOwned = false; parentInventory = IntPtr.Zero; visitToken = ""; plan = default;
        if (item == null || item.Pointer == IntPtr.Zero || item.identifier != LostCallRules.ItemId || item.unitCount != 1 ||
            !TryGetTradeContext(clientPointer, purchase, item.uniqueId, out visitToken, out plan)) return false;
        effectivelyOwned = GeneralHelper.IsItemEffectivelyOwned(item); parentInventory = item.parentInventory?.Pointer ?? IntPtr.Zero;
        return true; // This is before reputation handling, which may already be after native inventory transfer.
    }

    internal static bool OwnsItem(int uniqueId)
    {
        if (uniqueId <= 0 || Live?.CurrentOwner() != true) return false;
        var all = EmporiumEntry.Instance?.GetAllOwnedItems(true, true);
        if (all != null) foreach (var item in all)
            if (item != null && item.uniqueId == uniqueId && item.identifier == LostCallRules.ItemId &&
                item.unitCount == 1 && GeneralHelper.IsItemEffectivelyOwned(item)) return true;
        return false;
    }

    internal bool ConfirmTransferred(IntPtr clientPointer, bool purchase, GameItem item, string visitToken)
    {
        if (!CurrentOwner() || item == null || item.Pointer == IntPtr.Zero || item.identifier != LostCallRules.ItemId ||
            item.unitCount != 1 || !bindings.TryGetValue(clientPointer, out var binding) || binding.Token != visitToken ||
            binding.Owner != owner || binding.Run != run || !ReferenceEquals(binding.Book, book) || !binding.Arrived) return false;
        if (purchase) return binding.SellerUid == item.uniqueId && item.parentInventory != null &&
            item.parentInventory.Pointer != binding.SellerInventory && GeneralHelper.IsItemEffectivelyOwned(item) && OwnsItem(item.uniqueId);
        if (binding.Plan.RequiredActualUniqueId != item.uniqueId || GeneralHelper.IsItemEffectivelyOwned(item)) return false;
        // Positive destination evidence, rather than merely failing to find it in the player inventory.
        var currentInventory = store?.currentClientInstance?.storeClient.Pointer == clientPointer ? store.currentClientInstance.gridInv : null;
        return Contains(currentInventory, item.uniqueId) || Contains(EmporiumEntry.Instance?.soldElement, item.uniqueId);
    }
    private static bool Contains(GameInventory? inventory, int uid)
    {
        var children = inventory?.childItems;
        if (children != null) foreach (var item in children) if (item != null && item.uniqueId == uid && item.identifier == LostCallRules.ItemId) return true;
        return false;
    }


    internal sealed class NativeTradeCapture
    {
        internal IntPtr Owner, ClientPointer, ParentBefore;
        internal string Run = "", ClientIdentifier = "", VisitToken = "", TradeToken = "";
        internal LostCallBook Book = null!;
        internal GameItem Item = null!;
        internal LostCallVisitPlan Plan;
        internal int UniqueId, Day, Amount;
        internal bool Purchase, OwnedBefore, OurVisit, Submitted;
        internal float ExpiresAt;
    }

    // Called exclusively by the native OnItemTraded prefix. Buttons and scans have no settlement API.
    internal NativeTradeCapture? CaptureTradeBefore(IntPtr clientPointer, bool purchase, GameItem item)
    {
        int liveDay = StoreStation.GetDayCounter();
        if (liveDay < 0 || liveDay > LostCallRules.MaxDay) return null;
        day = liveDay;
        if (!CurrentOwner() || book == null || item == null || item.Pointer == IntPtr.Zero || item.uniqueId <= 0 ||
            item.identifier != LostCallRules.ItemId || item.unitCount != 1 || store?.currentClientInstance?.storeClient.Pointer != clientPointer ||
            store.isClientArrived != true) return null;
        var client = store.currentClientInstance.storeClient;
        bool ownedBefore = GeneralHelper.IsItemEffectivelyOwned(item);
        bool ourVisit = TryParse(client.identifier, out _);
        string token = ""; LostCallVisitPlan plan = default;
        if (ourVisit)
        {
            if (!TryGetTradeContext(clientPointer, purchase, item.uniqueId, out token, out plan)) return null;
        }
        else if (purchase || book.Phase != LostCallPhase.Acquired || book.AcquiredActualUniqueId != item.uniqueId) return null;
        return new NativeTradeCapture { Owner = owner, Run = run, Book = book, ClientPointer = clientPointer,
            ClientIdentifier = client.identifier, Item = item, UniqueId = item.uniqueId, Day = day,
            Purchase = purchase, OwnedBefore = ownedBefore, ParentBefore = item.parentInventory?.Pointer ?? IntPtr.Zero,
            OurVisit = ourVisit, VisitToken = token, Plan = plan,
            TradeToken = "lostcall/native/" + Guid.NewGuid().ToString("N") };
    }

    internal bool SubmitTradeAfter(NativeTradeCapture? capture, int actualAmount, int actualDay)
    {
        if (capture == null || capture.Submitted || actualAmount <= 0 || actualAmount > LostCallRules.MaxAmount ||
            actualDay != capture.Day || !CurrentOwner() || capture.Owner != owner || capture.Run != run ||
            !ReferenceEquals(capture.Book, book) || store?.currentClientInstance?.storeClient.Pointer != capture.ClientPointer ||
            store.currentClientInstance.storeClient.identifier != capture.ClientIdentifier) return false;
        capture.Submitted = true; capture.Amount = actualAmount; capture.ExpiresAt = Time.unscaledTime + 5;
        pendingTrades.Add(capture);
        // Some versions transfer inventory after the reputation callback. Keep the native event pending
        // until there is positive ownership/destination evidence; do not infer success from a missing scan.
        FlushTrades(); return true;
    }

    private void FlushTrades()
    {
        if (!CurrentOwner() || book == null) return;
        int liveDay = StoreStation.GetDayCounter();
        if (liveDay < 0 || liveDay > LostCallRules.MaxDay) return;
        day = liveDay;
        foreach (var capture in pendingTrades.ToArray())
        {
            try
            {
            if (capture.Owner != owner || capture.Run != run || !ReferenceEquals(capture.Book, book) || capture.Day != day)
            { pendingTrades.Remove(capture); continue; }
            bool transferred = capture.OurVisit
                ? ConfirmTransferred(capture.ClientPointer, capture.Purchase, capture.Item, capture.VisitToken)
                : ConfirmOrdinarySale(capture);
            if (!transferred)
            {
                if (Time.unscaledTime >= capture.ExpiresAt)
                {
                    pendingTrades.Remove(capture);
                    Plugin.Instance.Log.LogWarning("Lost call native payment not settled into story without positive transfer proof: " + capture.TradeToken);
                }
                continue;
            }
            var evidence = new LostCallTradeEvidence(capture.Day, capture.UniqueId, LostCallRules.ItemId, 1,
                capture.Amount, capture.TradeToken, true, true);
            var reply = capture.Purchase ? LostCallRules.RecordPurchase(book, capture.VisitToken, evidence)
                : capture.OurVisit ? LostCallRules.RecordDelivery(book, capture.VisitToken, capture.Plan, evidence)
                : LostCallRules.RecordSoldElsewhere(book, evidence);
            pendingTrades.Remove(capture);
            if (!reply.Changed) continue;
            if (!capture.OurVisit)
            {
                // Cancel only a not-yet-arrived own delivery reservation after a real ordinary sale.
                if (!TryParse(store?.currentClientInstance?.storeClient.identifier, out _)) CancelVisit(book);
            }
            save?.Invoke(); Plugin.Instance.Log.LogInfo("Lost call native settlement: " + reply.Message);
            }
            catch (Exception ex)
            {
                pendingTrades.Remove(capture);
                Plugin.Instance.Log.LogWarning("Lost call native event deferred without inferred transfer: " + capture.TradeToken + "; " + ex.GetType().Name);
            }
        }
    }

    private bool ConfirmOrdinarySale(NativeTradeCapture capture)
    {
        if (capture.Purchase || capture.Item == null || capture.Item.Pointer == IntPtr.Zero ||
            capture.Item.uniqueId != capture.UniqueId || GeneralHelper.IsItemEffectivelyOwned(capture.Item)) return false;
        var buyerInventory = store?.currentClientInstance?.storeClient.Pointer == capture.ClientPointer ? store.currentClientInstance.gridInv : null;
        return Contains(buyerInventory, capture.UniqueId) || Contains(EmporiumEntry.Instance?.soldElement, capture.UniqueId);
    }

    private bool Configure(Binding binding)
    {
        if (binding.Answering) return false;
        string signature = book!.Phase + ":" + book.LastVisitToken + ":" + binding.Arrived + ":" + binding.SellerPrepared + ":" + binding.SellerFailed;
        if (binding.Signature == signature) return false;
        binding.Signature = signature;
        var client = binding.Client;
        Action leave = () => { if (Current(binding)) DialogUIManager.Instance?.CurrentClientLeave(.6f); };
        binding.Pins.Add(leave);
        if (binding.Cold || binding.SellerFailed || !PlanMatches(binding))
        {
            client.mainDialogue = Pages(binding,"处置单需要重新核对。先正常结束这次来访，明天或以后再从手机约我。物品和钱保持原样。", leave);
            client.mainDialogue.isMainDialog = true; client.repeatDialogue = client.mainDialogue; client.allDoneDialogue = client.mainDialogue;
            return true;
        }
        if (binding.Plan.Kind == LostCallVisitKind.Acquisition)
        {
            client.mainDialogue = Pages(binding,book.Phase == LostCallPhase.Acquired ?
                "物和钱都对上了。匣子现在是你的货。手机有三个去处，先看实际成本，明天再约也行。" :
                Join(LostCallConversation.Opening(LostCallVisitKind.Acquisition, LostCallRoute.None, book)), null);
            client.mainDialogue.isMainDialog = true; client.repeatDialogue = client.mainDialogue;
            client.acceptDealDialogue = Pages(binding,"价格按你确认的柜台记录结算。故事不替你编成本，也不替你决定要不要买。", null);
            client.allDoneDialogue = Pages(binding,"两头对清楚了。先继续营业，想好去处再从手机约我。", leave);
            return true;
        }
        if (binding.Plan.Kind == LostCallVisitKind.Delivery)
        {
            string opening = book.Phase == LostCallPhase.Acquired ? Join(LostCallConversation.Opening(LostCallVisitKind.Delivery, binding.Plan.Route, book)) : "这只匣子的去向已经记录了。这次不收替代货，下一次我带结果来。";
            client.mainDialogue = Pages(binding,opening, null); client.mainDialogue.isMainDialog = true; client.repeatDialogue = client.mainDialogue;
            client.acceptDealDialogue = Pages(binding,"就是这只匣子，价格按柜台确认的记录来。成交之前去向只是预选，不合适就保留它。", null);
            client.placeRightItemWhenSellingToDialogue = Pages(binding,"编号和你收购时一致。放上议价台，谈好真实价格再成交。", null);
            client.placedWrongItemWhenSellingToDialogue = Pages(binding,"我只收这次处置单里的同一只录音匣。别的匣子或电子件留给普通客人，不用替交。", null);
            client.allDoneDialogue = Pages(binding,"实物到了，款也按柜台结了。明天或以后，闲时约我听听后来发生了什么。", leave);
            return true;
        }
        LostCallChoice[] choices = binding.Plan.Kind == LostCallVisitKind.Introduction
            ? LostCallRules.IntroductionChoices(book, day, binding.Token)
            : binding.Plan.Kind == LostCallVisitKind.Debrief ? LostCallRules.DebriefChoices(book, day, binding.Token) : Array.Empty<LostCallChoice>();
        string text = binding.Plan.Kind switch
        {
            LostCallVisitKind.Introduction => Join(LostCallConversation.Opening(LostCallVisitKind.Introduction, LostCallRoute.None, book)),
            LostCallVisitKind.Debrief => Join(LostCallConversation.Opening(LostCallVisitKind.Debrief, book.CommittedRoute, book)),
            _ => Join(LostCallConversation.Memory(book, day))
        };
        var first = Pages(binding,text, null, out var menu); first.isMainDialog = true; menu.isMainDialog = true;
        foreach (var choice in choices)
        {
            int key = choice.Key; bool handled = false;
            var answer = new Dialogue().SetText("CLIENT", "我们先把这页处置单对上。");
            binding.Dialogues.Add(answer);
            Action apply = () =>
            {
                try
                {
                    var currentChoices = binding.Plan.Kind == LostCallVisitKind.Introduction
                        ? LostCallRules.IntroductionChoices(binding.Book, day, binding.Token)
                        : LostCallRules.DebriefChoices(binding.Book, day, binding.Token);
                    if (handled || !Current(binding) || !binding.Arrived || !currentChoices.Any(c => c.Key == key))
                    { FillPages(binding,answer, "这页记录已变化，刚才没有更改选择或发放回报。先回手机看当前提示。", leave); return; }
                    handled = true; binding.Answering = true;
                    if (key == LostCallRules.AskMemoryChoice) FillPages(binding,answer, Join(LostCallConversation.Reply(key, binding.Book)), leave);
                    else
                    {
                        onChoice?.Invoke(key); save?.Invoke();
                        FillPages(binding,answer, Join(LostCallConversation.Reply(key, binding.Book)), leave);
                    }
                }
                catch (Exception ex) { FillPages(binding,answer, "这一页暂未核对好。已确认的进度保留，先继续营业。", leave); Plugin.Error(ex); }
            };
            binding.Pins.Add(apply); AddChoice(menu, binding, choice.Label, choice.Detail, answer, apply);
        }
        Action pause = () => { if (Current(binding)) binding.Answering = true; };
        binding.Pins.Add(pause);
        AddChoice(menu, binding, binding.Plan.Kind == LostCallVisitKind.Memory ? "先继续营业" : "这次先聊到这里",
            "不扣钱，不转移物品，不重复发放回报。", Pages(binding,"好，先顾柜台。线路有杂音，有时候只是另一头还在认真说话。", leave), pause);
        client.mainDialogue = first; client.repeatDialogue = first; client.allDoneDialogue = Pages(binding,"下回想聊，再从手机约我。", leave);
        return true;
    }

    private static string Join(string[] pages) => pages.Length > 0 ? string.Join("\n", pages) : "记录还在核对，先继续营业。";

    private static void AddChoice(Dialogue menu, Binding binding, string label, string detail, Dialogue next, Action apply)
    {
        var reply = new Dialogue().SetText(DialogUIManager.PLAYER, label); reply.SetNextDialogue(next);
        binding.Dialogues.Add(reply);
        var nativeChoice = (Il2CppSystem.Action)apply;
        binding.ChoiceActions[nativeChoice.Pointer] = nativeChoice;
        menu.AddChoice(new GameItem("spacephone.lostcall.choice." + binding.Plan.Kind + "." + binding.Pins.Count),
            label, detail, reply, nativeChoice);
    }
    private static List<string> Chunks(string text)
    {
        var result = new List<string>(); text = (text ?? "").Trim();
        while (text.Length > 70)
        {
            int end = text.LastIndexOf('。', 69, 55); if (end < 12) end = 69;
            result.Add(text[..(end + 1)]); text = text[(end + 1)..].TrimStart();
        }
        if (text.Length > 0) result.Add(text); if (result.Count == 0) result.Add("先顾柜台，想聊时再找我。"); return result;
    }
    private static Dialogue Pages(Binding binding, string text, Action? end) => Pages(binding, text, end, out _);
    private static Dialogue Pages(Binding binding, string text, Action? end, out Dialogue last)
    {
        var parts = Chunks(text); var first = new Dialogue().SetText("CLIENT", parts[0]); binding.Dialogues.Add(first); last = first;
        for (int i = 1; i < parts.Count; i++)
        { var next = new Dialogue().SetText("CLIENT", parts[i]); binding.Dialogues.Add(next); last.SetNextDialogue(next); last = next; }
        if (end != null) SetOwnedEnd(binding, last, end); return first;
    }
    private static void FillPages(Binding binding, Dialogue first, string text, Action end)
    {
        var parts = Chunks(text); first.SetText("CLIENT", parts[0]); var last = first;
        for (int i = 1; i < parts.Count; i++)
        { var next = new Dialogue().SetText("CLIENT", parts[i]); binding.Dialogues.Add(next); last.SetNextDialogue(next); last = next; }
        SetOwnedEnd(binding, last, end);
    }
    private static void SetOwnedEnd(Binding binding, Dialogue dialogue, Action end)
    {
        var nativeEnd = (Il2CppSystem.Action)end;
        dialogue.SetEndAction(nativeEnd); binding.EndActions[dialogue.Pointer] = nativeEnd;
    }

    internal static Sprite? RegisteredSprite(SpriteDict dictionary, string key) => key == SpriteKey && EnsureSprite(dictionary) ? originalSprite : null;
    internal static bool EnsureSprite() => SpriteDict.Instance != null && EnsureSprite(SpriteDict.Instance);
    private static bool EnsureSprite(SpriteDict dictionary)
    {
        if (dictionary.spriteDictionary == null) return false;
        if (spriteOwner == dictionary.Pointer && originalSprite != null &&
            dictionary.spriteDictionary.TryGetValue(SpriteKey, out var current) && current != null &&
            current.Pointer == originalSprite.Pointer) return true;
        if (dictionary.spriteDictionary.TryGetValue(SpriteKey, out var collision) && collision != null)
        {
            if (spritePins.Any(s => s != null && s.Pointer == collision.Pointer)) { spriteOwner = dictionary.Pointer; originalSprite = collision; return true; }
            if (warnings.Add("sprite collision")) Plugin.Instance.Log.LogWarning("Lost call original sprite key is already occupied.");
            return false;
        }
        if (dictionary.spriteDictionary.ContainsKey(SpriteKey))
        {
            // A null entry is still an occupied native key; do not overwrite it.
            if (warnings.Add("sprite null collision")) Plugin.Instance.Log.LogWarning("Lost call original sprite key has an unknown null entry.");
            return false;
        }
        if (originalSprite != null && spritePins.Any(s => s != null && s.Pointer == originalSprite.Pointer))
        {
            // A removed key can be restored with our retained asset. No outside mapping is replaced.
            dictionary.spriteDictionary.Add(SpriteKey, originalSprite); spriteOwner = dictionary.Pointer; return true;
        }
        // Completely original code-drawn adult figure; no source-game sprite pixels are read or recoloured.
        const int w = 96, h = 224;
        var pixels = new Color[w * h];
        Color outline = new(.10f,.10f,.09f,1), coat = new(.25f,.24f,.21f,1), steel = new(.31f,.34f,.35f,1),
            edge = new(.40f,.42f,.41f,1), shadow = new(.16f,.18f,.18f,1), skin = new(.58f,.47f,.37f,1), light = new(.72f,.61f,.48f,1), amber = new(.77f,.54f,.21f,1);
        void Box(int x,int y,int width,int height,Color c) { for(int py=Math.Max(0,y);py<Math.Min(h,y+height);py++)for(int px=Math.Max(0,x);px<Math.Min(w,x+width);px++)pixels[py*w+px]=c; }
        void Polygon((int x,int y)[] points,Color c)
        {
            int min = Math.Max(0,points.Min(p=>p.y)), max = Math.Min(h-1,points.Max(p=>p.y));
            for(int py=min;py<=max;py++)for(int px=0;px<w;px++)
            { bool inside=false;for(int a=0,z=points.Length-1;a<points.Length;z=a++) { var pa=points[a];var pz=points[z];if((pa.y>py)!=(pz.y>py)&&px<(float)(pz.x-pa.x)*(py-pa.y)/(pz.y-pa.y)+pa.x)inside=!inside; }if(inside)pixels[py*w+px]=c; }
        }
        // Adult proportions: head 28px and figure 208px; separate trousers/boots, asymmetric work coat.
        Polygon(new[]{(32,104),(62,104),(66,25),(52,20),(47,80),(42,22),(27,24)},outline);
        Polygon(new[]{(34,102),(47,100),(43,29),(31,28)},steel); Polygon(new[]{(48,100),(59,103),(62,30),(52,27)},shadow);
        Box(27,14,18,13,outline);Box(49,13,19,14,outline);Box(26,12,19,4,coat);Box(50,11,20,4,coat);Box(30,21,8,3,edge);Box(53,21,8,3,edge);
        Polygon(new[]{(30,180),(58,179),(72,155),(72,107),(60,87),(30,90),(22,113),(22,153)},outline);
        Polygon(new[]{(31,177),(45,180),(59,176),(68,153),(67,112),(56,91),(32,94),(25,117),(25,151)},coat);
        Polygon(new[]{(35,174),(48,166),(58,175),(57,124),(51,94),(36,95)},steel);
        Polygon(new[]{(27,153),(34,155),(30,116),(36,100),(29,96),(23,111)},shadow);
        Polygon(new[]{(61,156),(69,153),(78,109),(76,92),(66,96),(59,129)},outline);
        Polygon(new[]{(63,153),(68,150),(74,111),(72,99),(68,101),(63,127)},coat);
        Polygon(new[]{(25,152),(31,149),(24,118),(22,96),(15,97),(13,112)},outline);
        Polygon(new[]{(23,148),(27,146),(20,118),(20,101),(16,101),(16,115)},coat);
        Box(15,90,8,12,skin);Box(67,90,8,12,skin);Box(16,98,5,4,light);
        Box(27,93,36,7,outline);Box(45,94,6,5,amber);Box(29,96,9,2,edge);Box(60,74,10,19,outline);Box(61,76,8,15,coat);Box(63,80,3,9,edge);
        Box(41,173,13,13,skin);Polygon(new[]{(36,210),(48,215),(58,210),(61,195),(56,182),(44,179),(36,187),(33,200)},outline);
        Polygon(new[]{(38,207),(48,211),(55,207),(58,196),(54,185),(45,182),(38,189),(36,199)},skin);
        Box(37,205,20,6,shadow);Box(35,198,5,10,shadow);Box(37,205,14,3,edge);Box(39,199,5,2,outline);Box(51,199,4,2,outline);
        Box(48,191,5,2,shadow);Box(42,185,11,3,shadow);Box(38,195,3,5,light);Box(50,196,4,1,light);
        Polygon(new[]{(35,178),(42,174),(48,166),(36,160)},shadow);Polygon(new[]{(54,177),(60,173),(57,162),(49,166)},edge);
        Box(53,149,9,12,shadow);Box(54,151,7,2,edge);Box(55,157,4,3,amber);Box(30,127,9,15,shadow);Box(31,139,7,2,edge);
        // Hand-held phone, single amber work lamp and worn seam highlights.
        Box(12,82,10,17,outline);Box(14,85,6,11,steel);Box(15,90,4,3,amber);Box(14,97,6,2,edge);
        Box(28,115,2,24,edge);Box(63,118,2,28,shadow);Box(39,74,2,18,edge);Box(54,44,2,21,steel);
        var texture = new Texture2D(w,h,TextureFormat.RGBA32,false);texture.SetPixels(pixels);texture.Apply();texture.filterMode=FilterMode.Point;
        texture.name=SpriteKey+".texture";
        var sprite = Sprite.Create(texture,new Rect(0,0,w,h),new Vector2(.5f,0),80,0,SpriteMeshType.FullRect,Vector4.zero,false);sprite.name=SpriteKey;
        dictionary.spriteDictionary.Add(SpriteKey,sprite);spriteTextures.Add(texture);spritePins.Add(sprite);spriteOwner=dictionary.Pointer;originalSprite=sprite;
        return true;
    }
}

[HarmonyPatch(typeof(StoreClientListDict), nameof(StoreClientListDict.CreateStoreClient))]
internal static class LostCallFactoryPatch
{
    public static bool Prefix(string __0, ref StoreClient __result)
    { if (!LostCallClients.TryParse(__0,out _))return true;__result=LostCallClients.Create(__0);return false; }
}
[HarmonyPatch(typeof(StoreClientListDict), nameof(StoreClientListDict.GetClientDisplayName))]
internal static class LostCallNamePatch
{
    public static bool Prefix(string __0, ref string __result)
    { if(!LostCallClients.TryParse(__0,out _))return true;__result="余灯";return false; }
}
[HarmonyPatch(typeof(SpriteDict), nameof(SpriteDict.GetSprite))]
internal static class LostCallSpritePatch
{
    public static bool Prefix(SpriteDict __instance,string __0,ref Sprite __result)
    { if(__0!=LostCallClients.SpriteKey)return true;var sprite=LostCallClients.RegisteredSprite(__instance,__0);if(sprite==null)return true;__result=sprite;return false; }
}
[HarmonyPatch(typeof(StoreClientInstance), nameof(StoreClientInstance.CreateClientInstance))]
internal static class LostCallSellerInstancePatch
{
    public static void Postfix(StoreClient __0,StoreClientInstance __result)
    { try { if(__0!=null&&__result!=null)LostCallClients.PrepareNativeInstance(__0,__result); } catch(Exception ex){Plugin.Error(ex);} }
}
[HarmonyPatch(typeof(StoreClient), nameof(StoreClient.IsClientBuyingThisItem))]
internal static class LostCallAcceptancePatch
{
    public static void Postfix(StoreClient __instance,GameItem __0,ref bool __result)
    {
        if(!LostCallClients.TryParse(__instance.identifier,out var plan)||plan.Kind!=LostCallVisitKind.Delivery)return;
        // Preserve native refusal: this hook can only narrow a true native acceptance.
        try { __result=__result&&LostCallClients.Live?.Accepts(__instance,__0)==true; }
        catch(Exception ex) { __result=false; Plugin.Error(ex); }
    }
}
[HarmonyPatch(typeof(OverlayHelper), nameof(OverlayHelper.TransformItemChoice))]
internal static class LostCallChoicePatch
{
    public static bool Prefix(GameItem __0)=>!(__0?.identifier?.StartsWith("spacephone.lostcall.choice.",StringComparison.Ordinal)==true);
}
