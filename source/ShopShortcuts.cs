using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RightClickClose
{
    internal static class ShopShortcuts
    {
        private static readonly FieldInfo PrimaryButton = AccessTools.Field(typeof(NegociationUIManager), "primaryButton");
        private static readonly FieldInfo Frame = AccessTools.Field(typeof(NegociationUIManager), "frame");
        private static readonly MethodInfo RefreshNow = AccessTools.Method(typeof(NegociationUIManager), "RefreshNow");
        private static float lastDismissTime = float.NegativeInfinity;

        internal static void Reset() => lastDismissTime = float.NegativeInfinity;

        internal static bool Handle(Transform hit, bool newScrollGesture)
        {
            var store = StoreUIManager.Instance;
            var shutter = StoreShutterButton.Instance;
            // PlayerStore.Instance lazily constructs a save; never access it from an unloaded shop/menu.
            if (store == null || shutter == null || MapUIManager.Instance == null) return false;
            var player = PlayerStore.Instance;
            if (player == null ||
                MapUIManager.Instance.isOutside || store.isShootingMode || store.isMagnifyingMode ||
                player.cinematicMode || NegociationUIManager.IsTradeLocked() || Interactable.isPaused ||
                Active(store.escapeUIManager?.menuPanel) || Active(store.closeShutterConfirmationPanel) ||
                Active(store.confirmPanelUIManager?.panel) || Active(GenericConfirmationUIManager.Instance?.panel) ||
                (store.gameOptionUIManager != null && store.gameOptionUIManager.IsOpen) ||
                (EndOfDayUIManager.Instance != null && EndOfDayUIManager.Instance.IsOpen))
                return false;

            var negotiation = NegociationUIManager.Instance;
            var frame = negotiation != null ? Frame.GetValue(negotiation) as NegotiationTabletFrame : null;
            if (frame != null && Active(frame.confirmRoot)) return false;

            // Only the foreground hit can be a customer. Any item, control or floating UI blocks the shop behind it.
            bool customer = false;
            bool shutterHit = false;
            for (var current = hit; current != null; current = current.parent)
            {
                if (current.GetComponent<TreeNodeRender>() != null) return false;
                if (current.GetComponent<StoreClientMono>() != null) { customer = true; break; }
                if (current.gameObject == shutter.shutterGameObject || current.gameObject == shutter.blockerGameObject ||
                    (shutter.blockerMaskObject != null && current.gameObject == shutter.blockerMaskObject.gameObject))
                { shutterHit = true; break; }
                if (current.GetComponent<StoreUI>() != null || current.GetComponent<PaperUIPanel>() != null ||
                    current.GetComponent<UnityEngine.UI.Graphic>() != null ||
                    RightClick.IsControl(current, null)) return false;
            }
            if (hit != null && !customer && !shutterHit) return false;

            if (!customer && !InsideShopWindow(shutter)) return false;
            if (Shortcuts.Match(Shortcuts.SkipAnimation, out var skip) && ShopFlow.TrySkipAnimation(false))
            {
                skip.Consume();
                return true;
            }
            if (Shortcuts.Match(Shortcuts.SkipCustomerAnimation, out var walk) && ShopFlow.TrySkipAnimation(true))
            {
                walk.Consume();
                return true;
            }
            if (store.isAboutToStartMainDialog || ItemMouseDragHandler.current == null || !ItemMouseDragHandler.current.enabled)
                return false;
            bool advance = Shortcuts.Match(Shortcuts.CustomerNext, out var next);
            bool trade = Shortcuts.Match(Shortcuts.CustomerTrade, out var confirm);
            if (player.currentClientInstance != null && (advance || trade))
            {
                var input = advance ? next : confirm;
                input.Consume();
                bool forceDialogue = advance && next.Wheel && Shortcuts.ForceDialogue.Value;
                if ((input.Wheel && !newScrollGesture && !forceDialogue) || !player.isClientArrived ||
                    (StoreClientMono.Instance != null && StoreClientMono.Instance.IsMoving())) return true;
                var dialog = DialogUIManager.Instance;
                if (dialog != null && (dialog.GetCurrentDialog() != null || dialog.IsChoice()))
                {
                    // Never pick a dialogue choice on the player's behalf.
                    if (advance && !dialog.IsChoice())
                    {
                        if (forceDialogue) ShopFlow.Advance(dialog);
                        else
                        {
                            TutorialUIManager.Instance?.DismissAdvanceDialogHint();
                            dialog.OnAdvancePressed();
                        }
                    }
                }
                else OpenTrade(negotiation, trade);
                return true;
            }

            ConfigEntry<string> action;
            if (player.storeState == PlayerStore.StoreState.MORNING) action = Shortcuts.Open;
            else if (player.storeState != PlayerStore.StoreState.OPEN) return false;
            else if (player.currentClientInstance != null) action = Shortcuts.Dismiss;
            else if (player.storeClientManager.clientStack.Count > 0) action = Shortcuts.Call;
            else action = Shortcuts.Close;
            if (!Shortcuts.Match(action, out var gesture)) return false;
            gesture.Consume();
            // Only successful dismissals start the cooldown; other shop actions and animation skips remain free.
            bool dismiss = action == Shortcuts.Dismiss;
            if ((dismiss && Time.unscaledTime - lastDismissTime < Shortcuts.DismissInterval.Value) ||
                player.lockShutterButtonFlag > 0 || player.lockNextBellFlag > 0 ||
                (StoreClientMono.Instance != null && StoreClientMono.Instance.IsMoving())) return true;
            var previousClient = player.currentClientInstance;
            if (action == Shortcuts.Open)
            {
                if (player.storeState == PlayerStore.StoreState.MORNING && player.CanStartDay()) shutter.OnClick();
            }
            else if (player.storeState == PlayerStore.StoreState.OPEN)
            {
                if (player.currentClientInstance != null)
                {
                    if (player.isClientArrived && player.IsClientDimissable()) RingBell();
                }
                else if (player.storeClientManager.clientStack.Count > 0) RingBell();
                else if (player.CanEndDay()) shutter.OnClick();
            }
            if (dismiss && player.currentClientInstance != previousClient)
                lastDismissTime = Time.unscaledTime;
            return true;
        }

        private static bool Active(GameObject obj) => obj != null && obj.activeInHierarchy;

        private static void RingBell()
        {
            var bell = Object.FindObjectOfType<StoreBell>();
            if (bell != null) bell.RingBell();
        }

        private static void OpenTrade(NegociationUIManager negotiation, bool confirm)
        {
            var client = PlayerStore.Instance.currentClientInstance.GetClientBlueprint();
            if (negotiation == null || client.isBusinessComplete) return;
            // Match the current native primary action, including mixed buyers/sellers and whole barter offers.
            if (client.clientIntent != StoreClient.ClientIntent.BUY && client.clientIntent != StoreClient.ClientIntent.SELL &&
                client.clientIntent != StoreClient.ClientIntent.SELLNBUY && client.clientIntent != StoreClient.ClientIntent.BARTER)
                return;
            negotiation.OpenUI();
            if (!confirm) return;
            RefreshNow.Invoke(negotiation, null);
            var primary = PrimaryButton.GetValue(negotiation) as TabletButton;
            if (primary != null && primary.Interactable && primary.button != null &&
                primary.button.IsActive() && primary.button.IsInteractable()) primary.button.onClick.Invoke();
        }

        private static bool InsideShopWindow(StoreShutterButton shutter)
        {
            // The native shutter mask stays over the serving window even when its moving child rolls up.
            var mask = shutter.blockerMaskObject;
            if (mask == null) return false;
            var canvas = mask.GetComponentInParent<Canvas>(true);
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(mask.rectTransform, Input.mousePosition, camera);
        }
    }
}
