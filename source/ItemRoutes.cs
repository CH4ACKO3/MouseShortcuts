namespace RightClickClose
{
    internal static class ItemRoutes
    {
        internal enum Destination { 不移动, 主物品栏, 出售柜台, 买入柜台, 展示窗, 垃圾桶, 外出背包, 外出地面, 人物货架, 人物待买区, 人物待卖区,
            机器原料区, 机器成品区, 机器电源槽, 机器模块区, 冰箱冷藏区, 冰箱冷冻区, 外出背包后口袋 }

        private static ConfigEntry<Destination>[] main, sale, display, purchase, pendingPurchase, other;
        private static ConfigEntry<Destination>[] shoppingGround, shoppingCarried;
        private static ConfigEntry<Destination>[] ground, carried, outsideSale, outsideOther, merchantStock, merchantBuy, merchantSell;

        internal static void Init(ModConfig config)
        {
            main = Define(config, "main", "主物品栏", Destination.出售柜台, Destination.展示窗);
            sale = Define(config, "sale", "出售柜台（自有物品）", Destination.展示窗, Destination.主物品栏);
            display = Define(config, "display", "展示窗", Destination.主物品栏, Destination.出售柜台);
            purchase = Define(config, "purchase", "买入柜台／顾客侧", Destination.不移动, Destination.出售柜台);
            pendingPurchase = Define(config, "pending_purchase", "交易柜台（未买下物品）", Destination.买入柜台, Destination.出售柜台);
            other = Define(config, "other", "其他容器／区域", Destination.出售柜台, Destination.主物品栏);
            ground = Define(config, "outside_ground", "外出地面", Destination.外出背包, Destination.不移动);
            carried = Define(config, "outside_carried", "外出背包／口袋", Destination.出售柜台, Destination.外出地面);
            shoppingGround = Define(config, "shopping_ground", "外出购物地面", Destination.外出背包, Destination.外出背包后口袋);
            shoppingCarried = Define(config, "shopping_carried", "外出购物背包／口袋", Destination.外出地面, Destination.外出地面);
            outsideSale = Define(config, "outside_sale", "外出交易柜台（自有物品）", Destination.不移动, Destination.外出背包);
            outsideOther = Define(config, "outside_other", "外出其他容器／区域", Destination.外出背包, Destination.外出地面);
            merchantStock = Define(config, "merchant_stock", "人物交易货架", Destination.不移动, Destination.人物待买区);
            merchantBuy = Define(config, "merchant_buy", "人物交易待买区", Destination.人物货架, Destination.不移动);
            merchantSell = Define(config, "merchant_sell", "人物交易待卖区", Destination.不移动, Destination.主物品栏);
            MachineRoutes.Init(config);
        }

        internal static ConfigEntry<Destination>[] Define(ModConfig config, string key, string label, Destination up, Destination down)
            => new[]
            {
                config.DefineEnum("route_" + key + "_up", up, "区域与滚轮目标", () => label + " ↑",
                    () => "没有优先的新容器时，上方向快捷键要移入的位置。外出时主物品栏指向背包；人物交易时出售柜台指向待卖区。未买下的物品只在交易区域内选择和撤回。"),
                config.DefineEnum("route_" + key + "_down", down, "区域与滚轮目标", () => label + " ↓",
                    () => "下方向快捷键要移入的位置。「外出背包后口袋」依次尝试背包、左口袋、右口袋。垃圾桶是实际容器，不直接销毁；放不下时留在原处。")
            };

        internal static GameCharacterItem Merchant
            => OverlayHandler.current != null && OverlayHandler.current.barterWindow?.IsVisible() == true
                ? OverlayHandler.current.currentBarterCharacter : null;

        internal static bool IsOutside => MapUIManager.Instance != null && MapUIManager.Instance.isOutside;

        internal static GraphNodeStorage Resolve(GameItem item, bool up, out bool preferContainer)
        {
            var entry = EmporiumEntry.Instance;
            var source = item.parentInventory;
            var merchant = Merchant;
            var location = MapUIManager.Instance?.Current;
            bool shopping = IsOutside && (merchant != null || location.ShowsStoreCounters || location.Id == AfterhourLocation.COMMISSARY);
            var route = merchant != null && source == merchant.barterInventory ? merchantStock :
                merchant != null && source == merchant.barterBuyInventory ? merchantBuy :
                merchant != null && source == merchant.barterSellInventory ? merchantSell :
                IsOutside && source == entry.afterhourInventory ? (shopping ? shoppingGround : ground) :
                IsOutside && (Inside(source, entry.afterhourPocketSlotInvBackpack) ||
                    Inside(source, entry.afterhourPocketSlotInvLeft) || Inside(source, entry.afterhourPocketSlotInvRight)) ? (shopping ? shoppingCarried : carried) :
                IsOutside && GeneralHelper.IsItemOwned(item) &&
                    (source == entry.backInvinvElement || source == entry.frontInvinvElement) ? outsideSale :
                source == entry.invElement ? main :
                source == entry.showcaseElement ? display :
                source == entry.frontInvinvElement ? purchase :
                source == entry.backInvinvElement ? (GeneralHelper.IsItemOwned(item) ? sale : pendingPurchase) :
                IsOutside ? outsideOther : other;
            var machineRoute = MachineRoutes.SourceRoute(item);
            var target = (machineRoute ?? route)[up ? 0 : 1].Value;
            preferContainer = machineRoute == null && route != shoppingCarried &&
                target != Destination.外出背包后口袋 && !MachineRoutes.IsDestination(target);
            switch (target)
            {
                case Destination.不移动: return null;
                case Destination.主物品栏: return IsOutside ? Backpack() : entry.invElement;
                case Destination.出售柜台: return merchant != null ? merchant.barterSellInventory : Visible(entry.backInvinvElement);
                case Destination.买入柜台: return Visible(entry.frontInvinvElement);
                case Destination.展示窗: return IsOutside ? null : entry.showcaseElement;
                case Destination.垃圾桶:
                    return !IsOutside && item != entry.trashcan ? entry.trashcan?.contentWindow?.child as GameInventory : null;
                case Destination.外出背包: return IsOutside ? Backpack() : null;
                case Destination.外出背包后口袋: return IsOutside ? CarriedDestination(item) : null;
                case Destination.外出地面: return IsOutside ? Visible(entry.afterhourInventory) : null;
                case Destination.人物货架: return merchant?.barterInventory;
                case Destination.人物待买区: return merchant?.barterBuyInventory;
                case Destination.人物待卖区: return merchant?.barterSellInventory;
                default: return MachineRoutes.Resolve(item, target);
            }
        }

        private static bool Inside(GraphNodeStorage source, GraphNodeStorage root)
            => root != null && (source == root || GraphUtils.FindParentEquals(source, root));

        private static GameInventory Visible(GameInventory inventory)
            => inventory is PixelElement element && element.handler != null && element.handler.gameObject.activeInHierarchy
                ? inventory : null;

        private static GraphNodeStorage CarriedDestination(GameItem item)
        {
            var entry = EmporiumEntry.Instance;
            var destinations = new GraphNodeStorage[]
            {
                Backpack(), Visible(entry.afterhourPocketSlotInvLeft), Visible(entry.afterhourPocketSlotInvRight)
            };
            foreach (var destination in destinations)
            {
                if (destination == null || destination == item || item.parentInventory == destination ||
                    GraphUtils.FindParentEquals(destination, item)) continue;
                var slot = destination.TryFindOneValidInventorySlot(item);
                if (slot != null && slot.IsValid() && slot.numTransfer > 0 && GeneralHelper.MayPlayerInsertInto(slot.inventory))
                    return destination;
            }
            return null;
        }

        private static GraphNodeStorage Backpack()
        {
            var slot = EmporiumEntry.Instance.afterhourPocketSlotInvBackpack;
            return Visible(slot) != null && slot.childItem != null && GeneralHelper.IsItemEffectivelyOwned(slot.childItem)
                ? slot.childItem : null;
        }
    }
}
