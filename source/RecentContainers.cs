using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RightClickClose
{
    internal static class RecentContainers
    {
        private static readonly List<PixelWindow> Opened = new List<PixelWindow>();
        internal static void Reset() => Opened.Clear();

        internal static void OpenedWindow(PixelWindow window)
        {
            if (!Mod.Enabled || !window.canMove || window.parentItems.Count == 0 || !window.IsVisible()) return;
            if (Inventories(window.child).Count == 0) return;
            Opened.Remove(window);
            Opened.Add(window);
        }

        internal static bool TryStore(GameItemElement item, int amount)
        {
            if (!Shortcuts.PreferContainer.Value) return false;
            for (int i = Opened.Count - 1; i >= 0; i--)
            {
                var window = Opened[i];
                if (window.handler == null || !window.handler.gameObject.activeInHierarchy) continue;
                var inventories = Inventories(window.child);
                if (inventories.Count == 0) continue;
                foreach (var owner in window.parentItems)
                    if (owner == item || GraphUtils.FindParentEquals(owner, item)) return true;
                if (inventories.Contains(item.parentInventory))
                {
                    var counter = EmporiumEntry.Instance.backInvinvElement;
                    if (counter == null) return true;
                    inventories.Clear();
                    inventories.Add(counter);
                }
                foreach (var inventory in inventories)
                {
                    if (item.parentInventory == inventory) return true;
                    if (!GeneralHelper.MayPlayerInsertInto(inventory)) continue;
                    var slot = inventory.TryFindOneValidInventorySlot(item);
                    if (slot == null || !slot.IsValid() || slot.numTransfer <= 0) continue;
                    if (slot.TryAcceptOnce(amount) > 0)
                    {
                        AudioManager.Instance.TryPlayOne(item.soundDragStart, item.soundDragEnd);
                        break;
                    }
                }
                return true;
            }
            return false;
        }

        private static List<GameInventory> Inventories(GraphNodeStorage root)
        {
            var result = new List<GameInventory>();
            var pending = new Stack<GraphNodeStorage>();
            var visited = new HashSet<GraphNodeStorage>();
            if (root != null) pending.Push(root);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (!visited.Add(node) || node is GameItem || node is PixelWindow) continue;
                if (node is PixelElement element && (element.handler == null || !element.handler.gameObject.activeInHierarchy)) continue;
                if (node is GameInventory inventory) { result.Add(inventory); continue; }
                foreach (var child in node.children) if (child != null) pending.Push(child);
            }
            return result;
        }
    }

    [HarmonyPatch]
    internal static class TrackOpenedContainer
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PixelWindow), "Show", System.Type.EmptyTypes);
            yield return AccessTools.Method(typeof(PixelWindow), "Show", new[] { typeof(Vector2), typeof(Vector2), typeof(bool) });
        }
        private static void Prefix(PixelWindow __instance, out bool __state)
            => __state = __instance.handler != null && __instance.IsVisible();
        private static void Postfix(PixelWindow __instance, bool __state)
        {
            if (!__state) RecentContainers.OpenedWindow(__instance);
        }
    }

    [HarmonyPatch(typeof(StoreUIManager), "OnNextClientArrived")]
    internal static class ResetContainersOnArrival
    {
        private static void Prefix() => RecentContainers.Reset();
    }
}
