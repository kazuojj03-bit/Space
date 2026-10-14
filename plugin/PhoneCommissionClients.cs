using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpacePhone;

/// <summary>Invited counter commissions. Native sale, price, budget and inventory transfer remain authoritative.</summary>
internal sealed class PhoneCommissionClients
{
    internal const string IdPrefix = "spacephone.commission.";
    private static readonly string[] SpriteKeys = { "spacephone.rain", "spacephone.medic", "spacephone.runner" };
    private static readonly string[] FactoryNames = { nameof(FoodItemDirectory.BottledWater), nameof(MaterialDirectory.CommonMedicalSupplies), nameof(MaterialDirectory.Electronic) };
    // Exact literals from this game's global-metadata string table (13157, 13572, 13571).
    // Every candidate still requires native registration and a matching explicit factory fingerprint.
    private static readonly string[] RegistryKeys = { "bottled_water", "common_medical", "common_electronic" };
    private static readonly int[] RequestedUnits = { 2, 1, 1 };
    private static readonly int[] VisitBudgets = { 160, 240, 240 };
    private static readonly HashSet<string> verifiedDefinitions = new(StringComparer.Ordinal);
    private static float nextMetadataAttempt;
    internal static PhoneCommissionClients? Live;
    private PlayerStore? store;
    private CommissionBook? commissions;
    private IntPtr owner;
    private string run = "";
    private int day;
    private Action<int, int>? onChoice;
    private Action? save;
    private readonly HashSet<IntPtr> arrived = new();
    private readonly Dictionary<IntPtr, string> configured = new();
    private readonly Dictionary<IntPtr, List<Delegate>> pinned = new();
    private readonly Dictionary<IntPtr, float> dismissCleanup = new();
    private readonly HashSet<IntPtr> terminalStarted = new();
    private static readonly HashSet<string> metadataWarnings = new(StringComparer.Ordinal);

    internal PhoneCommissionClients() => Live = this;

    internal static string Identifier(int index, int stage) => IdPrefix + index + "." + stage;

    internal static bool TryParse(string? identifier, out int index, out int stage)
    {
        index = stage = -1;
        if (identifier == null || !identifier.StartsWith(IdPrefix, StringComparison.Ordinal)) return false;
        var parts = identifier[IdPrefix.Length..].Split('.');
        return parts.Length == 2 && int.TryParse(parts[0], out index) && index is >= 0 and < 3 &&
            int.TryParse(parts[1], out stage) && stage is >= 0 and < 2;
    }

