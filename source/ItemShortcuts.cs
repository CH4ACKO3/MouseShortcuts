using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RightClickClose
{
    internal static class ItemShortcuts
    {
        private static readonly AccessTools.FieldRef<ItemMultiSelectHandler, List<GameItemElement>> SelectedItems =
            AccessTools.FieldRefAccess<ItemMultiSelectHandler, List<GameItemElement>>("selectedItems");
        private static readonly Action<ItemMultiSelectHandler> PruneSelection =
            AccessTools.MethodDelegate<Action<ItemMultiSelectHandler>>(AccessTools.Method(typeof(ItemMultiSelectHandler), "PruneSelection"));
        private static readonly Action<ItemMultiSelectHandler> RefreshSelection =
            AccessTools.MethodDelegate<Action<ItemMultiSelectHandler>>(AccessTools.Method(typeof(ItemMultiSelectHandler), "RefreshSelectionHighlight"));

        internal static bool Handle(Transform hit)
        {
            var store = StoreUIManager.Instance;
            var entry = EmporiumEntry.Instance;
            if (store == null || entry == null || MapUIManager.Instance == null ||
                store.isShootingMode || store.isMagnifyingMode || NegociationUIManager.IsTradeLocked() ||
                ItemMouseDragHandler.current == null || !ItemMouseDragHandler.current.enabled)
                return false;

            GameItemElement item = null;
            GameInventory inventory = null;
            for (var current = hit; current != null; current = current.parent)
            {
                var node = current.GetComponent<TreeNodeRender>();
                if (RightClick.IsControl(current, node)) return false;
                if (node == null) continue;
                // Resolve only the foreground element; never reach through a panel to an item behind it.
                var element = node.FindParentElement();
                item = element as GameItemElement;
                inventory = element as GameInventory;
                break;
            }
            if (item == null && inventory == null) return false;
            if (item != null && !IsLive(item)) return true;

            bool pin = Shortcuts.Match(Shortcuts.Pin, out var gesture);
            bool forward = false;
            if (!pin)
            {
                forward = Shortcuts.Match(Shortcuts.ItemForward, out gesture);
                if (!forward && !Shortcuts.Match(Shortcuts.ItemBack, out gesture)) return item != null;
            }
            var selection = ItemMultiSelectHandler.current;
            var targets = GetTargets(selection, item, inventory);
            if (targets.Count == 0) return item != null;
            try
            {
                if (pin)
                {
                    gesture.Consume();
                    bool pinned = targets.Exists(target => ItemPinHelper.MayPin(target) && !ItemPinHelper.IsPinned(target));
                    foreach (var target in targets)
                        if (IsLive(target) && ItemPinHelper.MayPin(target)) ItemPinHelper.SetPinned(target, pinned);
                }
                else
                {
                    var client = PlayerStore.Instance?.currentClientInstance;
                    var merchant = ItemRoutes.Merchant;
                    var location = MapUIManager.Instance.Current;
                    var moves = targets.ConvertAll(target => (Item: target, Home: target.parentInventory, Amount: target.unitCount));
                    bool handled = false;
                    foreach (var move in moves)
                    {
                        if (PlayerStore.Instance?.currentClientInstance != client || ItemRoutes.Merchant != merchant ||
                            MapUIManager.Instance.Current != location || NegociationUIManager.IsTradeLocked()) break;
                        if (IsLive(move.Item) && move.Item.parentInventory == move.Home)
                            handled |= Move(move.Item, forward, move.Amount);
                    }
                    if (handled) gesture.Consume();
                }
            }
            finally
            {
                if (selection != null && selection.HasSelection)
                {
                    PruneSelection(selection);
                    if (selection.HasSelection) RefreshSelection(selection);
                }
            }
            return true;
        }

        private static bool IsLive(GameItemElement item)
            => item != null && !item.IsDestroyed() && item.parentInventory != null &&
               item.handler != null && item.handler.gameObject.activeInHierarchy && !GeneralHelper.IsDisplayOnly(item);

        private static List<GameItemElement> GetTargets(ItemMultiSelectHandler selection, GameItemElement item, GameInventory inventory)
        {
            if (selection != null && selection.enabled && selection.HasSelection &&
                !selection.IsGroupDragging && !Input.GetKey(selection.key))
            {
                PruneSelection(selection);
                var selected = SelectedItems(selection);
                if (item != null ? selected.Contains(item) : selected.Exists(target => IsLive(target) && target.parentInventory == inventory))
                {
                    // Transfers can merge/destroy items and change the native selection during callbacks.
                    return selected.FindAll(IsLive);
                }
            }
            return item != null ? new List<GameItemElement> { item } : new List<GameItemElement>();
        }

        private static bool Move(GameItemElement item, bool forward, int amount)
        {
            if (ItemPinHelper.IsPinned(item) || !item.MayRemove()) return false;
            amount = Math.Min(amount, item.MaxNumRemove());
            if (amount < 1) return false;
            var entry = EmporiumEntry.Instance;
            bool owned = GeneralHelper.IsItemOwned(item);
            bool pending = item.IsTag(GameCharacterItem.TAG_NOT_PURCHASED);
            var destination = ItemRoutes.Resolve(item, forward, out bool preferContainer);
            if (owned && !pending && forward && preferContainer && RecentContainers.TryStore(item, amount)) return true;
            if (destination == null) return false;
            var merchant = ItemRoutes.Merchant;
            bool merchantPurchase = merchant != null &&
                (item.parentInventory == merchant.barterInventory || item.parentInventory == merchant.barterBuyInventory);
            var client = PlayerStore.Instance?.currentClientInstance?.GetClientBlueprint();
            if (merchantPurchase)
            {
                if (destination != merchant.barterInventory && destination != merchant.barterBuyInventory) return true;
            }
            else if (pending) return true;
            else if (!owned)
            {
                if (client == null || client.isBusinessComplete ||
                    (item.parentInventory != entry.frontInvinvElement && item.parentInventory != entry.backInvinvElement)) return true;
                // Barter rewards stay on the customer's side: native offers may inspect every back-counter item.
                if (client.clientIntent == StoreClient.ClientIntent.BARTER)
                {
                    if (destination == entry.backInvinvElement) NegociationUIManager.Instance.OpenUI();
                    return true;
                }
                if (client.clientIntent != StoreClient.ClientIntent.SELL &&
                    client.clientIntent != StoreClient.ClientIntent.SELLNBUY) return true;
                // Selecting a destination never grants ownership or bypasses the purchase step.
                if (destination != entry.backInvinvElement && destination != entry.frontInvinvElement) return true;
            }
            if (item.parentInventory == destination)
            {
                // Re-select the buying/selling side for mixed customers without moving the item again.
                if (destination == entry.backInvinvElement)
                {
                    NegociationUIManager.Instance.UpdateUI(item);
                    NegociationUIManager.Instance.OpenUI();
                }
                return true;
            }
            if (destination == item || GraphUtils.FindParentEquals(destination, item)) return true;
            var slot = destination.TryFindOneValidInventorySlot(item);
            if (slot == null || !slot.IsValid() || slot.numTransfer <= 0 ||
                !GeneralHelper.MayPlayerInsertInto(slot.inventory)) return true;
            // Native transfer performs capacity/ownership checks and the counter's offer callbacks.
            if (slot.TryAcceptOnce(amount) > 0)
                AudioManager.Instance.TryPlayOne(item.soundDragStart, item.soundDragEnd);
            return true;
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
