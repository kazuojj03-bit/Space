using HarmonyLib;
using UnityEngine;

namespace SpacePhone;

/// <summary>Phone requests bring real native BUY visitors; payment and item ownership remain native.</summary>
internal sealed class NightOrderClients
{
    internal const string IdPrefix = "spacephone.nightorder.";
    private static readonly string[] SpriteKeys = { "spacephone.nightorder.inspector.original.v1", "spacephone.nightorder.cook.original.v1", "spacephone.nightorder.repair.original.v1" };
    internal static NightOrderClients? Live;
    private PlayerStore? store;
    private NightOrderBook? book;
    private IntPtr owner;
    private string run = "";
    private int day;
    private Action? save;
    private NightOrderVisitPlan? pending;
    private readonly Dictionary<IntPtr, Binding> bindings = new();
    private readonly List<Binding> retired = new();
    private const int RetirementLimit = 64;
    private readonly List<NativeTradeCapture> payments = new();
    private static readonly Sprite?[] sprites = new Sprite?[3];
    private static readonly List<Texture2D> textures = new();
    private static readonly HashSet<string> warnings = new(StringComparer.Ordinal);

    private sealed class Binding
    {
        internal StoreClient Client = null!;
        internal NightOrderBook Book = null!;
        internal IntPtr Owner;
        internal string Run = "", Token = "", Signature = "";
        internal NightOrderVisitPlan Plan;
        internal int RequiredUnits, InitialDelivered, Ordinal, ReceiptDelivered;
        internal long InitialPaid, ReceiptPaid;
        internal string ItemName = "";
        internal NightOrderOffer Offer = new();
        internal bool PartialClosed;
        internal bool Cold, Arrived, Settled, TerminalStarted, NativeDismissObserved, NativeEnding;
        internal float RetireAfter = -1;
        internal Il2CppSystem.Action? NativeArrival, NativeDismiss, OwnedArrival, OwnedDismiss;
        internal readonly List<Dialogue> Dialogues = new();
        internal readonly Dictionary<IntPtr, Il2CppSystem.Action> EndActions = new();
        internal readonly Dictionary<int, OwnedAcceptance> Accepted = new();
        internal readonly List<Delegate> Pins = new();
    }
    private sealed class OwnedAcceptance
    {
        internal GameItem Item = null!;
        internal IntPtr ItemPointer, Parent;
        internal int Uid, Units, Day;
        internal float ObservedAt;
    }
    internal NightOrderClients() => Live = this;
    internal static bool IsOwnId(string? identifier) => identifier?.StartsWith(IdPrefix, StringComparison.Ordinal) == true;
    internal static bool Ready => Live?.CurrentOwner() == true && Live.retired.Count < RetirementLimit && EnsureSprites();
    internal static bool TryParseId(string? identifier, out NightOrderBuyerKind kind, out string orderId, out int revision)
    {
        kind = default; orderId = ""; revision = 0;
        if (!IsOwnId(identifier)) return false;
        var parts = identifier![IdPrefix.Length..].Split('.');
        if (parts.Length != 3 || parts[0].Length != 1 || !int.TryParse(parts[0], out int buyer) || buyer is < 0 or > 2 ||
            parts[1].Length != 32 || parts[1].Any(c => !Uri.IsHexDigit(c)) ||
            parts[2].Length is < 1 or > 10 || parts[2].Any(c => c is < '0' or > '9') ||
            !int.TryParse(parts[2], out revision) || revision <= 0 || parts[2] != revision.ToString(System.Globalization.CultureInfo.InvariantCulture)) return false;
        kind = (NightOrderBuyerKind)buyer; orderId = parts[1]; return true;
    }
    internal static string ContactIdFor(NightOrderBuyerKind kind) => kind is NightOrderBuyerKind.Warehouse or NightOrderBuyerKind.Canteen or NightOrderBuyerKind.Repair
        ? IdPrefix + "contact." + (int)kind : "";
    internal static string? ContactIdFor(string? identifier) => TryParseId(identifier, out var kind, out _, out _) ? ContactIdFor(kind) : null;
    internal static bool HasPending(NightOrderBook candidate) => Live != null && Live.CurrentOwner() &&
        ReferenceEquals(Live.book, candidate) && (Live.pending.HasValue || Live.HasNativeVisitor() || Live.HasUnprovedPayment(candidate));

    internal static string QueueVisit(NightOrderBook candidate, int currentDay)
    {
        var live = Live;
        if (live == null || !live.CurrentOwner() || !ReferenceEquals(live.book, candidate)) return "求购单还在同步，先继续营业。";
        if (HasPending(candidate)) return "已有一位夜班买家在来访流程中，先完成或取消这次预约。";
        if (NightOrderRules.PlanVisit(candidate, currentDay) is not { } plan) return "现在没有待收货的求购单，看看手机上的下一步。";
        if (live.retired.Count >= RetirementLimit) return "买家正在收好旧收货记录，先正常结束柜台来访，再稍后预约。";
        if (!EnsureSprites()) return "买家的资料还没有就绪，普通柜台照常营业。";
        live.pending = plan;
        return NightOrderConversation.Name((int)plan.BuyerKind) + "收到了预约。营业且柜台空闲时来，当面议价，不替换当前顾客。";
    }
    internal static string CancelVisit(NightOrderBook candidate)
    {
        var live = Live;
        if (live == null || !live.CurrentOwner() || !ReferenceEquals(live.book, candidate)) return "求购单还在同步。";
        if (live.HasUnprovedPayment(candidate)) return "刚才的原生付款还在核对实物去向，请稍等；不会撤销已付的钱。";
        if (IsOwnId(live.store?.currentClientInstance?.storeClient.identifier)) return "买家已在来访流程中，请从原生柜台正常结束。";
        if (live.pending == null && !live.HasNativeVisitor()) return "没有待取消的夜班买家预约。";
        string token = live.pending?.ClientId ?? "";
        var stack = live.store?.storeClientManager?.clientStack;
        if (stack != null)
            for (int i = stack.Count - 1; i >= 0; i--)
                if (stack[i] != null && IsOwnId(stack[i].identifier) && (token.Length == 0 || stack[i].identifier == token)) stack.RemoveAt(i);
        live.pending = null;
        return "本次预约已取消。已成交部分保留，剩余货物和现金没有变化。";
    }