    /// <summary>Verify exact native registry IDs against explicit factories, never translated-name matching.</summary>
    internal static bool ConfigureOffers(CommissionBook book, int currentDay)
    {
        if (book.Entries == null || book.Entries.Length != 3 || !PlayerStore.IsInstanceExist()) return false;
        bool needsMetadata = book.Entries.Any(e => string.IsNullOrWhiteSpace(e.ItemId) || !verifiedDefinitions.Contains(e.ItemId));
        if (!needsMetadata || Time.unscaledTime < nextMetadataAttempt) return false;
        nextMetadataAttempt = Time.unscaledTime + 10;
        var food = Object.FindObjectOfType(Il2CppType.Of<FoodItemDirectory>())?.TryCast<FoodItemDirectory>();
        var materials = Object.FindObjectOfType(Il2CppType.Of<MaterialDirectory>())?.TryCast<MaterialDirectory>();
        if (food == null || materials == null) return false;
        bool changed = false;
        for (int index = 0; index < 3; index++)
        {
            var entry = book.Entries[index];
            if (!string.IsNullOrWhiteSpace(entry.ItemId) && verifiedDefinitions.Contains(entry.ItemId)) continue;
            GameItem? prototype = null;
            bool isolatedPrototype = false;
            try
            {
                Directory<GameItem> directory = index == 0 ? food : materials;
                string key = entry.ItemId;
                if (string.IsNullOrWhiteSpace(key))
                {
                    foreach (var factory in directory.factoryDictionary)
                        if (factory.Value != null && factory.Value.Method?.Name == FactoryNames[index])
                        { key = factory.Key; break; }
                    // IL2CPP registrations commonly use InitDirectory lambda wrappers, whose Method.Name
                    // differs from the factory they call. Check an exact original metadata literal instead.
                    if (string.IsNullOrWhiteSpace(key)) key = RegistryKeys[index];
                }
                if (string.IsNullOrWhiteSpace(key) || !directory.Has(key))
                {
                    if (metadataWarnings.Add(FactoryNames[index]))
                        Plugin.Instance.Log.LogWarning("Commission stock metadata is not ready: " + FactoryNames[index] + "; normal store play remains available.");
                    continue;
                }
                var existingPointers = new HashSet<IntPtr>();
                var existingUids = new HashSet<int>();
                var inventoryBefore = EmporiumEntry.Instance?.GetAllItems();
                if (inventoryBefore != null)
                    foreach (var existingItem in inventoryBefore)
                        if (existingItem != null)
                        { existingPointers.Add(existingItem.Pointer); if (existingItem.uniqueId > 0) existingUids.Add(existingItem.uniqueId); }
                prototype = ItemSpawner.Spawn(key);
                if (prototype == null || prototype.Pointer == IntPtr.Zero)
                {
                    WarnMetadataOnce("spawn:" + index, "Commission metadata Spawn returned no live object for " + key);
                    continue;
                }
                if (metadataWarnings.Add("diagnostic:" + index))
                    Plugin.Instance.Log.LogInfo("Commission metadata inspected: " + MetadataDiagnostic(prototype));
                if (prototype.identifier != key || prototype.uniqueId <= 0 || prototype.parentInventory != null ||
                    existingPointers.Contains(prototype.Pointer) || existingUids.Contains(prototype.uniqueId))
                {
                    WarnMetadataOnce("identity:" + index, "Commission metadata Spawn identity/UID/placement invalid: " + MetadataDiagnostic(prototype));
                    continue;
                }
                // This exact freshly spawned pointer/UID is our temporary object, not pre-existing stock.
                // Native Spawn can mark it owned even before placement; that flag is not proof of user ownership.
                isolatedPrototype = true;
                GameItem template = index switch
                {
                    0 => food.BottledWater(),
                    1 => materials.CommonMedicalSupplies(),
                    _ => materials.Electronic()
                };
                // The direct factory returns a template. It is never placed, assigned a UID or destroyed:
                // native GC can reclaim it, without risking an unregister of another object's UID zero.
                if (template == null || template.Pointer == IntPtr.Zero || !SameFactoryDefinition(template, prototype))
                {
                    if (metadataWarnings.Add("fingerprint:" + FactoryNames[index]))
                        Plugin.Instance.Log.LogWarning("Commission stock factory mismatch: " + FactoryNames[index] + " registry=" + key + "; order remains unconfigured.");
                    continue;
                }
                string name = System.Text.RegularExpressions.Regex.Replace(prototype.GetDisplayName(false) ?? "", "<[^>]+>", "");
                name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ").Trim();
                if (string.IsNullOrWhiteSpace(name) || prototype.unitCount <= 0)
                {
                    WarnMetadataOnce("label:" + index, "Commission metadata label/units invalid: " + MetadataDiagnostic(prototype));
                    continue;
                }
                verifiedDefinitions.Add(key);
                if (string.IsNullOrWhiteSpace(entry.ItemId))
                {
                    bool accepted = CommissionRules.Configure(book, index, key, name, RequestedUnits[index], currentDay);
                    changed |= accepted;
                    if (accepted) Plugin.Instance.Log.LogInfo($"Commission stock verified: {index} {key} ({name}) x{RequestedUnits[index]}, native visit budget={VisitBudgets[index]}");
                    else WarnMetadataOnce("rules:" + index, "Commission metadata definition was valid, but pure configuration rejected index=" + index + " key=" + key + " name=" + name);
                }
            }
            catch (Exception ex)
            {
                if (metadataWarnings.Add("error:" + FactoryNames[index]))
                    Plugin.Instance.Log.LogWarning("Commission metadata deferred: " + FactoryNames[index] + ": " + ex.Message);
            }
            finally
            {
                // This exact object was created only for metadata and never added to any inventory.
                // Delivery merchandise is transferred and consumed exclusively by the native sale pipeline.
                try
                {
                    if (isolatedPrototype && prototype != null && prototype.Pointer != IntPtr.Zero &&
                        prototype.uniqueId > 0 && prototype.parentInventory == null)
                    { GeneralHelper.SetItemOwned(prototype, false); prototype.Destroy(); }
                }
                catch (Exception ex)
                {
                    if (metadataWarnings.Add("cleanup:" + FactoryNames[index]))
                        Plugin.Instance.Log.LogWarning("Commission metadata cleanup deferred: " + ex.Message);
                }
            }
        }
        return changed;
    }

