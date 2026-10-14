using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace SpacePhone;

[BepInPlugin("kazuo.spacephone", "深空手机 SpacePhone", "3.2.0")]
public sealed class Plugin : BasePlugin
{
    internal static Plugin Instance = null!;
    private Harmony? harmony;
    public override void Load()
    {
        Instance = this;
        // Subscribe before the native game reconstructs saved items or initializes its directories.
        // A new-story registration failure must leave the original phone and counter usable.
        try { LostCallItemDefinition.Install(); }
        catch (Exception ex) { Log.LogError("Lost Call item registration paused: " + ex); }
        AddComponent<PhoneBehaviour>();
        harmony = new Harmony("kazuo.spacephone");
        harmony.PatchAll();
        Log.LogInfo("SpacePhone 3.2.0 loaded. In the shop press F6 or click the telephone.");
    }
    public override bool Unload()
    {
        PhoneBehaviour.Live?.Close();
        try { LostCallItemDefinition.Live?.DetachInitHook(); }
        catch (Exception ex) { Log.LogError("Lost Call registration detach failed: " + ex); }
        harmony?.UnpatchSelf();
        if (PhoneBehaviour.Live != null) UnityEngine.Object.Destroy(PhoneBehaviour.Live);
        return true;
    }
    internal static void Error(Exception ex) => Instance.Log.LogError(ex);
}

[HarmonyPatch(typeof(StorePhoneObject), nameof(StorePhoneObject.OnClick))]
public static class PhoneClickPatch
{
    public static bool Prefix()
    {
        var phone = PhoneBehaviour.Live;
        if (phone == null || !phone.Ready) return true;
        if(PhoneInput.IsBlocked)return false;
        try { phone.Toggle(); return false; }
        catch (Exception ex) { Plugin.Error(ex); return true; }
    }
}

[HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.SaveGame))]
public static class PhoneSavePatch
{
    public static void Prefix() { try { PhoneBehaviour.Live?.WriteState(); } catch (Exception ex) { Plugin.Error(ex); } }
}

[HarmonyPatch(typeof(StoreUIManager),nameof(StoreUIManager.CheckRaycast))]
public static class PhoneBlockShopRaycast { public static bool Prefix() => PhoneBehaviour.Live?.IsOpen!=true; }

[HarmonyPatch(typeof(PlayerStore),nameof(PlayerStore.ModCash))]
public static class PhoneShopLedger
{
    public static void Prefix(PlayerStore __instance,out int __state)=>__state=__instance.GetCash();
    public static void Postfix(PlayerStore __instance,int __state){try{PhoneBehaviour.Live?.RecordShopCash(__instance.GetCash()-__state);}catch(Exception ex){Plugin.Error(ex);}}
}
