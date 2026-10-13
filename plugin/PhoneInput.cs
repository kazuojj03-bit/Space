using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SpacePhone;

// The native game has three independent mouse routes: uGUI, pixel-window
// callbacks and InputActionManager. Protect all three; stopping CheckRaycast
// alone leaves the previous inventory target eligible for the next click.
internal static class PhoneInput
{
    private static int releaseFrame = -1;
    private static float releaseTime = -1;
    private static bool waitingForRelease;
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);

    internal static bool IsBlocked
    {
        get
        {
            if (PhoneBehaviour.Live?.IsOpen == true) return true;
            if (!waitingForRelease) return false;
            // A held closing click is never allowed to become a shop click.
            // Two frames also consume the key-up and delayed uGUI callback.
            if (ButtonsHeld())
            {
                releaseFrame = Math.Max(releaseFrame, Time.frameCount + 2);
                return true;
            }
            if (Time.frameCount <= releaseFrame || Time.unscaledTime < releaseTime) return true;
            waitingForRelease = false;
            return false;
        }
    }

    internal static void Begin()
    {
        waitingForRelease = false;
        releaseFrame = -1;
        releaseTime = -1;
    }

    internal static void End()
    {
        waitingForRelease = true;
        releaseFrame = Time.frameCount + 2;
        releaseTime = Time.unscaledTime + .10f;
    }

    internal static void Clear()
    {
        waitingForRelease = false;
        releaseFrame = -1;
        releaseTime = -1;
    }

    private static bool ButtonsHeld()
    {
        if (OperatingSystem.IsWindows())
            return (GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0 || (GetAsyncKeyState(4) & 0x8000) != 0;
        return Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
    }
}

public sealed partial class PhoneBehaviour
{
    private GameObject? inputShield;

    [HideFromIl2Cpp]
    internal bool OwnsPointerSurface(GameObject? target)
    {
        if (target == null) return false;
        if (view?.OwnsSurface(target) == true) return true;
        return inputShield != null && (target.Pointer == inputShield.Pointer ||
            target.transform.IsChildOf(inputShield.transform));
    }

    [HideFromIl2Cpp]
    internal void FollowPhoneNativeFloatingLayer() => view?.FollowNativeFloatingLayer();

    [HideFromIl2Cpp]
    private void AcquirePhoneInput()
    {
        PhoneInput.Begin();
        if (!mouseLocked && StoreUIManager.Instance != null)
        {
            lockedUI = StoreUIManager.Instance;
            lockedUI.IncrementMouseFlag();
            mouseLocked = true;
        }
        // Abort an unfinished drag and discard its cached selection before the
        // phone becomes interactive. Native window contents are left intact.
        EventSystem.current?.SetSelectedGameObject(null);
        InputActionManager.current?.Reset();
        foreach (var handler in Object.FindObjectsOfType<CustomEventHandler>())
            PhoneCustomInputRoute.ClearTarget(handler);
        var hovering = Interactable.currentHoveringInteractable;
        if (hovering != null) hovering.CancelHold();
        UIDragHandler.ReleaseDragLatch();
        EnsurePhoneInputShield();
        inputShield!.SetActive(true);
    }

    [HideFromIl2Cpp]
    private void ReleasePhoneInput(bool wasVisible)
    {
        if (wasVisible) PhoneInput.End();
        // Close() releases exactly the mouse flag acquired above. The shield
        // and the prefixes retain only the closing click until it is released.
        if (inputShield != null) inputShield.SetActive(PhoneInput.IsBlocked);
    }

    [HideFromIl2Cpp]
    private void TickPhoneInput()
    {
        if(visible)FollowPhoneNativeFloatingLayer();
        bool blocked = PhoneInput.IsBlocked;
        if (inputShield != null && inputShield.activeSelf != blocked) inputShield.SetActive(blocked);
    }

    [HideFromIl2Cpp]
    private void DisposePhoneInput()
    {
        PhoneInput.Clear();
        if (inputShield != null) Object.Destroy(inputShield);
        inputShield = null;
    }

    [HideFromIl2Cpp]
    private void EnsurePhoneInputShield()
    {
        if (inputShield != null) return;
        inputShield = new GameObject("SpacePhone modal input shield");
        inputShield.layer = 5;
        inputShield.transform.SetParent(transform, false);
        var canvas = inputShield.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 31999; // PhoneView is 32000, above this shield.
        inputShield.AddComponent<GraphicRaycaster>();
        var surface = new GameObject("SpacePhone pointer blocker");
        surface.layer = 5;
        var rect = surface.AddComponent<RectTransform>();
        rect.SetParent(inputShield.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = surface.AddComponent<Image>();
        image.color = new Color(0, 0, 0, 0);
        image.raycastTarget = true;
        // uGUI excludes graphics whose CanvasRenderer is culled. A fully
        // transparent blocker must keep its geometry eligible for raycasts.
        image.canvasRenderer.cullTransparentMesh = false;
        image.SetAllDirty();
        Canvas.ForceUpdateCanvases();
    }
}

[HarmonyPatch]
internal static class PhoneNativeFloatingLayer
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(LetterboxManager), nameof(LetterboxManager.ConvertCanvases));
        yield return AccessTools.Method(typeof(LetterboxManager), nameof(LetterboxManager.Apply));
        yield return AccessTools.Method(typeof(LetterboxManager), nameof(LetterboxManager.LateUpdate));
    }
    private static void Postfix() => PhoneBehaviour.Live?.FollowPhoneNativeFloatingLayer();
}