    internal void Tick(PlayerStore player, NightOrderBook candidate, int currentDay, Action write, Action close, bool allowScheduling = true)
    {
        if (owner != player.Pointer || run != (player.runID ?? "") || !ReferenceEquals(book, candidate))
        {
            foreach (var old in bindings.Values) { old.RetireAfter = Time.unscaledTime + 5; retired.Add(old); }
            bindings.Clear(); payments.Clear(); pending = null;
            owner = player.Pointer; run = player.runID ?? "";
        }
        store = player; book = candidate; day = currentDay; save = write;
        PruneRetired();
        if (!CurrentOwner() || !NightOrderRules.ValidDay(day)) return;
        FlushPayments();
        var current = player.currentClientInstance?.storeClient;
        foreach (var pair in bindings.ToArray())
        {
            if (current?.Pointer == pair.Key || HasQueued(pair.Value.Token)) continue;
            var binding = pair.Value;
            if (payments.Any(x => x.ClientPointer == pair.Key)) continue;
            if (binding.RetireAfter < 0) binding.RetireAfter = Time.unscaledTime + 5;
            if (Time.unscaledTime >= binding.RetireAfter && StoreClientMono.Instance?.IsMoving() != true)
            { bindings.Remove(pair.Key); retired.Add(binding); }
        }
        if (current != null && IsOwnId(current.identifier))
        {
            var binding = GetBinding(current, MatchingPlan(current.identifier));
            if (player.isClientArrived) ObserveArrival(binding);
            var manager = DialogUIManager.Instance;
            var oldMenu = current.mainDialogue;
            bool mainShowing = oldMenu != null && manager?.currentDialog != null && oldMenu.Pointer == manager.currentDialog.Pointer;
            bool rebound = Configure(binding);
            if (rebound && mainShowing && manager != null && !binding.Settled)
            { manager.InterruptDialog(); current.isMainDialogueStarted = false; current.StartMainDialogue(); }
            // A completed native trade may finish before inventory transfer evidence is visible. Show
            // its real receipt only after that proof, when the previous conversation has naturally ended.
            if (binding.Settled && !binding.TerminalStarted && !binding.NativeEnding && player.isClientArrived && manager != null &&
                manager.GetCurrentDialog() == null && manager.isConversationActiveFlag <= 0 && StoreClientMono.Instance?.IsMoving() != true)
            { binding.TerminalStarted = true; current.isMainDialogueStarted = false; current.StartMainDialogue(); }
            return; // Existing arrivals and payments continue even while the phone has a pending action.
        }
        if (!allowScheduling || retired.Count >= RetirementLimit || pending is not { } plan) return;
        if (NightOrderRules.PlanVisit(candidate, day) is not { } expected || expected != plan)
        { pending = null; return; }
        if (player.IsClientPresent() || player.storeState != PlayerStore.StoreState.OPEN ||
            player.playerLocation != PlayerStore.PlayerLocation.IN_STORE || player.lockAllFlag > 0 || player.lockNextBellFlag > 0 ||
            player.isHandlingShootout || player.isClientBeingArrested || player.isSecurityAlarmActive ||
            DialogUIManager.Instance?.isConversationActiveFlag > 0 || StoreClientMono.Instance?.IsMoving() == true || !EnsureSprites()) return;
        if (!HasQueued(plan.ClientId)) player.storeClientManager.AddNextClient(Create(plan.ClientId));
        close();
        if (CurrentOwner()) player.TryCallNextClient();
    }

    private bool CurrentOwner() => book != null && store != null && owner != IntPtr.Zero && PlayerStore.IsInstanceExist() &&
        PlayerStore.Instance.Pointer == owner && (PlayerStore.Instance.runID ?? "") == run && PhoneBehaviour.Live?.OwnsNightOrderBook(book) == true;
    private bool Current(Binding binding) => CurrentOwner() && ReferenceEquals(binding.Book, book) && binding.Owner == owner &&
        binding.Run == run && store?.currentClientInstance?.storeClient.Pointer == binding.Client.Pointer && binding.Client.identifier == binding.Token;
    private NightOrderVisitPlan? MatchingPlan(string token) => CurrentOwner() && NightOrderRules.PlanVisit(book!, day) is { } expected &&
        expected.ClientId == token ? expected : null;
    private bool ValidPlan(Binding binding) => !binding.Cold && CurrentOwner() && ReferenceEquals(binding.Book, book) &&
        binding.Owner == owner && binding.Run == run && NightOrderRules.PlanVisit(book!, day) is { } expected && expected == binding.Plan;
    private bool HasQueued(string token)
    {
        var stack = store?.storeClientManager?.clientStack;
        if (stack != null) foreach (var client in stack) if (client != null && client.identifier == token) return true;
        return false;
    }
    private bool HasNativeVisitor()
    {
        if (IsOwnId(store?.currentClientInstance?.storeClient.identifier)) return true;
        var stack = store?.storeClientManager?.clientStack;
        if (stack != null) foreach (var client in stack) if (client != null && IsOwnId(client.identifier)) return true;
        return false;
    }

