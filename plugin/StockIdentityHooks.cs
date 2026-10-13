using HarmonyLib;
namespace SpacePhone;
[HarmonyPatch(typeof(GameItem),nameof(GameItem.GetSplitItem),new[]{typeof(int)})]
public static class PhoneSplitCostGuard
{
    public static void Prefix(GameItem __instance,int __0){try{if(__0>0&&__0<__instance.unitCount)PhoneBehaviour.Live?.InvalidateNativeLot(__instance);}catch(Exception ex){Plugin.Error(ex);}}
    public static void Postfix(GameItem __instance,GameItem __result){try{if(__result!=null&&__result.Pointer!=__instance.Pointer)PhoneBehaviour.Live?.InheritSplitResearch(__instance,__result);}catch(Exception ex){Plugin.Error(ex);}}
}
[HarmonyPatch(typeof(GameItem),nameof(GameItem.SetUnitCount),new[]{typeof(int)})]
public static class PhoneQuantityCostGuard
{
    public static void Prefix(GameItem __instance,int __0){try{if(__0!=__instance.unitCount)PhoneBehaviour.Live?.InvalidateNativeLot(__instance);}catch(Exception ex){Plugin.Error(ex);}}
}
[HarmonyPatch(typeof(GameItem),nameof(GameItem.unitCount),MethodType.Setter)]
public static class PhoneDirectQuantityCostGuard
{
    public static void Prefix(GameItem __instance,int __0){try{if(__0!=__instance.unitCount)PhoneBehaviour.Live?.InvalidateNativeLot(__instance);}catch(Exception ex){Plugin.Error(ex);}}
}
