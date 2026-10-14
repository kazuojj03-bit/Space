using Il2CppInterop.Runtime.Attributes;
namespace SpacePhone;

public sealed partial class PhoneBehaviour
{
    private int nightHistoryPage;
    private string nightReceiptId = "";
    [HideFromIl2Cpp] internal bool OwnsNightOrderBook(NightOrderBook book) => OwnsPhoneRun() && ReferenceEquals(state!.NightOrders, book);
    [HideFromIl2Cpp] private bool NightOrderHasPending() => state != null && NightOrderClients.HasPending(state.NightOrders);
    [HideFromIl2Cpp] private bool PreferNightOrder() => state != null && !state.NightOrders.Disabled && (state.NightOrders.Active != null || NightOrderHasPending());
    [HideFromIl2Cpp] private void OpenNightOrders() => Navigate("夜班求购");
    [HideFromIl2Cpp] private void OpenNightHistory() { nightReceiptId = ""; nightHistoryPage = 0; Navigate("夜班收据"); }
    [HideFromIl2Cpp] private void RefreshNightBoard()
    {
        if (!OwnsPhoneRun() || state!.NightOrders.Active != null || Store.playerLocation != PlayerStore.PlayerLocation.IN_STORE) return;
        int trades = (int)Math.Min(int.MaxValue, (long)B.PurchaseCount + B.SaleCount);
        var stock = NightStock(); if (!nightStockObserved) return;
        var reply = NightOrderRules.Refresh(state.NightOrders, Day, trades, stock);
        if (reply.Changed) { WriteState(); nextDraw = 0; }
    }
    [HideFromIl2Cpp] private void TickNightOrders()
    {
        if (!OwnsPhoneRun()) return;
        nightOrderClients.Tick(Store, state!.NightOrders, Day, WriteState, Close, pendingAction == null && confirmAction == null && !NativePhoneBusy());
        // Register only when the player opens this optional board, after the shop has actually appeared.
        if (visible && page == "夜班求购" && (state.NightOrders.UnlockedDay < 0 || state.NightOrders.LastBoardDay < Day)) RefreshNightBoard();
    }
    [HideFromIl2Cpp] private void AcceptNightOrder(NightOrderBook expectedBook, string orderId, int observedDay)
    {
        if (!OwnsNightOrderBook(expectedBook) || Day != observedDay) { message = "需求日期已变化，请重新查看这张单。"; return; }
        if (NightOrderHasPending()) { message = "当前买方尚在来访流程，先处理这次柜台。"; return; }
        var reply = NightOrderRules.Accept(expectedBook, orderId, Day); message = reply.Message;
        if (reply.Changed) { WriteState(); nextDraw = 0; }
    }
    [HideFromIl2Cpp] private void InviteNightOrder()
    {
        if (!OwnsPhoneRun()) return;
        if (state!.PendingSupplier >= 0 || state.PendingRecommendation || state.Stories.Threads.Any(t => t.QueuedVisitScene >= 0) ||
            state.Commissions.Entries.Any(c => c.QueuedStage >= 0) || LostCallHasPending())
        { message = "已有到访预约，先完成或取消它，再约夜班买方。"; return; }
        if (!NightOrderClients.Ready) { message = "买方资料还在同步，求购会保留。"; return; }
        message = NightOrderClients.QueueVisit(state.NightOrders, Day); WriteState(); nextDraw = 0;
        if (NightOrderHasPending()) Close();
    }
    [HideFromIl2Cpp] private NightOrderGuide CurrentNightGuide()
    {
        var book = state!.NightOrders;
        long owned = book.Active != null ? NightMatchingStock(book.Active.Offer) : -1;
        bool atCounter = Store.isClientArrived && NightOrderClients.IsOwnId(Store.currentClientInstance?.storeClient.identifier);
        return NightOrderRules.GetGuide(book, Day, owned, atCounter);
    }
    [HideFromIl2Cpp] private void RunNightGuide(NightOrderBook expectedBook, NightOrderGuide guide)
    {
        if (!OwnsNightOrderBook(expectedBook)) return;
        if (NightOrderHasPending() || guide.PrimaryAction == NightOrderAction.ReturnToCounter) { Close(); return; }
        if (guide.PrimaryAction == NightOrderAction.ViewReceipt) { OpenNightHistory(); return; }
        if (guide.PrimaryAction == NightOrderAction.CheckInventory) { Navigate("货柜"); return; }
        if (guide.CanInvite)
        {
            var plan = NightOrderRules.PlanVisit(expectedBook, Day);
            if (plan == null) { message = "收货安排已变化，请查看当前步骤。"; return; }
            Ask("预约夜班收货", "邀请免费。剩余预算 " + plan.Value.ProposedBuyerBudget + "，还需 " + plan.Value.RemainingUnits + " 件。\n\n把合适数量放上柜台，谈好实际价格后才结账。未成交可以留货，已接单没有期限。", () =>
            {
                if (!OwnsNightOrderBook(expectedBook) || NightOrderRules.PlanVisit(expectedBook, Day) != plan) { message = "货量或余款已变化，请重新确认。"; return; }
                InviteNightOrder();
            }); return;
        }
        Close();
    }
    [HideFromIl2Cpp] private void AskCancelNightOrder()
    {
        if (!OwnsPhoneRun() || state!.NightOrders.Active is not { } active) return;
        if (NightOrderHasPending()) { message = "先正常结束当前来访，或取消尚未进场的预约，再取消未交余量。"; return; }
        var expectedBook = state.NightOrders; int revision = active.PlanRevision, day = Day;
        Ask("取消未交余量", "已经卖出的 " + active.DeliveredUnits + " 件与实收 " + active.ActualPaidTotal + " 保留。\n只取消尚未交的部分，没有罚金或退款。今天不再接新单，明天再看需求。", () =>
        {
            if (!OwnsNightOrderBook(expectedBook) || expectedBook.Active != active || active.PlanRevision != revision || Day != day || NightOrderHasPending())
            { message = "这张单或来访安排已变化，请重新确认。"; return; }
            var reply = NightOrderRules.Cancel(expectedBook, Day); message = reply.Message;
            if (reply.Changed) { WriteState(); nextDraw = 0; }
        });
    }
    [HideFromIl2Cpp] private void DrawNightOrders()
    {
        RefreshNightBoard(); var book = state!.NightOrders; var guide = CurrentNightGuide();
        Btn(36, 183, 179, 34, "熟人委托", () => OpenCommissions(-1)); Btn(225, 183, 179, 34, "夜班求购", () => {}); Fill(225, 216, 179, 2, Gold);
        if (book.Active is { } active)
        {
            Label(42, 232, 240, 35, NightOrderConversation.Name(active.Offer.BuyerKind) + " · 收货单", true);
            Small(42, 270, 350, NightOrderConversation.Role(active.Offer.BuyerKind));
            view!.Card(36, 309, 368, 102, active.Offer.ItemName + " ×" + active.Offer.RequiredUnits,
                "已交 " + active.DeliveredUnits + "/" + active.Offer.RequiredUnits, "实收 " + Money(active.ActualPaidTotal) + " · 余款 " + NightOrderRules.RemainingBudget(book), Gold);
            Fill(36, 426, 368, 120, Panel); Small(49, 437, 340, NightStockText(NightMatchingStock(active.Offer)) + " · 没有截止日");
            view.Label(49, 471, 340, 64, NightOrderHasPending() ? "已收到预约。先顾当前柜台，买方会正常进门。未交余量跨日保留。" : guide.Instruction, false, false, 15);
            Btn(36, 561, 368, 42, NightOrderHasPending() ? "回柜台等候" : guide.PrimaryLabel, () => RunNightGuide(book, guide));
            Btn(36, 618, 179, 35, "查看货柜", () => Navigate("货柜")); Btn(225, 618, 179, 35, "已成交收据", OpenNightHistory);
            if (NightOrderHasPending()) Btn(36, 668, 368, 33, "取消尚未进场的预约", () => { message = NightOrderClients.CancelVisit(book); WriteState(); nextDraw = 0; });
            else Btn(36, 668, 368, 33, "取消未交余量", AskCancelNightOrder);
            return;
        }
        Small(42, 233, 350, NightOrderRules.ReputationName(book) + " · 实单完成 " + book.TotalCompleted + " 单");
        Small(42, 267, 350, "今天选一单 · 以后更大的单最多 " + NightOrderRules.UnitLimit(book) + " 件");
        if (book.Disabled || book.UnlockedDay < 0 || book.Offers.Length == 0 || book.LastAcceptedDay >= Day || book.LastClosedDay >= Day)
        {
            view!.Card(36, 312, 368, 111, "当前提示", guide.Step, guide.Progress, Accent);
            Label(49, 447, 340, 105, guide.Instruction);
            Btn(36, 577, 368, 42, book.History.Count > 0 ? "查看实际成交收据" : "先继续营业", () => { if (book.History.Count > 0) OpenNightHistory(); else Close(); });
            Small(42, 650, 350, "接单和预约不付款 · 成交走柜台议价"); return;
        }
        for (int i = 0; i < book.Offers.Length; i++)
        {
            var offer = book.Offers[i]; float y = 308 + i * 106; int observedDay = Day;
            Surface(36, y, 368, 93, "night-offer:" + offer.OrderId, () =>
            {
                long owned = NightMatchingStock(offer);
                Ask("接下 " + NightOrderConversation.Name(offer.BuyerKind) + " 的求购", offer.ItemName + " ×" + offer.RequiredUnits + " · " + NightStockText(owned) + "\n买方总预算 " + offer.ProposedTotalBudget + "。没有期限，价格到柜台再谈。\n今天只选这一单；接下不会移走库存或产生收入。", () => AcceptNightOrder(book, offer.OrderId, observedDay));
            });
            Fill(36, y, 3, 93, Accent);
            Label(49, y + 8, 337, 27, NightOrderConversation.Name(offer.BuyerKind) + " · " + offer.ItemName);
            Small(49, y + 40, 337, "需求 " + offer.RequiredUnits + " 件 · " + NightStockText(NightMatchingStock(offer)));
            Small(49, y + 65, 337, "总预算 " + offer.ProposedTotalBudget + " · " + NightOrderConversation.Role(offer.BuyerKind));
        }
        Btn(36, 649, 179, 35, "查看货柜", () => Navigate("货柜")); Btn(225, 649, 179, 35, "合作收据", OpenNightHistory);
        Small(42, 695, 350, "预算不是成交价 · 未接需求次日按现货登记");
    }
    [HideFromIl2Cpp] private void DrawNightOrderHistory()
    {
        var book = state!.NightOrders;
        var selected = book.History.FirstOrDefault(h => h.Offer.OrderId == nightReceiptId);
        if (selected != null)
        {
            Label(42, 190, 244, 36, NightOrderConversation.Name(selected.Offer.BuyerKind) + " · 回执", true);
            Btn(288, 189, 116, 34, "收据列表", () => nightReceiptId = "");
            view!.Card(36, 243, 368, 111, selected.Offer.ItemName, selected.DeliveredUnits + "/" + selected.Offer.RequiredUnits + " 件 · 实收 " + Money(selected.ActualPaidTotal), "D" + selected.AcceptedDay + " 接单 · D" + selected.ClosedDay + " 收好", Gold);
            Fill(36, 373, 368, 168, Panel);
            var lines = NightOrderConversation.Receipt(selected);
            view.Label(49, 387, 340, 137, string.Join("\n\n", lines), false, false, 17);
            Small(42, 563, 350, "状态 · " + NightReceiptStatus(selected.Result));
            Small(42, 598, 350, "真实付款 " + selected.Payments.Length + " 笔 · 不另发现金");
            Btn(36, 646, 368, 35, "回到夜班求购", OpenNightOrders); return;
        }
        view!.Card(36, 187, 368, 103, NightOrderRules.ReputationName(book), "完成 " + book.TotalCompleted + " 单", "累计实收 " + Money(book.TotalActualPaid) + " · 累计交付 " + book.TotalDeliveredUnits + " 件", Accent);
        var receipts = book.History.AsEnumerable().Reverse().ToArray();
        nightHistoryPage = Math.Clamp(nightHistoryPage, 0, Math.Max(0, (receipts.Length - 1) / 3));
        for (int n = 0; n < 3; n++)
        {
            int index = nightHistoryPage * 3 + n; if (index >= receipts.Length) break;
            var receipt = receipts[index]; float y = 312 + n * 98;
            Surface(36, y, 368, 86, "night-receipt:" + receipt.Offer.OrderId, () => nightReceiptId = receipt.Offer.OrderId);
            Label(49, y + 8, 337, 27, NightOrderConversation.Name(receipt.Offer.BuyerKind) + " · " + receipt.Offer.ItemName);
            Small(49, y + 40, 337, "D" + receipt.ClosedDay + " · " + NightReceiptStatus(receipt.Result) + " · " + receipt.DeliveredUnits + "/" + receipt.Offer.RequiredUnits);
            Small(49, y + 63, 337, "实收 " + Money(receipt.ActualPaidTotal) + " · 原预算 " + receipt.Offer.ProposedTotalBudget);
        }
        if (receipts.Length == 0) Label(49, 330, 340, 128, "还没有夜班收据。\n\n接单只记需求；真正成交以后，付款与交付才会留在这里。");
        Pager(627, receipts.Length, 3, nightHistoryPage, p => nightHistoryPage = p);
        Btn(36, 678, 368, 34, "回到夜班求购", OpenNightOrders);
    }
    [HideFromIl2Cpp] private static string NightReceiptStatus(NightOrderResult result) => result switch { NightOrderResult.Completed => "全部交付", NightOrderResult.Cancelled => "余量取消", NightOrderResult.PartialClosed => "预算用尽 · 部分交付", _ => "待核对" };
}
