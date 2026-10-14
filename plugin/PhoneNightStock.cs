using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
namespace SpacePhone;

public sealed partial class PhoneBehaviour
{
    private readonly List<NightOrderStockLot> nightStock = new();
    private float nextNightStock;
    private bool nightStockObserved;
    [HideFromIl2Cpp] private IReadOnlyList<NightOrderStockLot> NightStock(bool force = false)
    {
        if (!force && Time.unscaledTime < nextNightStock) return nightStock;
        nextNightStock = Time.unscaledTime + 3; nightStock.Clear(); nightStockObserved = false;
        if (!OwnsPhoneRun() || Store.playerLocation != PlayerStore.PlayerLocation.IN_STORE) return nightStock;
        Il2CppSystem.Collections.Generic.List<GameItem>? owned;
        try { owned = EmporiumEntry.Instance.GetAllOwnedItems(true, true); }
        catch (Exception ex) { Plugin.Instance.Log.LogWarning("Night stock unavailable: " + ex.GetType().Name); return nightStock; }
        if (owned == null || owned.Count > 3000) return nightStock;
        nightStockObserved = true;
        var seen = new HashSet<int>();
        for (int i = 0; i < owned.Count && i < 3000; i++)
        {
            try
            {
                var item = owned[i];
                if (item == null || item.Pointer == IntPtr.Zero || item.uniqueId <= 0 || item.unitCount is < 1 or > 100000 ||
                    !GeneralHelper.IsItemEffectivelyOwned(item) || !seen.Add(item.uniqueId) || string.IsNullOrWhiteSpace(item.identifier) ||
                    item.identifier.StartsWith("spacephone.", StringComparison.Ordinal) || item.unitBaseValue is < 1 or > 10000) continue;
                var nativeTypes = item.GetGameItemType(); if (nativeTypes == null) continue;
                var types = new List<string>();
                foreach (var type in nativeTypes) if (!string.IsNullOrWhiteSpace(type)) types.Add(type.Trim().ToLowerInvariant());
                if (types.Any(t => t.Contains("contraband") || t.Contains("weapon") || t.Contains("gun") || t.Contains("medicine") || t.Contains("medical"))) continue;
                NightOrderCategory category; bool water = types.Contains("water");
                if (water || types.Contains("food")) category = NightOrderCategory.Food;
                else if (types.Any(t => t is "household" or "amenity" or "amenities")) category = NightOrderCategory.Supplies;
                else if (types.Any(t => t is "material" or "tool" or "electronic" or "electronics" or "component" or "scrap" or "mineral")) category = NightOrderCategory.Repair;
                else continue;
                if (water && !PhoneCommissionClients.MeetsGoodsRequirement(0, item)) continue;
                string uid = item.uniqueId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var lot = B.Lots.FirstOrDefault(l => l.ItemUid == uid && l.ItemId == item.identifier);
                long? cost = lot?.CostKnown == true && lot.Units == item.unitCount ? lot.Cost : null;
                string name = System.Text.RegularExpressions.Regex.Replace(item.GetDisplayName(false) ?? "", "<[^>]*>", "").Replace("\r", " ").Replace("\n", " ").Trim();
                if (name.Length > 160) name = name[..159] + "…";
                nightStock.Add(new(item.uniqueId, item.identifier, string.IsNullOrWhiteSpace(name) ? item.identifier : name,
                    item.unitCount, category, item.unitBaseValue, cost, water));
            }
            catch (Exception ex) { nightStockObserved = false; Plugin.Instance.Log.LogWarning("Night order stock observation skipped: " + ex.GetType().Name); }
        }
        return nightStock;
    }
    [HideFromIl2Cpp] private long NightMatchingStock(NightOrderOffer offer)
    { var stock = NightStock(); return nightStockObserved ? stock.Where(l => l.ItemId == offer.ItemId && l.RequiresDrinkingWater == offer.RequiresDrinkingWater).Sum(l => (long)l.Units) : -1; }
    [HideFromIl2Cpp] private static string NightStockText(long units) => units < 0 ? "现货同步中" : "现货可交 " + units;
    [HideFromIl2Cpp] private void ClearNightStock() { nightStock.Clear(); nextNightStock = 0; nightStockObserved = false; }
}