    private bool HasUnprovedPayment(NightOrderBook candidate) => payments.Any(c => c.Submitted &&
        ReferenceEquals(c.Book, candidate) && c.Owner == owner && c.Run == run && Time.unscaledTime < c.ExpiresAt);

    private void PruneRetired()
    {
        foreach (var binding in retired.ToArray())
        {
            try
            {
                if (binding.RetireAfter < 0) binding.RetireAfter = Time.unscaledTime + 5;
                if (Time.unscaledTime < binding.RetireAfter || payments.Any(c => c.ClientPointer == binding.Client.Pointer) ||
                    pending?.ClientId == binding.Token || StoreClientMono.Instance?.IsMoving() == true) continue;
                var currentStore = PlayerStore.IsInstanceExist() ? PlayerStore.Instance : null;
                if (currentStore?.currentClientInstance?.storeClient.Pointer == binding.Client.Pointer) continue;
                bool queued = false;
                var stack = currentStore?.storeClientManager?.clientStack;
                if (stack != null) foreach (var client in stack) if (client != null && client.Pointer == binding.Client.Pointer) { queued = true; break; }
                if (queued) continue;
                var manager = DialogUIManager.Instance;
                // An active or delayed native dialogue may still reference a delegate even after the
                // client ceased being current. Do not release it while any such coroutine is pending.
                if (manager == null || manager.isConversationActiveFlag > 0 || manager.activeCoroutines?.Count > 0 ||
                    IsOurDialogue(binding, manager.currentDialog) || IsOurDialogue(binding, manager.GetCurrentDialog()) ||
                    IsOurDialogue(binding, manager.displayingUncompletedDialog)) continue;
                bool obsoleteRun = currentStore != null && (currentStore.Pointer != binding.Owner || (currentStore.runID ?? "") != binding.Run);
                if (!binding.NativeDismissObserved && binding.Arrived && !obsoleteRun) continue;
                if (!WithdrawOwnedCallbacks(binding)) continue;
                binding.Accepted.Clear(); binding.Dialogues.Clear(); binding.EndActions.Clear(); binding.Pins.Clear();
                binding.NativeArrival = null; binding.NativeDismiss = null; binding.OwnedArrival = null; binding.OwnedDismiss = null;
                retired.Remove(binding);
            }
            catch (Exception ex)
            {
                if (warnings.Add("retirement:" + ex.GetType().Name)) Plugin.Instance.Log.LogWarning("Night-order callback retirement deferred: " + ex.GetType().Name);
            }
        }
        if (retired.Count >= RetirementLimit)
        {
            // Keep already-acquired goods and the active order. Only a not-yet-created invitation is
            // withdrawn; never remove a current visitor, a pending payment or an unknown callback.
            if (pending is { } plan && !HasQueued(plan.ClientId)) pending = null;
            if (warnings.Add("retirement capacity")) Plugin.Instance.Log.LogWarning("Night-order invitations paused while old native callbacks remain live; ordinary play and the accepted order are preserved.");
        }
    }
    private static bool IsOurDialogue(Binding binding, Dialogue? dialogue) => dialogue != null &&
        binding.Dialogues.Any(d => d.Pointer == dialogue.Pointer);
    private static bool SameAction(Il2CppSystem.Action? left, Il2CppSystem.Action? right) =>
        left == null ? right == null : right != null && left.Pointer == right.Pointer;
    private static bool WithdrawOwnedCallbacks(Binding binding)
    {
        // Touch only the exact wrappers installed by this binding. A third-party replacement may
        // retain them internally, so preserve its mapping and defer all resource release in that case.
        var arrival = binding.Client.OnArrival;
        if (SameAction(arrival, binding.OwnedArrival)) binding.Client.OnArrival = binding.NativeArrival!;
        else if (!SameAction(arrival, binding.NativeArrival)) return false;
        var dismiss = binding.Client.OnDismiss;
        if (SameAction(dismiss, binding.OwnedDismiss)) binding.Client.OnDismiss = binding.NativeDismiss!;
        else if (!SameAction(dismiss, binding.NativeDismiss)) return false;
        foreach (var dialogue in binding.Dialogues)
        {
            var end = dialogue.endAction;
            if (end == null) continue;
            if (!binding.EndActions.TryGetValue(dialogue.Pointer, out var owned) || !SameAction(end, owned)) return false;
            dialogue.SetEndAction(null!);
            if (dialogue.endAction != null) return false;
        }
        return SameAction(binding.Client.OnArrival, binding.NativeArrival) && SameAction(binding.Client.OnDismiss, binding.NativeDismiss);
    }

