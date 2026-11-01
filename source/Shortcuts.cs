using System;
using System.Collections.Generic;
using UnityEngine;

namespace RightClickClose
{
    internal static class Shortcuts
    {
        internal static ConfigEntry<string> ItemForward, ItemBack, Pin, CustomerNext, CustomerTrade, Dismiss, Open, Call, Close, CloseWindow;
        internal static ConfigEntry<string> SkipAnimation, SkipCustomerAnimation;
        internal static ConfigEntry<bool> PreferContainer, AutoRotate, ForceDialogue;
        internal static ConfigEntry<float> DismissInterval;
        internal static readonly HashSet<KeyCode> ConsumedKeys = new HashSet<KeyCode>();

        internal static void Init()
        {
            var config = ModConfig.For("ch4acko3_right_click_close");
            ItemForward = Define(config, "item_forward", "WheelUp", "物品", "物品上方向快捷键");
            ItemBack = Define(config, "item_back", "WheelDown", "物品", "物品下方向快捷键");
            Pin = Define(config, "pin", "Mouse2", "物品", "固定／取消固定");
            CustomerNext = Define(config, "customer_next", "Mouse1,WheelDown", "顾客与营业窗口", "推进对话／打开交易界面（不成交）");
            CustomerTrade = Define(config, "customer_trade", "Mouse1", "顾客与营业窗口", "对话结束后成交");
            SkipAnimation = Define(config, "skip_shop_animation", "WheelDown", "顾客与营业窗口", "跳过当前顾客进出／卷帘门动画");
            SkipCustomerAnimation = Define(config, "skip_customer_animation", "WheelUp", "顾客与营业窗口", "跳过顾客进出／开店卷帘门动画");
            ForceDialogue = config.Define("force_wheel_dialogue", true, "顾客与营业窗口", () => "滚轮推进对话直接跳到下一句",
                () => "连续滚动无需停顿，不等待当前句打字和句间暂停；保留对话选项。关闭后恢复原版推进方式和滚轮间隔。");
            Dismiss = Define(config, "dismiss", "WheelUp", "顾客与营业窗口", "遣散并召唤下一位");
            Open = Define(config, "open", "WheelUp", "顾客与营业窗口", "开始营业");
            Call = Define(config, "call", "WheelUp,WheelDown", "顾客与营业窗口", "无顾客时叫下一位");
            Close = Define(config, "close", "WheelUp,WheelDown", "顾客与营业窗口", "顾客和队列清空后结束营业");
            // Retain the stored key so an existing interval setting survives this change of scope.
            DismissInterval = config.Define("shop_wheel_interval", 0.1f, "顾客与营业窗口",
                () => "两次遣散之间的最小间隔（秒）", min: 0f, max: 2f,
                tooltip: () => "仅限制遣散快捷键，从上次成功遣散计算间隔；期间滚动不重新计时。默认 0.1 秒，设为 0 不限制。不影响开店、叫号、关店、对话和动画跳过。");
            CloseWindow = Define(config, "close_window", "Mouse1", "界面", "关闭没有原版交互的区域");
            PreferContainer = config.Define("prefer_recent_container", true, "物品", () => "优先放进最新打开的容器",
                () => "营业前或没有顾客时也记录容器，新顾客到来时清空标记。上方向优先选择仍打开的最新容器，放不下时不回退；无标记时按区域配置移动。");
            ItemRoutes.Init(config);
            AutoRotate = config.Define("auto_rotate_drag", true, "物品", () => "拖拽放置时自动尝试旋转",
                () => "当前方向放不下时，在同一落点尝试其他三个方向，不自动翻面。预览临时旋转，放下时才应用；手动旋转仍可用。");
        }

        private static ConfigEntry<string> Define(ModConfig config, string key, string value, string section, string label)
            => config.Define(key, value, section, () => label, tooltip: () =>
                "Mouse1=右键，Mouse2=中键，Mouse3/Mouse4=侧键，WheelUp/WheelDown=滚轮；也可填 Q、F6 等键名。Ctrl+Q 等组合键可用，逗号分隔多个按键，留空或 None 禁用。不支持左键；同一区域避免重复绑定。");

        internal static void BeginFrame()
        {
            ConsumedKeys.RemoveWhere(key => !Input.GetKey(key) && !Input.GetKeyUp(key));
        }

        internal readonly struct Gesture
        {
            internal readonly KeyCode Key;
            internal readonly bool Wheel;
            internal Gesture(KeyCode key, bool wheel) { Key = key; Wheel = wheel; }
            internal void Consume()
            {
                if (Wheel) RightClick.ScrollConsumed = true;
                else ConsumedKeys.Add(Key);
            }
        }

        internal static bool Match(ConfigEntry<string> binding, out Gesture gesture)
        {
            gesture = default;
            if (binding == null) return false;
            foreach (var alternative in binding.Value.Split(','))
            {
                var parts = alternative.Trim().Split('+');
                bool ctrl = false, shift = false, alt = false, valid = true;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    switch (parts[i].Trim().ToLowerInvariant())
                    {
                        case "ctrl": ctrl = true; break;
                        case "shift": shift = true; break;
                        case "alt": alt = true; break;
                        default: valid = false; break;
                    }
                }
                if (!valid || ctrl != (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) ||
                    shift != (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ||
                    alt != (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))) continue;
                var name = parts[parts.Length - 1].Trim();
                if (name.Equals("WheelUp", StringComparison.OrdinalIgnoreCase) || name.Equals("WheelDown", StringComparison.OrdinalIgnoreCase))
                {
                    bool up = name.Equals("WheelUp", StringComparison.OrdinalIgnoreCase);
                    if (up ? Input.mouseScrollDelta.y > 0 : Input.mouseScrollDelta.y < 0)
                    { gesture = new Gesture(KeyCode.None, true); return true; }
                }
                else if (Enum.TryParse(name, true, out KeyCode key) && Enum.IsDefined(typeof(KeyCode), key) &&
                    key != KeyCode.None && key != KeyCode.Mouse0 && Input.GetKeyDown(key))
                { gesture = new Gesture(key, false); return true; }
            }
            return false;
        }
    }
}