    private static void WarnMetadataOnce(string key, string text)
    { if (metadataWarnings.Add(key)) Plugin.Instance.Log.LogWarning(text); }

    internal static string MetadataDiagnostic(GameItem item)
    {
        string Safe(Func<string> read) { try { return read(); } catch (Exception ex) { return "unavailable:" + ex.GetType().Name; } }
        var features = new List<string>();
        if (item.itemFeatures != null)
            foreach (var feature in item.itemFeatures)
                if (feature != null) features.Add(feature.identifier);
        string Types()
        {
            var values = new List<string>(); var nativeTypes = item.GetGameItemType();
            if (nativeTypes != null) foreach (string type in nativeTypes) values.Add(type);
            return string.Join("/", values);
        }
        return $"id={item.identifier}, uid={item.uniqueId}, ptr={item.Pointer}, units={item.unitCount}, parent={item.parentInventory?.identifier ?? "none"}, " +
            "owned=" + Safe(() => GeneralHelper.IsItemEffectivelyOwned(item).ToString()) + ", current=" +
            Safe(() => item.GetCurrentValue(false, true, true, false, 0).ToString()) + ", refreshed=" +
            Safe(() => item.GetRefreshedValue().ToString()) + ", base=" + Safe(() => item.GetBaseValue().ToString()) +
            $", rawUnit={item.unitValue}, rawBase={item.unitBaseValue}, late={item.lateUnitValue}, children={item.children?.Count ?? 0}, features=" +
            string.Join("/", features) + ", nativeTypes=" + Safe(Types) + ", definition=" + FactoryFingerprint(item);
    }

    internal static string FactoryFingerprint(GameItem item)
    {
        var types = new List<string>();
        if (item.itemTypes != null) foreach (string type in item.itemTypes) types.Add(type);
        types.Sort(StringComparer.Ordinal);
        string Field(string? value) => (value?.Length ?? 0) + ":" + (value ?? "");
        return Field(item.spriteAtlasPath) + "|" + Field(item.spritePath) + "|" + Field(item.name) + "|" +
            item.unitBaseValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + string.Join(";", types.Select(Field));
    }

    internal static bool SameFactoryDefinition(GameItem template, GameItem registered) =>
        string.Equals(FactoryFingerprint(template), FactoryFingerprint(registered), StringComparison.Ordinal);

    internal static int BudgetFor(CommissionBook book, int index)
    {
        if (index is < 0 or > 2 || book.Entries == null || book.Entries.Length != 3) return 0;
        var entry = book.Entries[index];
        if (!verifiedDefinitions.Contains(entry.ItemId)) return 0;
        // A character has a finite purchasing allowance. A metadata-only item can have native value zero
        // before buyer/market placement; that is not a missing definition and cannot set a valid price.
        // The counter still determines the real quote and refuses any deal above this native allowance.
        return VisitBudgets[index];
    }

    internal static bool VisitAllowed(CommissionBook book, int index, int stage, int currentDay)
    {
        if (index is < 0 or > 2 || stage is < 0 or > 1 || book.Entries == null || book.Entries.Length != 3) return false;
        var entry = book.Entries[index];
        if (string.IsNullOrWhiteSpace(entry.ItemId)) return false;
        if (stage == 0) return CommissionRules.Choices(book, index, currentDay).Length > 0;
        return CommissionRules.CanDeliver(book, index, currentDay) && CommissionRules.Remaining(book, index) > 0 &&
            string.Equals(CommissionRules.ClientId(book, index), Identifier(index, 1), StringComparison.Ordinal);
    }

    internal static string QueueVisit(CommissionBook book, int index, int stage, int currentDay)
    {
        if (!VisitAllowed(book, index, stage, currentDay)) return "这次见面还未准备好，请先查看委托提示。";
        if (book.Entries.Any(e => e.QueuedStage >= 0)) return "已有一位委托人正在等候，请先完成或取消当前预约。";
        book.Entries[index].QueuedStage = stage;
        return CommissionRules.GetName(index) + "已收到邀请，会在营业且柜台空闲时前来。";
    }

