using Il2CppInterop.Runtime;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;
using NativeFactory = Il2CppSystem.Func<GameItem>;
using NativeInitHook = Il2CppSystem.Action<ModItemDirectory>;
using NativeSpriteMap = Il2CppSystem.Collections.Generic.Dictionary<string, UnityEngine.Sprite>;

namespace SpacePhone;

/// <summary>
/// A session-rooted original item definition. Registration and Spawn still require final native acceptance.
/// The factory describes merchandise only; native clients and trades own every inventory and cash change.
/// </summary>
internal sealed class LostCallItemDefinition
{
    internal const string ItemId = "spacephone.item.route_recorder";
    internal const string TypeId = "spacephone.type.route_recording";
    internal const string AtlasId = "spacephone.original.route_recorder.v1";
    internal const string SpriteKey = "route_recorder";
    internal const string DisplayName = "航线录音匣";
    internal const int DefinitionValue = 40;
    internal const bool RequiresNativeAcceptance = true;

    // Native directory slots can retain these delegates after a plugin unload or a scene change.
    // A detached owner is deliberately kept alive; it is not an uninstall mechanism for live items.
    private static readonly List<LostCallItemDefinition> sessionRoots = new();
    internal static LostCallItemDefinition? Live { get; private set; }
    private readonly int mainThread;
    private readonly Func<GameItem> managedFactory;
    private readonly Action<ModItemDirectory> managedInitHook;
    private readonly NativeFactory factory;
    private readonly NativeInitHook initHook;
    private readonly Dictionary<IntPtr, ModItemDirectory> ownedDirectories = new();
    private readonly List<GameItem> retainedUnplacedItems = new();
    private readonly HashSet<int> issuedUids = new();
    private readonly HashSet<IntPtr> issuedPointers = new();
    private readonly HashSet<IntPtr> ownFactoryProducts = new();
    private readonly HashSet<string> reported = new(StringComparer.Ordinal);
    private Texture2D? ownTexture;
    private Sprite? ownSprite;
    private NativeSpriteMap? ownAtlas;
    private const string ConstraintOwner = "spacephone.original.route-recorder-constraints";
    private readonly Harmony constraints = new(ConstraintOwner);
    private readonly List<(MethodInfo Target, MethodInfo Patch, bool IsPrefix)> constraintHooks = new();
    private bool subscribed;
    private bool disabled;
    private bool lastReady;
    private int factoryInvocations;
    private IntPtr lastFactoryProduct;
    private IntPtr sellerOwner;
    private string sellerRun = "";
    private string factoryFailure = "";
    internal bool Ready => subscribed && !disabled && lastReady;
    internal bool HasCreatedDefinitions => factoryInvocations != 0;
    internal bool InitHookObserved { get; private set; }
    internal int FactoryInvocations => factoryInvocations;
    internal string LastError { get; private set; } = "等待原生物品目录初始化。";

    private LostCallItemDefinition()
    {
        mainThread = Environment.CurrentManagedThreadId;
        managedFactory = BuildDefinition;
        managedInitHook = OnDirectoryInitialized;
        factory = (NativeFactory)managedFactory;
        initHook = (NativeInitHook)managedInitHook;
    }

    /// <summary>Call early from Plugin.Load on Unity's main thread, before native saved-item reconstruction.</summary>
    internal static LostCallItemDefinition Install()
    {
        var definition = Live;
        if (definition == null)
        {
            definition = new LostCallItemDefinition();
            sessionRoots.Add(definition);
            Live = definition;
        }
        definition.AttachInitHook();
        return definition;
    }

    private void AttachInitHook()
    {
        if (!OnMainThread() || subscribed || disabled) return;
        try
        {
            AttachConstraints();
            ModHook.add_OnModItemDirectoryInit(initHook);
            subscribed = true;
            LastError = "等待原生物品目录初始化。";
        }
        catch (Exception ex) { Disable("物品初始化回调暂不可用：" + ex.GetType().Name); }
    }