    internal static string DisplayName(string identifier)
    {
        if (TryParseId(identifier, out var encodedKind, out _, out _)) return NightOrderConversation.Name(encodedKind);
        if (Live?.MatchingPlan(identifier) is { } plan) return NightOrderConversation.Name((int)plan.BuyerKind);
        if (Live?.store?.currentClientInstance?.storeClient is { } current && current.identifier == identifier &&
            Live.bindings.TryGetValue(current.Pointer, out var binding) && !binding.Cold)
            return NightOrderConversation.Name((int)binding.Plan.BuyerKind);
        return "夜班来客";
    }
    internal static StoreClient Create(string identifier)
    {
        if (!IsOwnId(identifier)) throw new ArgumentException("Unknown night-order visitor.", nameof(identifier));
        var live = Live; var plan = live?.MatchingPlan(identifier);
        bool buying = plan.HasValue && plan.Value.ProposedBuyerBudget > 0;
        int index = buying ? (int)plan!.Value.BuyerKind : TryParseId(identifier, out var kind, out _, out _) ? (int)kind : 0;
        var client = new StoreClient
        {
            identifier = identifier, displayName = DisplayName(identifier),
            spriteName = SpriteKeys[index], clientIntent = buying ? StoreClient.ClientIntent.BUY : StoreClient.ClientIntent.DIALOGUE,
            clientFaction = StoreClient.FACTION_MIDDLE, bubbleColor = index == 1 ? "#ad9571" : index == 2 ? "#7d9593" : "#949799",
            dismissable = true, arrestable = false, nonShootable = true, isNoContributeToEvidence = true,
            clientCash = 0, clientBudget = 0, useClientBudget = true, isRegularSellDisable = !buying,
            isMultiBuyDisabled = true, cooldown = 0
        };
        if (buying)
        {
            client.clientBuyingTagList = new Il2CppSystem.Collections.Generic.List<string>();
            client.clientBuyingIdList = new Il2CppSystem.Collections.Generic.List<string>();
            client.clientBuyingIdList.Add(plan!.Value.RequiredItemId);
            client.SetBudget(plan.Value.ProposedBuyerBudget); // Native upper limit, never a promised sale price.
        }
        client.CompleteClientCreation(true);
        if (live != null && plan.HasValue) live.Configure(live.GetBinding(client, plan));
        else
        {
            client.mainDialogue = Pages(new[] { "我这张求购单还在同步，今天先不收货。", "先照顾柜台，手机确认好记录后再约我。" }, null);
            client.mainDialogue.isMainDialog = true; client.repeatDialogue = client.mainDialogue;
        }
        return client;
    }
    private Binding GetBinding(StoreClient client, NightOrderVisitPlan? plan)
    {
        if (bindings.TryGetValue(client.Pointer, out var existing) && existing.Token == client.identifier) return existing;
        var active = book?.Active;
        var nativeArrival = client.OnArrival; var nativeDismiss = client.OnDismiss;
        var previous = retired.LastOrDefault(b => b.Client.Pointer == client.Pointer && b.Token == client.identifier);
        if (previous != null)
        {
            if (SameAction(nativeArrival, previous.OwnedArrival)) nativeArrival = previous.NativeArrival;
            if (SameAction(nativeDismiss, previous.OwnedDismiss)) nativeDismiss = previous.NativeDismiss;
        }
        var binding = new Binding { Client = client, Book = book!, Owner = owner, Run = run, Token = client.identifier,
            Plan = plan ?? default, Cold = !plan.HasValue || client.clientIntent != StoreClient.ClientIntent.BUY,
            RequiredUnits = active?.Offer.RequiredUnits ?? 0, InitialDelivered = active?.DeliveredUnits ?? 0,
            InitialPaid = active?.ActualPaidTotal ?? 0, Ordinal = active?.Offer.Ordinal ?? 0, ItemName = active?.Offer.ItemName ?? "货物",
            Offer = active?.Offer ?? new(),
            NativeArrival = nativeArrival, NativeDismiss = nativeDismiss };
        bindings[client.Pointer] = binding;
        Action arrival = () =>
        {
            try { if (!Current(binding)) return; binding.NativeArrival?.Invoke(); ObserveArrival(binding); Configure(binding); }
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
    private void ObserveArrival(Binding binding)
    {
        if (!Current(binding) || binding.Arrived || !store!.isClientArrived) return;
        binding.Arrived = true;
        if (pending?.ClientId == binding.Token) pending = null;
    }

    internal bool Accepts(StoreClient client, GameItem item)
    {
        int currentDay = StoreStation.GetDayCounter();
        if (!NightOrderRules.ValidDay(currentDay)) return false;
        day = currentDay;
        if (item == null || item.Pointer == IntPtr.Zero || item.uniqueId <= 0 || item.unitCount <= 0 ||
            !bindings.TryGetValue(client.Pointer, out var binding) || !Current(binding) || !binding.Arrived ||
            binding.Settled || !ValidPlan(binding) || client.clientIntent != StoreClient.ClientIntent.BUY ||
            item.identifier != binding.Plan.RequiredItemId || item.identifier.StartsWith("spacephone.", StringComparison.Ordinal) ||
            item.unitCount > binding.Plan.RemainingUnits || !GeneralHelper.IsItemEffectivelyOwned(item) ||
            binding.Plan.RequiresDrinkingWater && !PhoneCommissionClients.MeetsGoodsRequirement(0, item)) return false;
        binding.Accepted[item.uniqueId] = new OwnedAcceptance { Item = item, ItemPointer = item.Pointer,
            Uid = item.uniqueId, Units = item.unitCount, Day = day, Parent = item.parentInventory?.Pointer ?? IntPtr.Zero,
            ObservedAt = Time.unscaledTime };
        return true;
    }

    internal sealed class NativeTradeCapture
    {
        internal NightOrderBook Book = null!;
        internal GameItem Item = null!;
        internal IntPtr Owner, ClientPointer, ItemPointer, SourceParent;
        internal string Run = "", ClientId = "", NativeToken = "";
        internal NightOrderVisitPlan Plan;
        internal int Day, UniqueId, Units, Amount;
        internal bool Submitted;
        internal float ExpiresAt;
    }
    // Only TradeHooks calls this at the explicit native OnItemTraded event; UI and scans cannot settle orders.
    internal NativeTradeCapture? CaptureTradeBefore(IntPtr clientPointer, bool purchase, GameItem item)
    {
        int currentDay = StoreStation.GetDayCounter();
        day = currentDay;
        if (purchase || !NightOrderRules.ValidDay(currentDay) || !CurrentOwner() || book == null || item == null ||
            item.Pointer == IntPtr.Zero || item.uniqueId <= 0 || !bindings.TryGetValue(clientPointer, out var binding) ||
            !Current(binding) || !binding.Arrived || binding.Settled || !ValidPlan(binding) ||
            item.identifier != binding.Plan.RequiredItemId || item.unitCount <= 0 || item.unitCount > binding.Plan.RemainingUnits ||
            !binding.Accepted.TryGetValue(item.uniqueId, out var accepted) || accepted.Day != currentDay ||
            accepted.ItemPointer != item.Pointer || accepted.Uid != item.uniqueId || accepted.Units != item.unitCount ||
            Time.unscaledTime - accepted.ObservedAt > 600 ||
            binding.Plan.RequiresDrinkingWater && !PhoneCommissionClients.MeetsGoodsRequirement(0, item)) return null;
        // The reputation callback can follow transfer. Effective ownership was positively observed at
        // native acceptance for this exact UID, pointer, quantity and current visitor, rather than guessed here.
        return new NativeTradeCapture { Book = book, Owner = owner, Run = run, ClientPointer = clientPointer,
            ClientId = binding.Token, Item = item, ItemPointer = item.Pointer, UniqueId = item.uniqueId,
            SourceParent = accepted.Parent, Units = accepted.Units, Day = currentDay, Plan = binding.Plan,
            NativeToken = "nightorder/native/" + Guid.NewGuid().ToString("N") };
    }
    internal bool SubmitTradeAfter(NativeTradeCapture? capture, int actualAmount, int actualDay)
    {
        if (capture == null || capture.Submitted || actualAmount <= 0 || actualAmount > NightOrderRules.MaxAmount ||
            actualDay != capture.Day || !CurrentOwner() || capture.Owner != owner || capture.Run != run ||
            !ReferenceEquals(capture.Book, book) || store?.currentClientInstance?.storeClient.Pointer != capture.ClientPointer ||
            store.currentClientInstance.storeClient.identifier != capture.ClientId ||
            !bindings.TryGetValue(capture.ClientPointer, out var binding) || !ValidPlan(binding) || binding.Settled) return false;
        capture.Submitted = true; capture.Amount = actualAmount; capture.ExpiresAt = Time.unscaledTime + 5;
        payments.Add(capture); FlushPayments(); return true;
    }
    private void FlushPayments()
    {
        if (!CurrentOwner() || book == null) return;
        int currentDay = StoreStation.GetDayCounter();
        if (!NightOrderRules.ValidDay(currentDay)) return;
        day = currentDay;
        foreach (var capture in payments.ToArray())
        {
            try
            {
                if (capture.Owner != owner || capture.Run != run || capture.Day != day || !ReferenceEquals(capture.Book, book) ||
                    !bindings.TryGetValue(capture.ClientPointer, out var binding) || binding.Token != capture.ClientId ||
                    !ValidPlan(binding) || binding.Settled)
                { payments.Remove(capture); continue; }
                if (!Transferred(capture))
                {
                    if (Time.unscaledTime >= capture.ExpiresAt)
                    {
                        payments.Remove(capture);
                        if (warnings.Add("unproved payment:" + capture.ClientId)) Plugin.Instance.Log.LogWarning("Night-order payment left uncounted without positive transfer evidence: " + capture.NativeToken);
                    }
                    continue;
                }
                var evidence = new NightOrderTradeEvidence(capture.Day, capture.UniqueId, capture.Plan.RequiredItemId,
                    capture.Units, capture.Amount, capture.NativeToken, true, true, true);
                var reply = NightOrderRules.RecordPayment(book, capture.Plan, evidence);
                payments.Remove(capture);
                if (!reply.Changed) continue;
                binding.Settled = true; binding.Accepted.Clear();
                binding.ReceiptDelivered = binding.InitialDelivered + capture.Units;
                binding.ReceiptPaid = binding.InitialPaid + capture.Amount;
                binding.PartialClosed = book.History.Any(h => h.Offer.OrderId == capture.Plan.OrderId && h.Result == NightOrderResult.PartialClosed);
                save?.Invoke(); Configure(binding);
                Plugin.Instance.Log.LogInfo("Night-order paid native receipt: " + capture.ClientId + ", units=" + capture.Units + ", paid=" + capture.Amount);
            }
            catch (Exception ex)
            {
                payments.Remove(capture);
                Plugin.Instance.Log.LogWarning("Night-order native evidence deferred without inferred payment: " + ex.GetType().Name);
            }
        }
    }
    private bool Transferred(NativeTradeCapture capture)
    {
        if (capture.Item == null || capture.Item.Pointer == IntPtr.Zero || capture.Item.Pointer != capture.ItemPointer ||
            capture.Item.uniqueId != capture.UniqueId || capture.Item.identifier != capture.Plan.RequiredItemId ||
            GeneralHelper.IsItemEffectivelyOwned(capture.Item) ||
            capture.Plan.RequiresDrinkingWater && !PhoneCommissionClients.MeetsGoodsRequirement(0, capture.Item)) return false;
        var buyer = store?.currentClientInstance?.storeClient.Pointer == capture.ClientPointer ? store.currentClientInstance.gridInv : null;
        return Contains(buyer, capture) || Contains(EmporiumEntry.Instance?.soldElement, capture);
    }
    private static bool Contains(GameInventory? inventory, NativeTradeCapture capture)
    {
        var items = inventory?.childItems;
        if (items != null) foreach (var item in items)
            if (item != null && item.Pointer == capture.ItemPointer && item.uniqueId == capture.UniqueId &&
                item.identifier == capture.Plan.RequiredItemId && item.unitCount == capture.Units &&
                !GeneralHelper.IsItemEffectivelyOwned(item)) return true;
        return false;
    }

    private bool Configure(Binding binding)
    {
        string signature = binding.Cold + ":" + binding.Arrived + ":" + ValidPlan(binding) + ":" + binding.Settled;
        if (binding.Signature == signature) return false;
        binding.Signature = signature;
        Action leave = () => { binding.NativeEnding = true; if (Current(binding)) DialogUIManager.Instance?.CurrentClientLeave(.6f); };
        binding.Pins.Add(leave);
        var client = binding.Client;
        if (binding.Settled)
        {
            bool complete = binding.ReceiptDelivered >= binding.RequiredUnits;
            var receipt = binding.PartialClosed
                ? NightOrderConversation.PartialClosed(new NightOrderActive { Offer = binding.Offer, DeliveredUnits = binding.ReceiptDelivered, ActualPaidTotal = binding.ReceiptPaid })
                : complete
                ? NightOrderConversation.Completed(binding.Plan.BuyerKind, binding.RequiredUnits, binding.ReceiptPaid, day, binding.Ordinal)
                : NightOrderConversation.Partial(binding.Plan.BuyerKind, binding.ReceiptDelivered, binding.RequiredUnits, binding.ReceiptPaid);
            Action receiptLeave = () => { binding.TerminalStarted = true; leave(); };
            binding.Pins.Add(receiptLeave);
            client.mainDialogue = Pages(binding, receipt, receiptLeave); client.mainDialogue.isMainDialog = true;
            client.repeatDialogue = client.mainDialogue; client.allDoneDialogue = client.mainDialogue;
            return true;
        }
        if (binding.Cold || !ValidPlan(binding))
        {
            client.mainDialogue = Pages(binding, new[] { "这张收货单需要重新核对，今天不收替代货。", "已付的钱和已交的货照旧，剩下的回手机看当前提示。" }, leave);
            client.mainDialogue.isMainDialog = true; client.repeatDialogue = client.mainDialogue; client.allDoneDialogue = client.mainDialogue;
            return true;
        }
        var opening = binding.InitialDelivered > 0
            ? NightOrderConversation.Revisit(binding.Plan.BuyerKind, binding.InitialDelivered, binding.RequiredUnits, day, binding.Ordinal, binding.ItemName, binding.Plan.ProposedBuyerBudget)
            : NightOrderConversation.Opening(binding.Plan.BuyerKind, day, binding.Ordinal, binding.ItemName, binding.Plan.RemainingUnits, binding.Plan.ProposedBuyerBudget);
        client.mainDialogue = Pages(binding, opening, null); client.mainDialogue.isMainDialog = true; client.repeatDialogue = client.mainDialogue;
        client.acceptDealDialogue = Pages(binding, new[] { "价格按刚才柜台确认的数额结。", "实际付款和交货都对上，手机才会记进这张求购单。" }, null);
        client.placeRightItemWhenSellingToDialogue = Pages(binding, new[] { "货对上这张单子了，接下来按你的柜台规矩议价。", "整件超过剩余数量的话，先用原生库存功能分好再谈。" }, null);
        client.placedWrongItemWhenSellingToDialogue = Pages(binding, NightOrderConversation.WrongItem(binding.Plan.BuyerKind), null);
        client.allDoneDialogue = Pages(binding, new[] { "今天的柜台流程到这里。", "实物和付款核对完成后，手机会记实际收款；余量可以另约。" }, leave);
        return true;
    }
    private static Dialogue Pages(IEnumerable<string> pages, Action? end) => Pages(null, pages, end);
    private static Dialogue Pages(Binding? binding, IEnumerable<string> pages, Action? end)
    {
        var parts = new List<string>();
        foreach (var page in pages)
        {
            string text = (page ?? "").Trim();
            while (text.Length > 64) { int cut = text.LastIndexOf('。', 63, 45); if (cut < 12) cut = 63; parts.Add(text[..(cut + 1)]); text = text[(cut + 1)..].TrimStart(); }
            if (text.Length > 0) parts.Add(text);
        }
        if (parts.Count == 0) parts.Add("先顾柜台，想好后从手机约我。");
        var first = new Dialogue().SetText("CLIENT", parts[0]); var last = first; binding?.Dialogues.Add(first);
        for (int i = 1; i < parts.Count; i++) { var next = new Dialogue().SetText("CLIENT", parts[i]); binding?.Dialogues.Add(next); last.SetNextDialogue(next); last = next; }
        if (end != null) { var nativeEnd = (Il2CppSystem.Action)end; last.SetEndAction(nativeEnd); if (binding != null) binding.EndActions[last.Pointer] = nativeEnd; }
        return first;
    }

    internal static Sprite? RegisteredSprite(SpriteDict dictionary, string key)
    {
        int index = Array.IndexOf(SpriteKeys, key);
        return index >= 0 && RegisterSprite(dictionary, index) ? sprites[index] : null;
    }
    private static bool EnsureSprites()
    {
        var dictionary = SpriteDict.Instance;
        if (dictionary?.spriteDictionary == null) return false;
        for (int i = 0; i < SpriteKeys.Length; i++) if (!RegisterSprite(dictionary, i)) return false;
        return true;
    }
    private static bool RegisterSprite(SpriteDict dictionary, int index)
    {
        if (dictionary.spriteDictionary == null) return false;
        string key = SpriteKeys[index]; var owned = sprites[index];
        if (dictionary.spriteDictionary.TryGetValue(key, out var current))
        {
            if (owned != null && current != null && current.Pointer == owned.Pointer) return true;
            if (warnings.Add("sprite collision:" + key)) Plugin.Instance.Log.LogWarning("Night-order original sprite key occupied; preserving the existing mapping: " + key);
            return false;
        }
        if (dictionary.spriteDictionary.ContainsKey(key)) return false;
        // Only three original procedural sprites are created per session and reused if SpriteDict reloads.
        if (owned == null) { owned = DrawBuyer(index, key); sprites[index] = owned; }
        dictionary.spriteDictionary.Add(key, owned);
        return dictionary.spriteDictionary.TryGetValue(key, out current) && current != null && current.Pointer == owned.Pointer;
    }
    private static Sprite DrawBuyer(int index, string key)
    {
        const int w = 96, h = 224;
        var pixels = new Color[w * h];
        Color line = new(.09f, .10f, .10f, 1), dark = new(.17f, .19f, .19f, 1), steel = new(.30f, .34f, .35f, 1),
            worn = new(.43f, .44f, .40f, 1), skin = new(.60f, .47f, .36f, 1), light = new(.73f, .60f, .45f, 1),
            cloth = index == 0 ? new(.26f, .28f, .29f, 1) : index == 1 ? new(.32f, .25f, .19f, 1) : new(.24f, .31f, .31f, 1),
            amber = new(.76f, .51f, .20f, 1);
        void Box(int x, int y, int width, int height, Color color)
        { for (int py = Math.Max(0, y); py < Math.Min(h, y + height); py++) for (int px = Math.Max(0, x); px < Math.Min(w, x + width); px++) pixels[py * w + px] = color; }
        void Poly((int x, int y)[] points, Color color)
        {
            int lo = Math.Max(0, points.Min(v => v.y)), hi = Math.Min(h - 1, points.Max(v => v.y));
            for (int py = lo; py <= hi; py++) for (int px = 0; px < w; px++)
            {
                bool inside = false;
                for (int a = 0, z = points.Length - 1; a < points.Length; z = a++)
                { var pa = points[a]; var pz = points[z]; if ((pa.y > py) != (pz.y > py) && px < (float)(pz.x - pa.x) * (py - pa.y) / (pz.y - pa.y) + pa.x) inside = !inside; }
                if (inside) pixels[py * w + px] = color;
            }
        }
        // Head 27px within a 207px adult figure. Boots sit at the foot pivot, not in a UI overlay.
        Poly(new[] { (32, 105), (61, 105), (64, 25), (51, 22), (47, 83), (42, 23), (29, 25) }, line);
        Poly(new[] { (34, 102), (46, 101), (41, 29), (32, 29) }, steel);
        Poly(new[] { (49, 102), (58, 102), (61, 29), (52, 27) }, dark);
        Box(27, 13, 18, 15, line); Box(49, 12, 19, 16, line); Box(26, 12, 20, 4, dark); Box(49, 11, 22, 4, dark);
        Box(30, 23, 10, 2, worn); Box(53, 23, 11, 2, worn);
        Poly(new[] { (29, 177), (43, 182), (56, 179), (70, 155), (70, 107), (59, 91), (30, 93), (23, 118), (23, 150) }, line);
        Poly(new[] { (31, 174), (44, 178), (55, 175), (66, 151), (66, 110), (56, 96), (32, 97), (27, 119), (27, 150) }, cloth);
        Poly(new[] { (23, 151), (31, 148), (24, 117), (24, 99), (16, 97), (13, 117) }, line);
        Poly(new[] { (25, 149), (29, 148), (21, 116), (20, 102), (17, 102), (17, 118) }, cloth);
        Poly(new[] { (61, 152), (68, 151), (79, 113), (77, 95), (68, 94), (62, 123) }, line);
        Poly(new[] { (64, 150), (67, 148), (75, 112), (73, 100), (70, 100), (66, 124) }, cloth);
        Box(17, 91, 8, 12, skin); Box(69, 89, 8, 13, skin); Box(18, 97, 5, 3, light);
        Box(40, 174, 13, 11, skin);
        Poly(new[] { (36, 209), (48, 215), (58, 207), (60, 194), (54, 183), (44, 181), (36, 188), (34, 201) }, line);
        Poly(new[] { (39, 207), (48, 211), (55, 205), (57, 195), (52, 186), (44, 184), (39, 190), (37, 200) }, skin);
        Box(39, 199, 4, 2, line); Box(51, 198, 4, 2, line); Box(48, 191, 4, 2, dark); Box(42, 185, 9, 2, dark); Box(39, 193, 2, 5, light);
        if (index == 0)
        {
            // Port inspector: short coat, grey side-part, brass badge and a scuffed clipboard.
            Box(36, 205, 21, 7, dark); Box(36, 204, 8, 3, worn); Box(37, 195, 2, 7, worn);
            Poly(new[] { (33, 173), (42, 171), (48, 161), (35, 156) }, steel);
            Poly(new[] { (52, 172), (59, 169), (55, 156), (49, 161) }, dark);
            Box(45, 99, 2, 64, line); Box(51, 150, 7, 7, amber); Box(52, 151, 5, 4, worn);
            Box(15, 72, 23, 37, line); Box(18, 75, 17, 30, worn); Box(23, 103, 7, 5, dark);
            for (int y = 80; y < 101; y += 5) Box(21, y, 11, 1, dark);
        }
        else if (index == 1)
        {
            // Canteen foreman: tied-back hair, rolled sleeves and a patched work apron.
            Box(35, 206, 24, 8, dark); Box(34, 196, 5, 11, dark); Box(55, 188, 8, 17, dark); Box(59, 185, 7, 6, line);
            Box(36, 173, 21, 6, amber); Box(39, 167, 16, 6, cloth);
            Poly(new[] { (36, 163), (55, 163), (61, 95), (31, 95) }, worn);
            Box(37, 156, 17, 2, dark); Box(35, 114, 23, 13, cloth); Box(37, 124, 19, 2, amber);
            Box(17, 107, 9, 9, worn); Box(68, 106, 9, 9, worn); Box(45, 111, 5, 6, dark);
            Box(73, 88, 8, 11, line); Box(74, 90, 6, 7, worn); Box(28, 96, 8, 2, dark);
        }
        else
        {
            // Maintenance worker: low cap, coveralls, reflective seams and a small tool pouch.
            Box(33, 208, 30, 9, dark); Box(33, 205, 32, 4, steel); Box(36, 205, 24, 2, worn);
            Box(34, 136, 4, 35, worn); Box(56, 137, 4, 34, worn); Box(39, 143, 14, 19, dark);
            Box(31, 99, 31, 8, dark); Box(44, 100, 7, 5, amber); Box(61, 79, 14, 25, line); Box(63, 82, 10, 19, cloth);
            Box(67, 97, 3, 17, steel); Box(63, 91, 4, 15, worn); Box(55, 126, 8, 3, amber);
            Box(17, 84, 8, 15, dark); Box(18, 92, 6, 4, amber); Box(36, 47, 2, 24, worn);
        }
        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false); texture.SetPixels(pixels); texture.Apply();
        texture.filterMode = FilterMode.Point; texture.name = key + ".texture"; textures.Add(texture);
        var sprite = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(.5f, 0), 80, 0, SpriteMeshType.FullRect, Vector4.zero, false);
        sprite.name = key; return sprite;
    }
}