    internal static string CancelVisit(CommissionBook book, int index)
    {
        if (index is < 0 or > 2 || book.Entries == null || book.Entries.Length != 3 || book.Entries[index].QueuedStage < 0)
            return "没有待处理的委托预约。";
        book.Entries[index].QueuedStage = -1;
        return "已取消预约，委托进度和货物保留。";
    }

    internal void Tick(PlayerStore player, CommissionBook book, int currentDay,
        Action<int, int> choice, Action write, Action close, bool allowScheduling = true)
    {
        if (owner != player.Pointer || run != player.runID)
        {
            owner = player.Pointer; run = player.runID;
            arrived.Clear(); configured.Clear(); pinned.Clear(); dismissCleanup.Clear(); terminalStarted.Clear();
        }
        store = player; commissions = book; day = currentDay; onChoice = choice; save = write;
        var current = player.currentClientInstance?.storeClient;
        foreach (var pending in dismissCleanup.ToArray())
        {
            if (Time.unscaledTime < pending.Value || current?.Pointer == pending.Key || StoreClientMono.Instance?.IsMoving() == true) continue;
            dismissCleanup.Remove(pending.Key); arrived.Remove(pending.Key); configured.Remove(pending.Key); pinned.Remove(pending.Key); terminalStarted.Remove(pending.Key);
        }
        if (current != null && TryParse(current.identifier, out int currentIndex, out int currentStage))
        {
            if (!EnsureSprites()) return;
            var oldMenu = current.mainDialogue;
            var manager = DialogUIManager.Instance;
            bool coldMenu = currentStage == 1 && current.clientIntent == StoreClient.ClientIntent.DIALOGUE &&
                book.Entries[currentIndex].Status == CommissionStatus.Accepted;
            bool ownMenuActive = manager != null && ((currentStage == 0 || coldMenu) && manager.IsChoice() || oldMenu != null &&
                manager.currentDialog != null && oldMenu.Pointer == manager.currentDialog.Pointer);
            bool rebound = Configure(current, currentIndex, currentStage);
            if (rebound && ownMenuActive && manager != null)
            { manager.InterruptDialog(); current.isMainDialogueStarted = false; current.StartMainDialogue(); }
            if (player.isClientArrived) RecordArrival(current, currentIndex, currentStage);
            var status = book.Entries[currentIndex].Status;
            if (status is CommissionStatus.Completed or CommissionStatus.Expired or CommissionStatus.Declined &&
                player.isClientArrived && manager != null && manager.GetCurrentDialog() == null &&
                manager.isConversationActiveFlag <= 0 && StoreClientMono.Instance?.IsMoving() != true && terminalStarted.Add(current.Pointer))
            { current.isMainDialogueStarted = false; current.StartMainDialogue(); }
            return;
        }
        // Updating the client already at the counter is independent of new scheduling.
        if (!allowScheduling) return;
        if (player.IsClientPresent() || player.storeState != PlayerStore.StoreState.OPEN ||
            player.playerLocation != PlayerStore.PlayerLocation.IN_STORE || player.lockAllFlag > 0 ||
            player.lockNextBellFlag > 0 || player.isHandlingShootout || player.isClientBeingArrested ||
            player.isSecurityAlarmActive || DialogUIManager.Instance?.isConversationActiveFlag > 0 ||
            StoreClientMono.Instance?.IsMoving() == true || !EnsureSprites()) return;
        for (int index = 0; index < book.Entries.Length; index++)
        {
            int stage = book.Entries[index].QueuedStage;
            if (stage < 0) continue;
            if (!VisitAllowed(book, index, stage, day))
            { book.Entries[index].QueuedStage = -1; write(); continue; }
            if (stage == 1 && BudgetFor(book, index) <= 0) continue;
            string id = Identifier(index, stage);
            bool queued = false;
            foreach (var queuedClient in player.storeClientManager.clientStack)
                if (queuedClient.identifier == id) { queued = true; break; }
            if (!queued) player.storeClientManager.AddNextClient(Create(index, stage));
            close(); player.TryCallNextClient();
            Plugin.Instance.Log.LogInfo("Commission visitor requested: " + id);
            break;
        }
    }