// Letterbox uses several UI cameras. Camera depth is sorted before a canvas's
// sortingOrder, so an original calendar can otherwise sit ahead of both the
// phone and its shield in the actual EventSystem pointer target list.
[HarmonyPatch(typeof(EventSystem), nameof(EventSystem.RaycastAll))]
internal static class PhoneOwnsPointerTargets
{
    private static void Postfix(Il2CppSystem.Collections.Generic.List<RaycastResult> __1)
    {
        if (!PhoneInput.IsBlocked) return;
        var phone = PhoneBehaviour.Live;
        for (int i = __1.Count - 1; i >= 0; i--)
            if (phone == null || !phone.OwnsPointerSurface(__1[i].gameObject)) __1.RemoveAt(i);
    }
}

[HarmonyPatch(typeof(InputActionManager), nameof(InputActionManager.Update))]
internal static class PhoneNativeInputRoute
{
    private static bool Prefix() => !PhoneInput.IsBlocked;
}

[HarmonyPatch]
internal static class PhoneNativeMouseEdges
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(InputActionManager), nameof(InputActionManager.GetMouseKeyDown));
        yield return AccessTools.Method(typeof(InputActionManager), nameof(InputActionManager.GetMouseKeyUp));
    }
    private static bool Prefix(ref bool __result)
    {
        if (!PhoneInput.IsBlocked) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(CustomEventHandler), nameof(CustomEventHandler.Update))]
internal static class PhoneCustomInputRoute
{
    private static bool Prefix(CustomEventHandler __instance)
    {
        if (!PhoneInput.IsBlocked) return true;
        ClearTarget(__instance);
        return false;
    }
    internal static void ClearTarget(CustomEventHandler handler)
    {
        handler.hasRayCast = false;
        handler.rayCastResult = null;
        handler.lastHandlerClicked = null;
        CustomEventHandler.clearDoubleClickOnNextFrame = true;
    }
}

[HarmonyPatch]
internal static class PhonePixelPointerCallbacks
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string name in new[] { "OnPointerClick", "OnPointerDown", "OnPointerUp", "OnBeginDrag", "OnDrag", "OnEndDrag", "OnDrop", "OnScroll", "OnPointerDoubleClick", "OnUpdateSelected" })
            yield return AccessTools.Method(typeof(TreeNodeRender), name);
        foreach (string name in new[] { "OnPointerClick", "OnPointerDown", "OnPointerUp", "HandleClick", "OnClick", "OnDoubleClick", "UpdateHold" })
            yield return AccessTools.Method(typeof(Interactable), name);
        foreach (string name in new[] { "OnBeginDrag", "OnDrag", "OnEndDrag", "OnPointerDown" })
            yield return AccessTools.Method(typeof(UIDragHandler), name);
        yield return AccessTools.Method(typeof(InteractableUI), "UnityEngine_EventSystems_IPointerClickHandler_OnPointerClick");
        yield return AccessTools.Method(typeof(StoreUIManager), nameof(StoreUIManager.CheckRaycast));
        yield return AccessTools.Method(typeof(StoreUIManager), nameof(StoreUIManager.TryTrashHoveredItem));
        yield return AccessTools.Method(typeof(StoreUIManager), nameof(StoreUIManager.Shoot));
        yield return AccessTools.Method(typeof(ItemMouseDoubleClickHandler), nameof(ItemMouseDoubleClickHandler.OpenContentAction));
        yield return AccessTools.Method(typeof(ItemMouseDoubleClickHandler), nameof(ItemMouseDoubleClickHandler.OpenExamineAction));
        // StorePhoneObject is handled by PhoneClickPatch; it must check this
        // barrier before calling Toggle to avoid closing through the handset.
    }
    private static bool Prefix() => !PhoneInput.IsBlocked;
}

[HarmonyPatch(typeof(StoreUIManager), nameof(StoreUIManager.ResolveEscape))]
internal static class PhoneOwnsEscape
{
    private static bool Prefix(ref bool __result)
    {
        if (!PhoneInput.IsBlocked) return true;
        __result = true;
        return false;
    }
}