    /// <summary>Stops new story merchandise. Existing factories, delegates, textures and items stay rooted.</summary>
    internal void DetachInitHook()
    {
        lastReady = false;
        if (!OnMainThread() || !subscribed) return;
        subscribed = false;
        try { ModHook.remove_OnModItemDirectoryInit(initHook); }
        catch (Exception ex)
        {
            // The still-rooted callback becomes inert even if native removal failed. Do not reattach it twice.
            Disable("退订物品回调失败；已停止新出货并保留定义：" + ex.GetType().Name);
        }
    }

    private void OnDirectoryInitialized(ModItemDirectory directory)
    {
        if (!OnMainThread() || disabled || !subscribed) return;
        InitHookObserved = true;
        try { RefreshRegistration(directory); }
        catch (Exception ex) { Disable("录音匣初始化已暂停：" + ex.GetType().Name + ": " + ex.Message); }
    }

    /// <summary>
    /// Refreshes the latest live-directory proof before scheduling or spawning. No merchandise is made here.
    /// Enumerating live ItemDirectory roots does not prove undocumented global-loader or save-read timing.
    /// </summary>
    internal bool TryEnsureReady()
    {
        if (!OnMainThread() || disabled || !subscribed) return false;
        try { return RefreshRegistration(null); }
        catch (Exception ex) { Disable("录音匣定义暂不可用：" + ex.GetType().Name + ": " + ex.Message); return false; }
    }

    private bool RefreshRegistration(ModItemDirectory? eventDirectory)
    {
        lastReady = false;
        VerifyConstraints();
        var roots = new Dictionary<IntPtr, ItemDirectory>();
        foreach (var native in Object.FindObjectsOfType(Il2CppType.Of<ItemDirectory>(), true))
        {
            var directory = native?.TryCast<ItemDirectory>();
            if (directory != null && directory.Pointer != IntPtr.Zero) roots[directory.Pointer] = directory;
        }
        if (eventDirectory != null && eventDirectory.Pointer != IntPtr.Zero)
            roots[eventDirectory.Pointer] = eventDirectory;
        if (roots.Count == 0) { LastError = "等待原生物品目录初始化。"; return false; }
        foreach (var directory in roots.Values)
        {
            if (directory.factoryDictionary == null) { LastError = "物品目录仍在初始化。"; return false; }
            if ((directory.Has(ItemId) || directory.factoryDictionary.ContainsKey(ItemId)) && !OwnsSlot(directory))
            { Disable("另一份物品定义占用了录音匣 ID；失物来电已暂停，原有定义保持不变。"); return false; }
        }
        var targets = roots.Values.Select(root => root.TryCast<ModItemDirectory>())
            .Where(root => root != null && root.Pointer != IntPtr.Zero).ToArray();
        if (targets.Length == 0) { LastError = "等待原生 Mod 物品目录。"; return false; }
        if (!TryPrepareOriginalSprite()) return false;
        foreach (var directory in targets)
        {
            if (directory == null) continue;
            if (OwnsSlot(directory)) continue;
            // Add only. Never use Set, replace a dictionary, rename a native ID or overwrite another factory.
            bool added = directory.Add(ItemId, factory);
            if (!added || !directory.Has(ItemId) || !directory.factoryDictionary.TryGetValue(ItemId, out var registered) ||
                registered == null || registered.Pointer != factory.Pointer)
            {
                TryRemoveNeverInvokedOwnSlot(directory);
                Disable("原生目录未接受录音匣工厂；失物来电已暂停。");
                return false;
            }
            ownedDirectories[directory.Pointer] = directory;
        }
        lastReady = targets.All(directory => directory != null && OwnsSlot(directory)) && ExactSpriteResolves();
        LastError = lastReady ? "" : "等待录音匣原生定义与贴图。";
        return lastReady;
    }