    internal static StoreClient Create(int index, int stage)
    {
        if (index is < 0 or > 2 || stage is < 0 or > 1) throw new ArgumentOutOfRangeException();
        bool ready = Live != null && Live.CurrentOwner(Live.owner, Live.run) &&
            Live.commissions != null && VisitAllowed(Live.commissions, index, stage, Live.day);
        bool buying = ready && stage == 1 && BudgetFor(Live!.commissions!, index) > 0;
        var client = new StoreClient
        {
            identifier = Identifier(index, stage), displayName = CommissionRules.GetName(index),
            spriteName = SpriteKeys[index], clientIntent = buying ? StoreClient.ClientIntent.BUY : StoreClient.ClientIntent.DIALOGUE,
            clientFaction = StoreClient.FACTION_MIDDLE, bubbleColor = index == 1 ? "#729c91" : "#b3a277",
            dismissable = true, arrestable = false, nonShootable = true, isNoContributeToEvidence = true,
            clientCash = 0, clientBudget = 0, useClientBudget = true, isRegularSellDisable = !buying,
            isMultiBuyDisabled = true, cooldown = 0
        };
        if (buying) PrepareBuyer(client, Live!.commissions!, index);
        client.CompleteClientCreation(true);
        if (ready) Live!.Configure(client, index, stage);
        else
        {
            // A save can reconstruct the client before this save's phone book has been loaded.
            // No live buying predicate or budget is borrowed from another run during that interval.
            client.mainDialogue = LoadingDialogue(client, index); client.repeatDialogue = client.mainDialogue;
        }
        return client;
    }

    private static void PrepareBuyer(StoreClient client, CommissionBook book, int index)
    {
        client.clientIntent = StoreClient.ClientIntent.BUY; client.isRegularSellDisable = false;
        client.isMultiBuyDisabled = true;
        client.clientBuyingTagList = new Il2CppSystem.Collections.Generic.List<string>();
        client.clientBuyingIdList = new Il2CppSystem.Collections.Generic.List<string>();
        client.clientBuyingIdList.Add(book.Entries[index].ItemId);
        client.useClientBudget = true; client.SetBudget(BudgetFor(book, index));
    }

    private bool CurrentOwner(IntPtr expectedOwner, string expectedRun) =>
        commissions != null && PhoneBehaviour.Live?.OwnsCommissionBook(commissions)==true && store != null && PlayerStore.IsInstanceExist() && owner == expectedOwner && run == expectedRun &&
        PlayerStore.Instance.Pointer == expectedOwner && PlayerStore.Instance.runID == expectedRun;

    internal bool Accepts(StoreClient client, GameItem item)
    {
        if (!CurrentOwner(owner, run) || commissions == null || item == null || item.Pointer == IntPtr.Zero ||
            store?.currentClientInstance?.storeClient.Pointer != client.Pointer ||
            !TryParse(client.identifier, out int index, out int stage) || stage != 1 ||
            !VisitAllowed(commissions, index, stage, Math.Max(0, StoreStation.GetDayCounter())) ||
            client.clientIntent != StoreClient.ClientIntent.BUY) return false;
        return item.unitCount > 0 && item.unitCount <= CommissionRules.Remaining(commissions, index) &&
            string.Equals(item.identifier, commissions.Entries[index].ItemId, StringComparison.Ordinal)&&MeetsGoodsRequirement(index,item);
    }

    internal static bool MeetsGoodsRequirement(int index,GameItem item)
    {
        if(index!=0)return true;
        if(WaterHelper.GetCurrentCapacityML(item)<500||!WaterHelper.IsFull(item))return false;
        var accepted=new Il2CppSystem.Collections.Generic.List<string>();
        accepted.Add(WaterFeatureHelper.PURE_WATER_DISPLAY);accepted.Add(WaterFeatureHelper.HIGH_QUALITY_WATER_DISPLAY);accepted.Add(WaterFeatureHelper.BASE_WATER_DISPLAY);
        // Keep the original game's water acceptance and label mechanics in control.
        return WaterHelper.WillAcceptWater(item,accepted);
    }

