namespace RightClickClose
{
    internal static class ItemRoutes
    {
        internal enum Destination { 不移动, 主物品栏, 出售柜台, 买入柜台, 展示窗, 垃圾桶 }

        private static ConfigEntry<Destination>[] main, sale, display, purchase, pendingPurchase, other;

        internal static void Init(ModConfig config)
        {
            main = Define(config, "main", "主物品栏", Destination.出售柜台, Destination.展示窗);
            sale = Define(config, "sale", "出售柜台（自有物品）", Destination.展示窗, Destination.主物品栏);
            display = Define(config, "display", "展示窗", Destination.主物品栏, Destination.出售柜台);
            purchase = Define(config, "purchase", "买入柜台／顾客侧", Destination.不移动, Destination.出售柜台);
            pendingPurchase = Define(config, "pending_purchase", "交易柜台（未买下物品）", Destination.买入柜台, Destination.出售柜台);
            other = Define(config, "other", "其他容器／区域", Destination.出售柜台, Destination.主物品栏);
        }

        private static ConfigEntry<Destination>[] Define(ModConfig config, string key, string label, Destination up, Destination down)
            => new[]
            {
                config.DefineEnum("route_" + key + "_up", up, "区域与滚轮目标", () => label + " ↑",
                    () => "没有优先的新容器时，上方向快捷键要移入的位置。未买下的物品只能在两侧柜台间移动，易物保持原版整单规则。"),
                config.DefineEnum("route_" + key + "_down", down, "区域与滚轮目标", () => label + " ↓",
                    () => "下方向快捷键要移入的位置。垃圾桶是实际容器，不直接销毁；放不下时留在原处。")
            };

        internal static GameInventory Resolve(GameItem item, bool up)
        {
            var entry = EmporiumEntry.Instance;
            var source = item.parentInventory;
            var route = source == entry.invElement ? main :
                source == entry.showcaseElement ? display :
                source == entry.frontInvinvElement ? purchase :
                source == entry.backInvinvElement ? (GeneralHelper.IsItemOwned(item) ? sale : pendingPurchase) : other;
            switch (route[up ? 0 : 1].Value)
            {
                case Destination.主物品栏: return entry.invElement;
                case Destination.出售柜台: return entry.backInvinvElement;
                case Destination.买入柜台: return entry.frontInvinvElement;
                case Destination.展示窗: return entry.showcaseElement;
                case Destination.垃圾桶:
                    return item != entry.trashcan ? entry.trashcan?.contentWindow?.child as GameInventory : null;
                default: return null;
            }
        }
    }
}
