using System.Collections.Generic;

namespace RightClickClose
{
    internal static class MachineRoutes
    {
        private static readonly (string Role, string Label, ItemRoutes.Destination Target)[] Parts =
        {
            ("input", "机器原料区", ItemRoutes.Destination.机器原料区),
            ("output", "机器成品区", ItemRoutes.Destination.机器成品区),
            ("power", "机器电源槽", ItemRoutes.Destination.机器电源槽),
            ("module", "机器模块区", ItemRoutes.Destination.机器模块区),
            ("chill", "冰箱冷藏区", ItemRoutes.Destination.冰箱冷藏区),
            ("freeze", "冰箱冷冻区", ItemRoutes.Destination.冰箱冷冻区)
        };
        private static readonly Dictionary<string, ConfigEntry<ItemRoutes.Destination>[]> Routes =
            new Dictionary<string, ConfigEntry<ItemRoutes.Destination>[]>();

        internal static void Init(ModConfig config)
        {
            Routes.Clear();
            foreach (var part in Parts)
            {
                var up = part.Role == "chill" ? ItemRoutes.Destination.冰箱冷冻区 :
                    part.Role == "freeze" ? ItemRoutes.Destination.冰箱冷藏区 :
                    part.Role == "power" || part.Role == "module" ? ItemRoutes.Destination.不移动 : ItemRoutes.Destination.出售柜台;
                Routes[part.Role] = ItemRoutes.Define(config, "machine_" + part.Role, part.Label, up, ItemRoutes.Destination.主物品栏);
            }
        }

        private static MachineryPanel SourcePanel(GameItem item, out MachineryPanelRow row)
        {
            row = GraphUtils.FindParentType<MachineryPanelRow>(item.parentInventory);
            if (row == null) return null;
            foreach (var panel in row.Panels)
            {
                if (panel.role == null || !Routes.ContainsKey(panel.role)) continue;
                var storage = new List<GraphNodeStorage>();
                panel.CollectStorage(storage);
                if (storage.Contains(item.parentInventory)) return panel;
            }
            row = null;
            return null;
        }

        internal static ConfigEntry<ItemRoutes.Destination>[] SourceRoute(GameItem item)
        {
            var panel = SourcePanel(item, out _);
            return panel != null ? Routes[panel.role] : null;
        }

        internal static bool IsMachine(PixelWindow window)
        {
            if (!(window.child is MachineryPanelRow row)) return false;
            foreach (var panel in row.Panels)
                if (panel.role != null && Routes.ContainsKey(panel.role)) return true;
            return false;
        }

        internal static bool IsDestination(ItemRoutes.Destination target)
        {
            foreach (var part in Parts) if (part.Target == target) return true;
            return false;
        }

        internal static GraphNodeStorage Resolve(GameItem item, ItemRoutes.Destination target)
        {
            SourcePanel(item, out var row);
            row ??= RecentContainers.LatestMachine?.child as MachineryPanelRow;
            if (row == null) return null;
            foreach (var part in Parts)
            {
                if (part.Target != target) continue;
                var panel = row.GetPanel(part.Role);
                if (panel == null || panel.isHidden || panel.handler == null || !panel.handler.gameObject.activeInHierarchy)
                    return null;
                var storage = new List<GraphNodeStorage>();
                panel.CollectStorage(storage);
                return storage.Count == 1 ? storage[0] : null;
            }
            return null;
        }
    }
}