    private bool Configure(StoreClient client, int index, int stage)
    {
        var entry = commissions!.Entries[index];
        if (stage == 1 && VisitAllowed(commissions, index, stage, day) && BudgetFor(commissions, index) <= 0) return false;
        if (stage == 1 && VisitAllowed(commissions, index, stage, day) && client.clientIntent != StoreClient.ClientIntent.BUY)
        {
            PrepareBuyer(client, commissions, index);
            // Capture lifecycle callbacks only after native BUY initialization, preserving its default cleanup.
            client.CompleteClientCreation(true);
        }
        // Rebind text when the agreed window changes, but never refill a budget or interrupt a partial-sale dialogue.
        string signature = owner + ":" + run + ":" + entry.Status + ":" + entry.ItemId + ":" + stage + ":" +
            entry.AvailableDay + ":" + entry.Deadline;
        if (configured.TryGetValue(client.Pointer, out string? existing) && existing == signature) return false;
        configured[client.Pointer] = signature;
        if (!pinned.TryGetValue(client.Pointer, out var keep))
        { keep = new List<Delegate>(); pinned[client.Pointer] = keep; }
        IntPtr expectedOwner = owner; string expectedRun = run; CommissionStatus expectedStatus = entry.Status;
        var nativeArrival = client.OnArrival;
        var nativeDismiss = client.OnDismiss;
        Action arrival = () =>
        {
            try
            {
                if (!CurrentOwner(expectedOwner, expectedRun)) return;
                nativeArrival?.Invoke(); RecordArrival(client, index, stage);
            }
            catch (Exception ex) { Plugin.Error(ex); }
        };
        Action dismiss = () =>
        {
            try { if (CurrentOwner(expectedOwner, expectedRun)) nativeDismiss?.Invoke(); }
            catch (Exception ex) { Plugin.Error(ex); }
            finally { dismissCleanup[client.Pointer] = Time.unscaledTime + 3; }
        };
        keep.Add(arrival); keep.Add(dismiss); client.OnArrival = (Il2CppSystem.Action)arrival; client.OnDismiss = (Il2CppSystem.Action)dismiss;
        Action leave = () =>
        {
            if (CurrentOwner(expectedOwner, expectedRun) && store?.currentClientInstance?.storeClient.Pointer == client.Pointer)
                DialogUIManager.Instance?.CurrentClientLeave(.6f);
        };
        keep.Add(leave);
        if (!VisitAllowed(commissions, index, stage, day))
        {
            client.clientIntent = StoreClient.ClientIntent.DIALOGUE; client.isRegularSellDisable = true;
            string text = entry.Status == CommissionStatus.Completed ? CompletionText(index) :
                entry.Status == CommissionStatus.Expired ? "这批货没赶上窗口。我得先另找办法；你柜台上的东西仍归你。手机会保留这次结果。" :
                "这次约定已经变了。先照顾店里吧，手机上的委托记录会告诉你下一步。";
            var end = new Dialogue().SetText("CLIENT", text); end.isMainDialog = true;
            end.SetEndAction((Il2CppSystem.Action)leave);
            client.mainDialogue = end; client.repeatDialogue = end; client.allDoneDialogue = end;
            return true;
        }
        if (stage == 1)
        {
            var main = new Dialogue().SetText("CLIENT", DeliveryText(index, entry, CommissionRules.Remaining(commissions, index)));
            main.isMainDialog = true;
            client.mainDialogue = main; client.repeatDialogue = main;
            client.acceptDealDialogue = new Dialogue().SetText("CLIENT", index switch
            {
                0 => "把这件货的价格写在收据上。我按柜台议好的价付款。",
                1 => "这份补给我认。照常结账，诊所账目得清清楚楚。",
                _ => "规格对了。你开价，我按这笔柜台交易付款。"
            });
            client.allDoneDialogue = new Dialogue().SetText("CLIENT", CompletionText(index));
            client.allDoneDialogue.SetEndAction((Il2CppSystem.Action)leave);
            client.placedWrongItemWhenSellingToDialogue = new Dialogue().SetText("CLIENT", "我这次只收约定的 " + entry.ItemName + "。其他货你留给普通顾客；超过剩余数量的整组请先拆开。"+(index==0?"饮水要满瓶、容量至少500ml，等级按原版收水规则核对。":""));
            client.placeRightItemWhenSellingToDialogue = new Dialogue().SetText("CLIENT", "就是这个。放到议价台，按正常柜台价格成交就行。");
            return true;
        }
        string offer = entry.Status == CommissionStatus.Accepted ?
            CommissionRules.GetName(index) + $"重新摊开收货单。\n“{entry.ItemName} 已交 {entry.DeliveredUnits}/{entry.RequiredUnits}，交货窗口到 D{entry.Deadline}。你想多争取一天，还是不继续这次供货？已付款的部分照旧算成交。”" : OfferText(index, entry);
        var menu = new Dialogue().SetText("CLIENT", offer); menu.isMainDialog = true;
        foreach (var choice in CommissionRules.Choices(commissions, index, day))
        {
            int key = choice.Key;
            Action apply = () =>
            {
                try
                {
                    if (!CurrentOwner(expectedOwner, expectedRun) || commissions!.Entries[index].Status != expectedStatus ||
                        store?.currentClientInstance?.storeClient.Pointer != client.Pointer ||
                        !VisitAllowed(commissions, index, stage, day) ||
                        !CommissionRules.Choices(commissions, index, day).Any(c => c.Key == key)) return;
                    onChoice?.Invoke(index, key); save?.Invoke();
                }
                catch (Exception ex) { Plugin.Error(ex); }
            };
            keep.Add(apply);
            string reply = expectedStatus == CommissionStatus.Accepted && key == 1 ?
                "我把交货窗口再延长一天。已交的货和付过的款都保留，剩下的按手机的新日期安排；这次只能延长一次。" :
                expectedStatus == CommissionStatus.Accepted && key == 2 ?
                "行，这次剩下的供货就不继续了。已经成交的部分保持原样，未交的货仍归你，我们原来的合作照常。" : ReplyText(index, key);
            var answer = new Dialogue().SetText("CLIENT", reply); answer.SetEndAction((Il2CppSystem.Action)leave);
            AddChoice(menu, choice.Label, key == 0 ? "接下后自己备货，邀请对方来柜台正常议价、收款。" :
                key == 1 ? "推迟窗口，不会收走任何货物或费用。" : "谢绝这次委托，货物与现金都保留。", answer, (Il2CppSystem.Action)apply);
        }
        client.mainDialogue = menu; client.repeatDialogue = menu;
        client.allDoneDialogue = new Dialogue().SetText("CLIENT", "先照顾柜台吧，委托记录在手机里。");
        return true;
    }