    private bool OwnsSlot(Directory<GameItem> directory) =>
        ownedDirectories.ContainsKey(directory.Pointer) && directory.factoryDictionary != null &&
        directory.factoryDictionary.TryGetValue(ItemId, out var registered) && registered != null &&
        registered.Pointer == factory.Pointer;

    private bool TryRemoveNeverInvokedOwnSlot(ModItemDirectory directory)
    {
        if (factoryInvocations != 0 || directory.factoryDictionary == null ||
            !directory.factoryDictionary.TryGetValue(ItemId, out var registered) || registered == null ||
            registered.Pointer != factory.Pointer) return false;
        bool removed = directory.Remove(ItemId);
        if (removed) ownedDirectories.Remove(directory.Pointer);
        return removed;
    }

    /// <summary>
    /// Creates one native merchandise object for an actual SELL client. Does not assign a UID, ownership,
    /// parent, inventory or money. The caller must use the normal native seller setup and paid trade.
    /// </summary>
    internal bool TrySpawnForSeller(out GameItem? item, out string reason)
    {
        item = null;
        reason = "";
        if (!TryEnsureReady()) { reason = LastError; return false; }
        if (!PlayerStore.IsInstanceExist() || EmporiumEntry.Instance == null)
        { reason = "请进入店铺后再预约余灯。"; return false; }
        try
        {
            var currentStore = PlayerStore.Instance;
            if (sellerOwner != currentStore.Pointer || sellerRun != currentStore.runID)
            {
                // Native UID scopes can restart in a new shop. Definition lifetime is independent of that scope.
                sellerOwner = currentStore.Pointer; sellerRun = currentStore.runID;
                issuedUids.Clear(); issuedPointers.Clear();
            }
            var existingPointers = new HashSet<IntPtr>();
            var existingUids = new HashSet<int>(issuedUids);
            var stock = EmporiumEntry.Instance.GetAllItems();
            if (stock == null) { reason = "原生店铺库存尚未就绪。"; return false; }
            foreach (var existing in stock)
                if (existing != null)
                {
                    existingPointers.Add(existing.Pointer);
                    if (existing.uniqueId > 0) existingUids.Add(existing.uniqueId);
                }
            foreach (var pointer in issuedPointers) existingPointers.Add(pointer);
            int callsBefore = factoryInvocations;
            lastFactoryProduct = IntPtr.Zero;
            factoryFailure = "";
            var spawned = ItemSpawner.Spawn(ItemId);
            bool valid = spawned != null && spawned.Pointer != IntPtr.Zero &&
                factoryInvocations == callsBefore + 1 && factoryFailure.Length == 0 &&
                lastFactoryProduct == spawned.Pointer && spawned.identifier == ItemId &&
                spawned.uniqueId > 0 && spawned.unitCount == 1 && spawned.parentInventory == null &&
                !existingPointers.Contains(spawned.Pointer) && !existingUids.Contains(spawned.uniqueId) &&
                spawned.spriteAtlasPath == AtlasId && spawned.spritePath == SpriteKey &&
                spawned.unitBaseValue == DefinitionValue && spawned.shape != null &&
                spawned.shape.width == 2 && spawned.shape.height == 1;
            if (!valid)
            {
                // Never destroy a returned object whose native identity/ownership could not be proved.
                if (spawned != null) retainedUnplacedItems.Add(spawned);
                Disable("录音匣原生生成结果未通过身份门禁；未交付货物，剧情已暂停。");
                reason = LastError;
                return false;
            }
            item = spawned;
            issuedUids.Add(spawned!.uniqueId);
            issuedPointers.Add(spawned.Pointer);
            // Retain the original wrapper without later changing or destroying its native object.
            retainedUnplacedItems.Add(spawned);
            return true;
        }
        catch (Exception ex)
        {
            Disable("余灯的货物准备已暂停：" + ex.GetType().Name + ": " + ex.Message);
            reason = LastError;
            return false;
        }
    }

