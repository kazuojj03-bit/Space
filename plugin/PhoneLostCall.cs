using Il2CppInterop.Runtime.Attributes;
namespace SpacePhone;

public sealed partial class PhoneBehaviour
{
    [HideFromIl2Cpp] internal bool OwnsLostCallBook(LostCallBook book) => OwnsPhoneRun() && ReferenceEquals(state!.LostCall, book);
    [HideFromIl2Cpp] private bool LostCallHasPending() => state != null && LostCallClients.HasPending(state.LostCall);
    [HideFromIl2Cpp] private bool LostCallReady() => LostCallClients.TryEnsureContentReady();
    [HideFromIl2Cpp] private bool PreferLostCall() => state != null && !state.LostCall.Disabled &&
        (LostCallHasPending() || state.LostCall.Phase is LostCallPhase.AwaitingIntroduction or LostCallPhase.AwaitingAcquisition or LostCallPhase.Acquired or LostCallPhase.Delivered);
    [HideFromIl2Cpp] private void OpenLostCall() => Navigate("失物来电");
    [HideFromIl2Cpp] private void TickLostCall()
    {
        if (!OwnsPhoneRun()) return;
        bool maySchedule = pendingAction == null && confirmAction == null && !NativePhoneBusy();
        lostCallClients.Tick(Store, state!.LostCall, Day, OnLostCallChoice, WriteState, Close, maySchedule);
        bool noOlderReservation = !state.Stories.Threads.Any(t => t.QueuedVisitScene >= 0) &&
            !state.Commissions.Entries.Any(c => c.QueuedStage >= 0);
        int trades = (int)Math.Min(int.MaxValue, (long)B.PurchaseCount + B.SaleCount);
        bool changed = LostCallRules.Offer(state.LostCall, Day, trades, noOlderReservation && LostCallReady()).Changed;
        changed |= FlushLostCallResearch();
        if (changed) { WriteState(); nextDraw = 0; }
    }
    [HideFromIl2Cpp] private bool FlushLostCallResearch()
    {
        if (state == null || LostCallRules.PendingResearchReward(state.LostCall) is not { } pending) return false;
        int pointsBefore = B.ResearchPoints, storedBefore = B.StoredResearchPoints;
        int accepted = BusinessRules.StoreResearchReward(B, pending.Points);
        if (accepted != 1) return false;
        if (LostCallRules.MarkResearchStored(state.LostCall, pending.ReceiptId, accepted).Changed) return true;
        B.ResearchPoints = pointsBefore; B.StoredResearchPoints = storedBefore;
        return false;
    }
    [HideFromIl2Cpp] private void OnLostCallChoice(int key)
    {
        if (!OwnsPhoneRun()) return;
        var book = state!.LostCall;
        if (book.LastVisitFinished || book.LastVisitDay != Day) return;
        LostCallReply reply = book.LastVisitKind switch
        {
            LostCallVisitKind.Introduction => LostCallRules.ReplyIntroduction(book, key, Day, book.LastVisitToken),
            LostCallVisitKind.Debrief => LostCallRules.ReplyDebrief(book, key, Day, book.LastVisitToken),
            _ => new(false, "这次来访没有待确认的选择。")
        };
        message = reply.Message;
        if (reply.Changed) { FlushLostCallResearch(); WriteState(); nextDraw = 0; }
    }
    [HideFromIl2Cpp] private void InviteLostCall()
    {
        if (!OwnsPhoneRun()) return;
        if (NightOrderHasPending()) { message = "夜班买家已有安排，先完成或取消它再约余灯。"; return; }
        if (state!.PendingSupplier >= 0 || state.PendingRecommendation || state.Stories.Threads.Any(t => t.QueuedVisitScene >= 0) ||
            state.Commissions.Entries.Any(c => c.QueuedStage >= 0))
        { message = "已有到访预约，先完成或取消它，再约余灯。"; return; }
        if (!LostCallReady()) { message = "新来电暂待同步，当前生意照常。"; return; }
        message = LostCallClients.QueueVisit(state.LostCall, Day);
        WriteState(); nextDraw = 0;
        if (LostCallHasPending()) Close();
    }
    [HideFromIl2Cpp] private void SelectLostCallEntry(int key)
    {
        if (!OwnsPhoneRun()) return;
        var reply = LostCallRules.ReplyEntry(state!.LostCall, key, Day);
        message = reply.Message;
        if (reply.Changed) { WriteState(); nextDraw = 0; }
        if (key == LostCallRules.InviteChoice && reply.Changed) InviteLostCall();
        if (key == LostCallRules.LaterChoice) Close();
    }
    [HideFromIl2Cpp] private void ChooseLostCallRoute(int key)
    {
        if (!OwnsPhoneRun()) return;
        if (LostCallHasPending()) { message = "本次收货安排已发出；先取消预约，或等余灯正常离店后再改去向。"; return; }
        var reply = LostCallRules.SelectRoute(state!.LostCall, key);
        message = reply.Message;
        if (reply.Changed) { WriteState(); nextDraw = 0; }
    }
    [HideFromIl2Cpp] private LostCallGuide CurrentLostCallGuide()
    {
        var book = state!.LostCall;
        bool owned = book.AcquiredActualUniqueId > 0 && LostCallClients.OwnsItem(book.AcquiredActualUniqueId);
        return LostCallRules.GetGuide(book, Day, LostCallReady(), owned);
    }
    [HideFromIl2Cpp] private void DrawLostCallListCard(float y)
    {
        var book = state!.LostCall; var guide = LostCallRules.GetGuide(book, Day);
        Surface(36, y, 368, 96, "task:lostcall", OpenLostCall);
        Fill(36, y, 3, 96, book.Phase == LostCallPhase.Offered ? Gold : Accent);
        Label(50, y + 8, 332, 29, "余灯 · 失物来电", true);
        Small(50, y + 41, 332, guide.Step);
        Small(50, y + 66, 332, book.Disabled ? "来电记录待核对 · 当前生意照常" : book.Phase == LostCallPhase.Locked ? "原创实物与三种去向 · D2起留意来电" : guide.Progress);
    }
    [HideFromIl2Cpp] private void DrawLostCall()
    {
        var book = state!.LostCall; var guide = CurrentLostCallGuide();
        Label(42, 187, 240, 36, "余灯 · 夜班维修员", true);
        Btn(288, 187, 116, 34, "伙伴列表", () => { taskListMode = true; Navigate("任务"); });
        if (book.Phase == LostCallPhase.Offered)
        {
            view!.Card(36, 235, 368, 105, "陌生来电", "先问来历，再谈买卖", "没有期限 · 不打断柜台", Gold);
            Fill(36, 355, 368, 144, Panel);
            view.Label(50, 367, 340, 120, "这里是余灯。号码没打错，线路倒有点杂音。\n\n我有一只航线录音匣，也有处置单。你若想知道它值多少，最好当面聊。", false, false, 17);
            var choices = LostCallRules.EntryChoices(book);
            for (int i = 0; i < choices.Length; i++)
            { int key = choices[i].Key; var expectedBook = book; Btn(36, 518 + i * 51, 368, 41, choices[i].Label, () => { if (OwnsLostCallBook(expectedBook)) SelectLostCallEntry(key); }); }
            Small(42, 680, 350, "约见不扣款；名片号码仍在原版拨号中。"); return;
        }
        if (book.Phase == LostCallPhase.Acquired && book.LastVisitFinished && !LostCallHasPending() && LostCallReady() && LostCallClients.OwnsItem(book.AcquiredActualUniqueId))
        { DrawLostCallRoutes(book, guide); return; }
        string progress = LostCallHasPending() ? "预约已保留 · 柜台空闲后到访" : guide.Progress;
        view!.Card(36, 235, 368, 106, "当前步骤", guide.Step, progress, Gold);
        Fill(36, 356, 368, 170, Panel); Small(49, 369, 340, "接下来这样做");
        string instruction = LostCallHasPending() ? "先接待当前顾客。余灯会沿游戏原来的来店流程到柜台；手机预约没有联络费，交易时再确认报价。" : guide.Instruction;
        if (book.Phase == LostCallPhase.AwaitingAcquisition && Store.GetCash() < LostCallRules.SuggestedPurchaseBudget)
            instruction += "\n现金较少，先留够租金和周转钱；这条来电可以一直等。";
        view.Label(49, 404, 340, 110, instruction, false, false, 17);
        Btn(36, 544, 368, 43, LostCallHasPending() ? "回柜台等候" : guide.PrimaryLabel, () => RunLostCallAction(guide));
        Btn(36, 604, 179, 36, "查看货柜", () => Navigate("货柜"));
        Btn(225, 604, 179, 36, "收付与回执", () => Navigate("失物回执"));
        if (LostCallHasPending()) Btn(36, 656, 368, 34, "取消本次预约", () => { message = LostCallClients.CancelVisit(book); WriteState(); nextDraw = 0; });
        else Small(42, 660, 350, "拒价、暂缓和改天再约都没有罚金。");
    }
    [HideFromIl2Cpp] private void DrawLostCallRoutes(LostCallBook book, LostCallGuide guide)
    {
        Small(42, 231, 350, "实际收购 " + Money(book.ActualPurchaseAmount) + " · 同一只实物，三种去向");
        var routes = LostCallRules.RouteChoices(book).Where(c => c.Key != LostCallRules.PauseRouteChoice).ToArray();
        string[] notes = { "正常议价，留下现款与收藏回执", "正常议价，留下报平安的夜航记忆", "正常议价，收尾归档一次1点研究" };
        for (int i = 0; i < routes.Length; i++)
        {
            var choice = routes[i]; int key = choice.Key; float y = 268 + i * 96;
            var expectedBook = book;
            bool selected = (int)book.ProposedRoute == key - LostCallRules.ProfitChoice;
            Surface(36, y, 368, 83, "lost-route:" + key, () => Ask("预选去向", choice.Detail + "\n\n只改变预选；库存和现金仍在，成交后才确定去向。", () => { if (OwnsLostCallBook(expectedBook)) ChooseLostCallRoute(key); }));
            Fill(36, y, 3, 83, selected ? Gold : Accent);
            Label(49, y + 8, 336, 28, (selected ? "✓ " : "") + choice.Label);
            Small(49, y + 37, 336, notes[i]); Small(49, y + 60, 336, "拟定买方预算 " + choice.ProposedBuyerBudget + " · 不承诺成交价");
        }
        Btn(36, 571, 368, 43, book.ProposedRoute == LostCallRoute.None ? "先点选一个去向" : guide.CanInvite ? "预约按预选去向收货" : guide.PrimaryLabel, () => RunLostCallAction(guide), book.ProposedRoute != LostCallRoute.None);
        Btn(36, 631, 179, 35, "核对采购与回执", () => Navigate("失物回执")); Btn(225, 631, 179, 35, "先继续营业", Close);
        Small(42, 680, 350, guide.WaitUntilDay > Day ? "最早 D" + guide.WaitUntilDay + " 再约 · 没有截止日" : "未成交离店后可改去向 · 没有截止日");
    }
    [HideFromIl2Cpp] private void RunLostCallAction(LostCallGuide guide)
    {
        if (!OwnsPhoneRun()) return;
        if (LostCallHasPending()) { Close(); return; }
        if (guide.PrimaryAction == LostCallAction.ViewReceipt) { Navigate("失物回执"); return; }
        if (guide.PrimaryAction == LostCallAction.CheckInventory) { Navigate("货柜"); return; }
        if (guide.CanInvite)
        {
            var expectedBook = state!.LostCall; var plan = LostCallRules.PlanVisit(expectedBook, Day);
            if (plan == null) { message = "来访安排已变化，请查看当前步骤。"; return; }
            string detail = plan.Value.Kind == LostCallVisitKind.Acquisition ? "邀请本身免费。余灯会带实物来，柜台确认价格后才付款。手机建议预算70，可正常议价或拒绝。" :
                plan.Value.Kind == LostCallVisitKind.Delivery ? "拟定买方预算 " + plan.Value.ProposedBuyerBudget + "。把同一只匣子放到柜台，实际议价成交后才移交；预算不等于售价。" : "邀请免费，只在柜台谈话，不移走任何货物或扣款。";
            Ask("邀请余灯", detail, () => { if (!OwnsLostCallBook(expectedBook) || LostCallRules.PlanVisit(expectedBook, Day) != plan) { message = "日期或安排已变化，请重新确认。"; return; } InviteLostCall(); }); return;
        }
        if (guide.PrimaryAction == LostCallAction.ReviewEntry || guide.PrimaryAction == LostCallAction.ChooseRoute) { nextDraw = 0; return; }
        Close();
    }
    [HideFromIl2Cpp] private void DrawLostCallReceipt()
    {
        var book = state!.LostCall;
        bool acquired = book.AcquiredActualUniqueId > 0;
        bool sold = book.ItemDisposition is LostCallDisposition.Delivered or LostCallDisposition.SoldElsewhere;
        view!.Card(36, 187, 179, 100, "真实购入支出", acquired ? Money(book.ActualPurchaseAmount) : "尚未成交", acquired ? "D" + book.PurchaseDay : "电话不产生费用", Accent);
        view.Card(225, 187, 179, 100, "真实售出收入", sold ? Money(book.ActualSaleAmount) : "尚未成交", sold ? "D" + book.DispositionDay : "物品去向未结算", Gold);
        Btn(288, 305, 116, 28, "返回指引", OpenLostCall);
        Small(42, 305, 239, sold ? "已知买卖差额 " + LostCallRules.KnownTradeDifference(book).ToString("+#,0;-#,0;0") : "暂不计算未成交利润");
        Fill(36, 344, 368, 199, Panel);
        string destination = book.ItemDisposition switch { LostCallDisposition.SoldElsewhere => "自行出售", LostCallDisposition.Destroyed => "已核对销毁", LostCallDisposition.Delivered => LostCallRules.RouteName(book.CommittedRoute), _ => "尚未移交" };
        Label(49, 357, 340, 29, "实物去向 · " + destination, true);
        string outcome = book.Phase == LostCallPhase.Delivered ? "同一只录音匣已完成柜台移交，真实收付已记下。\n\n接收回执还在路上。最早 D" + (book.DispositionDay + 1) + " 邀请余灯来讲后来发生的事；没有期限。" : LostCallConversation.OutcomeSummary(book);
        view.Label(49, 396, 340, 133, outcome, false, false, 16);
        if (book.OriginalVoiceUnlocked)
        {
            Label(49, 550, 340, 45, "夜航留言：" + LostCallConversation.OriginalPublicMessage, false);
            Btn(36, 602, 179, 35, "播放夜航信号", PlayLostCallSignal); Btn(225, 602, 179, 35, "停止信号", StopLostCallSignal);
            var memoryGuide = CurrentLostCallGuide();
            Btn(36, 650, 368, 34, memoryGuide.CanInvite ? "请余灯来聊听音台的近况" : "今天先继续营业", () => { if (memoryGuide.CanInvite) InviteLostCall(); else Close(); });
            Small(42, 691, 350, "原创信号与播报文字 · 无人声录音");
        }
        else if (book.Phase == LostCallPhase.Closed)
        {
            var guide = CurrentLostCallGuide();
            Btn(36, 569, 368, 41, guide.CanInvite ? "闲时请余灯来聊近况" : "今天先继续营业", () => { if (guide.CanInvite) InviteLostCall(); else Close(); });
            Small(42, 629, 350, book.PendingResearch == 1 ? "一页研究保留待入账，容量腾出后自动归档。" : "回执只记录一次；再见面不重复发回报。");
        }
        else Btn(36, 569, 368, 41, "回到当前步骤", OpenLostCall);
        if (!book.OriginalVoiceUnlocked) Btn(36, 674, 368, 34, "返回失物来电", OpenLostCall);
    }
}