    private void RecordArrival(StoreClient client, int index, int stage)
    {
        if (commissions == null || store?.currentClientInstance?.storeClient.Pointer != client.Pointer || !arrived.Add(client.Pointer)) return;
        if (commissions.Entries[index].QueuedStage == stage)
        { commissions.Entries[index].QueuedStage = -1; save?.Invoke(); }
        Plugin.Instance.Log.LogInfo("Commission visitor arrived: " + client.identifier);
    }

    private static bool EnsureSprites()
    {
        if (SpriteDict.Instance == null) return false;
        for (int index = 0; index < 3; index++)
            if (PhoneVisitors.RegisteredSprite(SpriteDict.Instance, SpriteKeys[index]) == null) return false;
        return true;
    }

    private static Dialogue LoadingDialogue(StoreClient client, int index)
    {
        var menu = new Dialogue().SetText("CLIENT", CommissionRules.GetName(index) + "正在核对收货单。手机同步好这份委托后，我们就继续。");
        menu.isMainDialog = true;
        var next = new Dialogue().SetText("CLIENT", "好，先整理你的柜台。我们稍后核对。");
        IntPtr expectedOwner = PlayerStore.IsInstanceExist() ? PlayerStore.Instance.Pointer : IntPtr.Zero;
        string expectedRun = PlayerStore.IsInstanceExist() ? PlayerStore.Instance.runID : "";
        Action leave = () =>
        {
            if (PlayerStore.IsInstanceExist() && PlayerStore.Instance.Pointer == expectedOwner && PlayerStore.Instance.runID == expectedRun &&
                PlayerStore.Instance.currentClientInstance?.storeClient.Pointer == client.Pointer)
                DialogUIManager.Instance?.CurrentClientLeave(.6f);
        };
        next.SetEndAction((Il2CppSystem.Action)leave);
        AddChoice(menu, "稍等，我先整理柜台", "保留这份委托，暂不交易。", next, null);
        return menu;
    }

