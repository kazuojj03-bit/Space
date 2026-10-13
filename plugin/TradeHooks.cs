using System.Globalization;
using HarmonyLib;

namespace SpacePhone;

// This native callback carries an explicit trade direction. Using a single signal
// avoids counting the same transaction again through wallet or inventory hooks.
[HarmonyPatch(typeof(StoreReputation), nameof(StoreReputation.OnItemTraded),
    new[] { typeof(string), typeof(int), typeof(int), typeof(StoreClient), typeof(bool), typeof(GameItem) })]
public static class PhoneNativeTradePatch
{
    public sealed class Snapshot
    {
        internal IntPtr StorePointer;
        internal IntPtr ClientPointer;
        internal string RunId = "";
        internal bool Purchase;
        internal string Uid = "", ItemId = "", ItemName = "", Category = "other";
        internal int Units, Amount;
        internal string CustomerId = "", PublicName = "";
    }

    public static void Prefix(int value, StoreClient storeClient, bool isPlayerSold,
        GameItem gameItem, out Snapshot? __state)
    {
        __state = null;
        try
        {
            var phone = PhoneBehaviour.Live;
            if (value <= 0 || phone == null || !phone.Ready ||
                !PlayerStore.IsInstanceExist() || gameItem == null ||
                gameItem.Pointer == IntPtr.Zero || storeClient == null ||
                storeClient.Pointer == IntPtr.Zero) return;

            var store = PlayerStore.Instance;
            if (store == null || store.Pointer == IntPtr.Zero) return;
            var itemId = gameItem.identifier;
            int units = gameItem.unitCount;
            if (string.IsNullOrWhiteSpace(itemId) || units <= 0) return;
            if(isPlayerSold&&PhoneCommissionClients.TryParse(storeClient.identifier,out int commissionIndex,out int commissionStage)&&commissionStage==1&&
                !PhoneCommissionClients.MeetsGoodsRequirement(commissionIndex,gameItem))return;

            string category = "other";
            var types = gameItem.GetGameItemType();
            if (types != null)
                for (int i = 0; i < types.Count; i++)
                    if (!string.IsNullOrWhiteSpace(types[i]))
                    { category = types[i].Trim().ToLowerInvariant(); break; }

            var name = gameItem.GetDisplayName(false);
            var publicName = storeClient.displayName;
            __state = new Snapshot
            {
                StorePointer = store.Pointer,
                ClientPointer = storeClient.Pointer,
                RunId = store.runID ?? "",
                Purchase = !isPlayerSold,
                Uid = gameItem.uniqueId.ToString(CultureInfo.InvariantCulture),
                ItemId = itemId,
                ItemName = string.IsNullOrWhiteSpace(name) ? itemId : name,
                Category = category,
                Units = units,
                // The native signature calls this value and distinguishes it
                // from originalValue. Native transaction tests must verify that
                // it is the total payment rather than a per-unit reference price.
                Amount = value,
                CustomerId = storeClient.identifier ?? "",
                PublicName = string.IsNullOrWhiteSpace(publicName) ? "访客" : publicName
            };
        }
        catch (Exception ex) { Plugin.Error(ex); }
    }

    public static void Postfix(Snapshot? __state)
    {
        if (__state == null) return;
        try
        {
            var phone = PhoneBehaviour.Live;
            if (phone == null || !phone.Ready || !PlayerStore.IsInstanceExist()) return;
            var store = PlayerStore.Instance;
            if (store == null || store.Pointer != __state.StorePointer ||
                !string.Equals(store.runID ?? "", __state.RunId, StringComparison.Ordinal)) return;
            if (PhoneCommissionClients.TryParse(__state.CustomerId, out _, out _) &&
                store.currentClientInstance?.storeClient.Pointer != __state.ClientPointer) return;

            phone.RecordNativeTrade(__state.Purchase, __state.Uid, __state.ItemId,
                __state.ItemName, __state.Category, __state.Units, __state.Amount,
                __state.CustomerId, __state.PublicName);
        }
        catch (Exception ex) { Plugin.Error(ex); }
    }
}