    // Native inventory reduction uses Delegate.DynamicInvoke; a managed Func bridge cannot safely
    // participate there. Retain constructor-native callbacks and constrain only this original ID.
    // These session-owned constraints remain rooted with live item definitions across plugin detach.
    private void AttachConstraints()
    {
        if (constraintHooks.Count != 0) { VerifyConstraints(); return; }
        void Hook(string name, Type[] args, string patchName, bool isPrefix)
        {
            var target = typeof(GameItem).GetMethod(name, BindingFlags.Instance | BindingFlags.Public, null, args, null)
                ?? throw new MissingMethodException("GameItem." + name);
            var patch = typeof(LostCallItemDefinition).GetMethod(patchName, BindingFlags.Static | BindingFlags.NonPublic)!;
            constraints.Patch(target, prefix: isPrefix ? new HarmonyMethod(patch) : null, postfix: isPrefix ? null : new HarmonyMethod(patch));
            constraintHooks.Add((target, patch, isPrefix));
        }
        Hook("MaxNumAdd", Type.EmptyTypes, nameof(MaxAddPostfix), false);
        Hook("MaxNumAdd", new[] { typeof(SlotMarker) }, nameof(MaxAddPostfix), false);
        Hook("MayStack", new[] { typeof(GameItem) }, nameof(MayStackPrefix), true);
        VerifyConstraints();
    }
    private void VerifyConstraints()
    {
        if (constraintHooks.Count != 3 || constraintHooks.Any(h =>
            (h.IsPrefix ? Harmony.GetPatchInfo(h.Target)?.Prefixes : Harmony.GetPatchInfo(h.Target)?.Postfixes)?.Any(p => p.owner == ConstraintOwner && p.PatchMethod == h.Patch) != true))
            throw new InvalidOperationException("Original recorder inventory constraints are not installed.");
    }
    private static void MaxAddPostfix(GameItem __instance, ref int __result)
    {
        // Keep all original capacity/slot refusals. This only tightens a positive limit.
        if (IsOwnFactoryItem(__instance)) __result = Math.Min(__result, 1);
    }
    private static bool MayStackPrefix(GameItem __instance, ref bool __result)
    {
        if (!IsOwnFactoryItem(__instance)) return true;
        __result = false; return false;
    }

    private static bool IsOwnFactoryItem(GameItem? item)
    {
        var definition = Live;
        return definition != null && item != null && definition.ownedDirectories.Count > 0
            && definition.ownFactoryProducts.Contains(item.Pointer) && item.identifier == ItemId
            && item.spriteAtlasPath == AtlasId && item.spritePath == SpriteKey && definition.ExactSpriteResolves();
    }

    private GameItem BuildDefinition()
    {
        factoryInvocations++;
        try
        {
            var item = new GameItemElement(ItemId, AtlasId, SpriteKey);
            item.SetName(DisplayName);
            item.shortDescription = "封存旧航线公开播报的便携录音匣。";
            item.longDescription = "余灯已获这份公开录音的转交许可。完成柜台买卖后，可在手机选择它的新去向。";
            item.flavorText = "外壳磨旧了。最后一盏提示灯仍然亮着。";
            item.SetValue(DefinitionValue);
            item.unitBaseValue = DefinitionValue;
            item.SetUnitCount(1);
            item.SetGameItemType(TypeId);
            item.forceDisableUse = true;
            item.forceDisableActivate = true;
            item.SetSprite(AtlasId, SpriteKey);
            item.SetShape(new GridShapeBuilder().SetDataFill(2, 1, (byte)1).Build());
            ownFactoryProducts.Add(item.Pointer);
            lastFactoryProduct = item.Pointer;
            return item;
        }
        catch (Exception ex)
        {
            factoryFailure = ex.GetType().Name + ": " + ex.Message;
            Disable("录音匣工厂暂不可用：" + ex.GetType().Name);
            // An exception must not escape a native callback and break unrelated item loading.
            return null!;
        }
    }