[HarmonyPatch(typeof(StoreClientListDict), nameof(StoreClientListDict.CreateStoreClient))]
internal static class NightOrderFactoryPatch
{
    public static bool Prefix(string __0, ref StoreClient __result)
    { if (!NightOrderClients.IsOwnId(__0)) return true; __result = NightOrderClients.Create(__0); return false; }
}
[HarmonyPatch(typeof(StoreClientListDict), nameof(StoreClientListDict.GetClientDisplayName))]
internal static class NightOrderNamePatch
{
    public static bool Prefix(string __0, ref string __result)
    { if (!NightOrderClients.IsOwnId(__0)) return true; __result = NightOrderClients.DisplayName(__0); return false; }
}
[HarmonyPatch(typeof(SpriteDict), nameof(SpriteDict.GetSprite))]
internal static class NightOrderSpritePatch
{
    public static bool Prefix(SpriteDict __instance, string __0, ref Sprite __result)
    { var sprite = NightOrderClients.RegisteredSprite(__instance, __0); if (sprite == null) return true; __result = sprite; return false; }
}
[HarmonyPatch(typeof(StoreClient), nameof(StoreClient.IsClientBuyingThisItem))]
internal static class NightOrderAcceptancePatch
{
    public static void Postfix(StoreClient __instance, GameItem __0, ref bool __result)
    {
        if (!NightOrderClients.IsOwnId(__instance.identifier)) return;
        // Never promote an original game's refusal into acceptance.
        try { __result = __result && NightOrderClients.Live?.Accepts(__instance, __0) == true; }
        catch (Exception ex) { __result = false; Plugin.Error(ex); }
    }
}
