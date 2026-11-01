using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RightClickClose
{
    public sealed class Mod : IMod
    {
        internal static bool Enabled;
        public void Init(ModManifest manifest) => Shortcuts.Init();
        public void OnEnable()
        {
            Enabled = true;
            ModCompatibility.Enable();
            Debug.Log("[RightClickClose] Mouse shortcuts enabled v1.6.10");
        }
        public void OnDisable()
        {
            Enabled = false;
            ModCompatibility.Disable();
            RightClick.Reset();
            ShopShortcuts.Reset();
            ShopFlow.Reset();
            RecentContainers.Reset();
            DragAutoRotate.Reset();
        }
    }

    internal static class RightClick
    {
        private static readonly List<RaycastResult> Hits = new List<RaycastResult>();
        private static readonly System.Reflection.FieldInfo WindowBackground = AccessTools.Field(typeof(PixelWindow), "background");
        private static int polledFrame = -1;
        internal static bool Consumed => Shortcuts.ConsumedKeys.Contains(KeyCode.Mouse1);
        internal static bool MiddleConsumed => Shortcuts.ConsumedKeys.Contains(KeyCode.Mouse2);
        internal static bool ScrollConsumed;
        private static float lastScrollTime = float.NegativeInfinity;

        internal static void Reset()
        {
            polledFrame = -1;
            Shortcuts.ConsumedKeys.Clear();
            ScrollConsumed = false;
            lastScrollTime = float.NegativeInfinity;
            Hits.Clear();
        }

        // Both input systems call this before dispatch; only the first call may close a window.
        internal static void Poll()
        {
            if (polledFrame == Time.frameCount) return;
            polledFrame = Time.frameCount;
            ScrollConsumed = false;
            Shortcuts.BeginFrame();
            float scroll = Input.mouseScrollDelta.y;
            bool newScrollGesture = scroll != 0 && Time.unscaledTime - lastScrollTime > 0.3f;
            if (scroll != 0) lastScrollTime = Time.unscaledTime;
            if (!Mod.Enabled || (!Input.anyKeyDown && scroll == 0)) return;
            if (scroll != 0 && Shortcuts.Match(Shortcuts.PanelScroll, out _)) return;
            if (EventSystem.current == null || KeyBindHelper.SuppressHotkeys || StoreUIManager.IsAnyTyping() ||
                ItemPinHelper.BlocksClicks || MachineStorageCoordinator.IsBusy || HopperHelper.IsBusy ||
                Input.GetMouseButton(0) || UIDragHandler.isDraggingAnyUI ||
                (ItemMultiSelectHandler.current != null && ItemMultiSelectHandler.current.IsGroupDragging) ||
                (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem))
                return;

            var pointer = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
            Hits.Clear();
            EventSystem.current.RaycastAll(pointer, Hits);
            // Never search through an unrelated foreground UI to close a window behind it.
            var hit = Hits.Count > 0 && Hits[0].gameObject != null ? Hits[0].gameObject.transform : null;
            if (!ItemShortcuts.Handle(hit) && !ShopShortcuts.Handle(hit, newScrollGesture) &&
                Shortcuts.Match(Shortcuts.CloseWindow, out var close) && CloseTarget(hit))
            {
                close.Consume();
            }
            Hits.Clear();
            if (Shortcuts.ConsumedKeys.Count > 0 || ScrollConsumed) CustomEventHandler.clearDoubleClickOnNextFrame = true;
        }

        // Preserve item menus, buttons, inputs and other actionable controls before reaching the window.
        internal static bool IsControl(Transform current, TreeNodeRender node)
        {
            var selectable = current.GetComponent<Selectable>();
            if (selectable != null && selectable.IsActive() && selectable.IsInteractable()) return true;
            if (current.GetComponent<Interactable>() != null || current.GetComponent<InteractableUI>() != null)
                return true;
            if (node == null) return false;
            if (node.element is SelectableElement control && !control.isDisabled && !control.isHidden) return true;
            return node.window == null && !(node.element is GameInventory) && node.eventHandler != null &&
                (node.eventHandler.mouseClickFunc.Count > 0 || node.eventHandler.mouseUpFunc.Count > 0);
        }

        private static bool CloseTarget(Transform hit)
        {
            for (var current = hit; current != null; current = current.parent)
            {
                var node = current.GetComponent<TreeNodeRender>();
                if (node != null && node.FindParentElement() is GameItem) return false;
                // Empty inventory cells have native context actions; preserve them even when sorting is currently unavailable.
                if (node != null && node.FindParentElement() is GameInventory) return false;
                if (node != null && node.window == null && node.eventHandler != null &&
                    (node.eventHandler.mouseClickFunc.Count > 0 ||
                     (node.eventHandler.mouseDownFunc.Count > 0 && !IsWindowBackground(node)) ||
                     node.eventHandler.mouseUpFunc.Count > 0)) return false;
                if (IsControl(current, node)) return false;
                if (node != null && node.window != null)
                {
                    var window = node.window;
                    if (!window.IsVisible() || (!window.canMove && window.closeCallback == null))
                        return false;
                    WindowsHandler.CloseWindow(window);
                    return true;
                }

                var paper = current.GetComponent<PaperUIPanel>();
                if (paper != null && paper.owner != null)
                {
                    paper.owner.Hide();
                    return true;
                }

                var manager = CustomUIManager.Instance;
                const string prefix = "custom_window_";
                if (manager != null && current.name.StartsWith(prefix))
                {
                    var window = manager.GetWindow(current.name.Substring(prefix.Length));
                    if (window != null && window.root == current.gameObject)
                    {
                        // Persistent native tablets must use their own close handlers.
                        switch (window.id)
                        {
                            case GameOptionUIManager.WINDOW_ID:
                                GameOptionUIManager.Instance.CloseUI(); break;
                            case EndOfDayUIManager.WINDOW_ID:
                                EndOfDayUIManager.Instance.CloseUI(); break;
                            case BargainUIManager.WINDOW_ID:
                                BargainUIManager.Instance.CloseUI(); break;
                            default:
                                if (!window.closeOnEscape && (window.frame == null ||
                                    window.frame.closeButton == null ||
                                    !window.frame.closeButton.gameObject.activeInHierarchy ||
                                    !window.frame.closeButton.IsInteractable())) return false;
                                window.Close(); break;
                        }
                        return true;
                    }
                }

                var store = StoreUIManager.Instance;
                if (store != null && store.escapeUIManager != null &&
                    current.gameObject == store.escapeUIManager.menuPanel)
                {
                    store.escapeUIManager.CloseEscapeMenu();
                    return true;
                }
                if (NegociationUIManager.Instance != null && current.gameObject == NegociationUIManager.Instance.uiObject)
                {
                    NegociationUIManager.Instance.CloseUI();
                    return true;
                }
                if (PhoneUIManager.Instance != null && current.gameObject == PhoneUIManager.Instance.phonePanel)
                {
                    PhoneUIManager.Instance.CloseUI();
                    return true;
                }
                if (EndOfDayUIManager.Instance != null && current.gameObject == EndOfDayUIManager.Instance.UI)
                {
                    EndOfDayUIManager.Instance.CloseUI();
                    return true;
                }
                if (BargainUIManager.Instance != null && current.gameObject == BargainUIManager.Instance.uiObject)
                {
                    BargainUIManager.Instance.CloseUI();
                    return true;
                }
                var ui = current.GetComponent<StoreUI>();
                if (ui != null && ui.isPaperUI)
                {
                    // Same route as the game's Esc handler; OnDisable performs native cleanup.
                    current.gameObject.SetActive(false);
                    return true;
                }
            }
            return false;
        }

        private static bool IsWindowBackground(TreeNodeRender node)
        {
            // The native background's mouse-down only raises the window; its blank header may close.
            var window = node.FindParentWindow();
            return window != null && ReferenceEquals(WindowBackground.GetValue(window), node);
        }
    }

    [HarmonyPatch(typeof(InputActionManager), "Update")]
    internal static class BeforeGameInput
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix() => RightClick.Poll();
    }

    [HarmonyPatch(typeof(StandaloneInputModule), "ProcessMouseEvent", typeof(int))]
    internal static class BeforeUIInput
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix() => RightClick.Poll();
    }

    [HarmonyPatch(typeof(StandaloneInputModule), "ProcessMousePress")]
    internal static class ConsumeUIClick
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(PointerInputModule.MouseButtonEventData data)
        {
            if (!(RightClick.Consumed && data.buttonData.button == PointerEventData.InputButton.Right) &&
                !(RightClick.MiddleConsumed && data.buttonData.button == PointerEventData.InputButton.Middle))
                return true;
            data.buttonData.eligibleForClick = false;
            data.buttonData.pointerPress = null;
            data.buttonData.rawPointerPress = null;
            data.buttonData.pointerDrag = null;
            data.buttonData.dragging = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(InputActionManager), "UpdateKeys")]
    internal static class ConsumeGameClick
    {
        [HarmonyPostfix]
        private static void Postfix(ISet<KeyCode> ___tempKeysPressed, ISet<KeyCode> ___tempKeysReleased)
        {
            foreach (var key in Shortcuts.ConsumedKeys)
            {
                ___tempKeysPressed.Remove(key);
                ___tempKeysReleased.Remove(key);
            }
        }
    }

    [HarmonyPatch(typeof(CustomEventHandler), "Update")]
    internal static class ConsumeDoubleClick
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            RightClick.Poll();
            return !RightClick.Consumed && !RightClick.MiddleConsumed;
        }
    }

    [HarmonyPatch(typeof(KeyBindHelper), "GetKeyDown")]
    internal static class ConsumeBoundKey
    {
        private static void Postfix(string id, ref bool __result)
        {
            RightClick.Poll();
            if (Shortcuts.ConsumedKeys.Contains(KeyBindHelper.Get(id))) __result = false;
        }
    }
}
