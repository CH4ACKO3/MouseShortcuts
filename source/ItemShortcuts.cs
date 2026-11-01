using HarmonyLib;
using UnityEngine;

namespace RightClickClose
{
    internal static class ItemShortcuts
    {
        internal static bool Handle(Transform hit)
        {
            var store = StoreUIManager.Instance;
            var entry = EmporiumEntry.Instance;
            if (store == null || entry == null || MapUIManager.Instance == null || MapUIManager.Instance.isOutside ||
                store.isShootingMode || store.isMagnifyingMode || NegociationUIManager.IsTradeLocked() ||
                ItemMouseDragHandler.current == null || !ItemMouseDragHandler.current.enabled)
                return false;

            GameItemElement item = null;
            for (var current = hit; current != null; current = current.parent)
            {
                var node = current.GetComponent<TreeNodeRender>();
                if (RightClick.IsControl(current, node)) return false;
                if (node == null) continue;
                // Resolve only the foreground element; never reach through a panel to an item behind it.
                item = node.FindParentElement() as GameItemElement;
                break;
            }
            if (item == null) return false;
            if (item.IsDestroyed() || GeneralHelper.IsDisplayOnly(item)) return true;

            if (Shortcuts.Match(Shortcuts.Pin, out var pin))
            {
                pin.Consume();
                if (ItemPinHelper.MayPin(item)) ItemPinHelper.TogglePinned(item);
            }
            else
            {
                bool forward = Shortcuts.Match(Shortcuts.ItemForward, out var gesture);
                if (forward || Shortcuts.Match(Shortcuts.ItemBack, out gesture))
                {
                    gesture.Consume();
                    Move(item, forward);
                }
            }
            return true;
        }

        private static void Move(GameItemElement item, bool forward)
        {
            if (ItemPinHelper.IsPinned(item) || !item.MayRemove()) return;
            var entry = EmporiumEntry.Instance;
            bool owned = GeneralHelper.IsItemOwned(item);
            if (owned && forward && RecentContainers.TryStore(item)) return;
            var destination = ItemRoutes.Resolve(item, forward);
            if (destination == null) return;
            var client = PlayerStore.Instance?.currentClientInstance?.GetClientBlueprint();
            if (!owned)
            {
                if (client == null || client.isBusinessComplete ||
                    (item.parentInventory != entry.frontInvinvElement && item.parentInventory != entry.backInvinvElement)) return;
                // Barter rewards stay on the customer's side: native offers may inspect every back-counter item.
                if (client.clientIntent == StoreClient.ClientIntent.BARTER)
                {
                    if (destination == entry.backInvinvElement) NegociationUIManager.Instance.OpenUI();
                    return;
                }
                if (client.clientIntent != StoreClient.ClientIntent.SELL &&
                    client.clientIntent != StoreClient.ClientIntent.SELLNBUY) return;
                // Selecting a destination never grants ownership or bypasses the purchase step.
                if (destination != entry.backInvinvElement && destination != entry.frontInvinvElement) return;
            }
            if (!GeneralHelper.MayPlayerInsertInto(destination)) return;
            if (item.parentInventory == destination)
            {
                // Re-select the buying/selling side for mixed customers without moving the item again.
                if (destination == entry.backInvinvElement)
                {
                    NegociationUIManager.Instance.UpdateUI(item);
                    NegociationUIManager.Instance.OpenUI();
                }
                return;
            }
            var slot = destination.TryFindOneValidInventorySlot(item);
            if (slot == null || !slot.IsValid() || slot.numTransfer <= 0) return;
            // Native transfer performs capacity/ownership checks and the counter's offer callbacks.
            if (slot.TryAcceptOnce(item.unitCount) > 0)
                AudioManager.Instance.TryPlayOne(item.soundDragStart, item.soundDragEnd);
        }
    }

    [HarmonyPatch(typeof(TreeNodeRender), "OnScroll")]
    internal static class ConsumeInventoryScroll
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix() => !RightClick.ScrollConsumed;
    }

    [HarmonyPatch(typeof(InputActionHandler), "UpdateMouseScroll")]
    internal static class ConsumeGameScroll
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix() => !RightClick.ScrollConsumed;
    }
}