    private bool TryPrepareOriginalSprite()
    {
        var cache = RenderHandler.atlasCache;
        if (cache == null) { LastError = "等待物品图集初始化。"; return false; }
        if (cache.TryGetValue(AtlasId, out var existing))
        {
            bool own = ownAtlas != null && ownSprite != null && existing != null && existing.Pointer == ownAtlas.Pointer &&
                existing.Count == 1 && existing.TryGetValue(SpriteKey, out var sprite) && sprite != null && sprite.Pointer == ownSprite.Pointer;
            if (!own) { Disable("录音匣图集命名空间已被占用；原有图集保持不变。"); return false; }
            if (!ExactSpriteResolves()) { LastError = "原生渲染器尚未解析录音匣贴图。"; return false; }
            return true;
        }
        if (ownSprite == null)
        {
            // Original, code-native pixel art. No game sprite, atlas, font or reference image is read.
            var pixels = new Color[32 * 16];
            void Fill(int x1, int y1, int x2, int y2, Color color)
            { for (int y = y1; y <= y2; y++) for (int x = x1; x <= x2; x++) pixels[y * 32 + x] = color; }
            var dark = new Color(.10f, .12f, .13f, 1);
            var metal = new Color(.35f, .37f, .36f, 1);
            var edge = new Color(.52f, .53f, .47f, 1);
            Fill(2, 2, 29, 12, dark);
            Fill(3, 3, 28, 12, metal);
            Fill(4, 11, 27, 12, edge);
            Fill(4, 4, 19, 10, new Color(.20f, .22f, .22f, 1));
            Fill(5, 7, 18, 9, new Color(.39f, .40f, .32f, 1));
            Fill(6, 8, 17, 8, new Color(.57f, .54f, .40f, 1));
            Fill(8, 5, 15, 5, dark);
            Fill(21, 4, 26, 10, new Color(.14f, .16f, .16f, 1));
            Fill(22, 7, 25, 8, new Color(.84f, .55f, .22f, 1));
            Fill(22, 5, 23, 5, edge);
            Fill(25, 5, 26, 5, edge);
            Fill(4, 4, 4, 4, edge);
            Fill(27, 10, 27, 10, dark);
            Fill(9, 13, 22, 13, new Color(.26f, .28f, .26f, 1));
            ownTexture = new Texture2D(32, 16, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = AtlasId };
            ownTexture.SetPixels(pixels);
            ownTexture.Apply();
            // 16 PPU is provisional; actual inventory/counter grid alignment is part of final visual acceptance.
            ownSprite = Sprite.Create(ownTexture, new Rect(0, 0, 32, 16), new Vector2(.5f, .5f), 16, 0, SpriteMeshType.FullRect);
        }
        ownAtlas ??= new NativeSpriteMap();
        if (!ownAtlas.ContainsKey(SpriteKey)) ownAtlas.Add(SpriteKey, ownSprite);
        cache.Add(AtlasId, ownAtlas);
        if (!ExactSpriteResolves()) { LastError = "原生渲染器尚未解析录音匣贴图。"; return false; }
        return true;
    }

    private bool ExactSpriteResolves()
    {
        var resolved = RenderHandler.LoadFromAtlas(AtlasId, SpriteKey);
        return ownSprite != null && resolved != null && resolved.Pointer == ownSprite.Pointer;
    }

    private bool OnMainThread()
    {
        if (Environment.CurrentManagedThreadId == mainThread) return true;
        lastReady = false;
        LastError = "录音匣操作等待 Unity 主线程。";
        return false;
    }

    private void Disable(string message)
    { disabled = true; lastReady = false; LastError = message; ReportOnce(message); }

    private void ReportOnce(string message)
    {
        if (!reported.Add(message)) return;
        try { Plugin.Instance?.Log.LogWarning("LostCall item: " + message); } catch { /* diagnostics cannot break native loading */ }
    }
}