    private static void AddChoice(Dialogue menu, string label, string detail, Dialogue next, Il2CppSystem.Action? action)
    {
        var reply = new Dialogue().SetText(DialogUIManager.PLAYER, label); reply.SetNextDialogue(next);
        menu.AddChoice(new GameItem("spacephone.choice.commission"), label, detail, reply, action);
    }

    private static string OfferText(int index, CommissionEntry entry) => index switch
    {
        0 => $"雨鸦把一张楼层送水单推过柜台。\n“楼下有两位老人，升降梯今天又停了。我需要 {entry.ItemName} ×{entry.RequiredUnits}，每瓶装满至少500ml，普通饮水或更好。你能备到货，我就回来当面买；价格还是照你柜台的规矩。”",
        1 => $"阿岚摘下一只手套，露出诊所盖章的补给单。\n“今晚轮值室缺 {entry.ItemName} ×{entry.RequiredUnits}。我不想让你白送，货到后在你这里正常结账。你有把握接这单吗？”",
        _ => $"洛克展开一张修理草图。\n“门外那条老货梯卡了传感器，缺 {entry.ItemName} ×{entry.RequiredUnits}。你先想想进货和周转，准备好了叫我来，按柜台价格交易。”"
    };

    private static string DeliveryText(int index, CommissionEntry entry, int remaining) =>
        CommissionRules.GetName(index) + $"把收货单摊在柜台上。\n“这单原需 {entry.ItemName} ×{entry.RequiredUnits}。手机会记录已交和剩余数量。把自己的货放到议价台，照常查看、议价和成交；我只收这张单子里的货。”\n本次交货窗口到 D{entry.Deadline}。";

    private static string ReplyText(int index, int choice) => choice switch
    {
        0 => "那就这么定。你自己备货，手机会显示数量和交货窗口。准备好了邀请我来柜台，照常议价收款；不用现在把货交出来。",
        1 => index == 1 ? "我先调一点备用补给。明天再联系，你也别为了赶单把租金压进去。" : "可以，我先安排别的事。明天再联系，手机会记住新的窗口。",
        _ => index == 0 ? "明白。做生意得看自己的余力，我再找别的送水路子。你的柜台照常开着就好。" :
            index == 1 ? "没关系，我转去别处问。这次谢绝会写在记录里，已经在你柜台的货都还是你的。" : "行，我回去换个修理计划。咱们不强做这笔，别的生意照常。"
    };

    private static string CompletionText(int index) => index switch
    {
        0 => "雨鸦折好收据，把水提到门边。\n“我走楼梯送上去。两户人的门口，今天至少有这一份。”这次委托的实物与付款都留在手机记录里。",
        1 => "阿岚把补给和收据一起封进诊所袋。\n“今晚轮值能撑过去了。钱付清，货送到，大家都能把店继续开下去。”",
        _ => "洛克确认完电子件，收起修理草图。\n“我去试着把那条货梯修通。账在你柜台结，东西送到我手里，这才算一笔合作。”"
    };
}

[HarmonyPatch(typeof(StoreClientListDict), nameof(StoreClientListDict.CreateStoreClient))]
internal static class PhoneCommissionFactoryPatch
{
    public static bool Prefix(string __0, ref StoreClient __result)
    {
        if (!PhoneCommissionClients.TryParse(__0, out int index, out int stage)) return true;
        __result = PhoneCommissionClients.Create(index, stage); return false;
    }
}

[HarmonyPatch(typeof(StoreClientListDict), nameof(StoreClientListDict.GetClientDisplayName))]
internal static class PhoneCommissionNamePatch
{
    public static bool Prefix(string __0, ref string __result)
    {
        if (!PhoneCommissionClients.TryParse(__0, out int index, out _)) return true;
        __result = CommissionRules.GetName(index); return false;
    }
}

[HarmonyPatch(typeof(StoreClient), nameof(StoreClient.IsClientBuyingThisItem))]
internal static class PhoneCommissionBuyingPatch
{
    public static void Postfix(StoreClient __instance, GameItem __0, ref bool __result)
    {
        if (!PhoneCommissionClients.TryParse(__instance.identifier, out _, out int stage) || stage != 1) return;
        // The native whitelist, condition checks and refusal still apply. This only narrows our own
        // buyers to the currently accepted order and remaining units; a native refusal stays a refusal.
        __result = __result && PhoneCommissionClients.Live?.Accepts(__instance, __0) == true;
    }
}
