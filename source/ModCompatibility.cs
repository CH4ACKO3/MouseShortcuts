using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RightClickClose
{
    internal static class ModCompatibility
    {
        private const string Owner = "mod.ch4acko3_right_click_close.compat";
        private static readonly Harmony Patches = new Harmony(Owner);
        private static readonly HashSet<MethodBase> Patched = new HashSet<MethodBase>();
        private static FieldInfo candidateItem, restockKey, bindingRestock;
        private static MethodInfo updateStockCounts;

        internal static void Enable()
        {
            ModHook.OnModAssetsLoaded += Apply;
            ModHook.OnGameLoadedInit += ApplyLoaded;
            ApplyLoaded();
        }

        internal static void Disable()
        {
            ModHook.OnModAssetsLoaded -= Apply;
            ModHook.OnGameLoadedInit -= ApplyLoaded;
            foreach (var method in Patched) Patches.Unpatch(method, HarmonyPatchType.All, Owner);
            Patched.Clear();
        }

        private static void ApplyLoaded()
        {
            foreach (var mod in ModLoader.Mods) Apply(mod);
        }

        private static void Apply(LoadedMod mod)
        {
            if (!Mod.Enabled || !mod.IsEnabled || mod.Assembly == null) return;
            try
            {
                if (mod.Manifest.ID == "lenchiiii.easier-item-dragging")
                {
                    var type = mod.Assembly.GetType("EasierItemDragging.Swap", true);
                    Patch(RequireMethod(type, "MayRemove", typeof(bool), typeof(GameItem)), nameof(RespectPinnedSwap), false);
                }
                else if (mod.Manifest.ID == "kale.quicktrade")
                {
                    var type = mod.Assembly.GetType("QuickTrade.QuickTradeMod", true);
                    var candidate = type.GetNestedType("Candidate", BindingFlags.NonPublic);
                    if (candidate == null) throw new MissingMemberException(type.FullName, "Candidate");
                    var list = typeof(List<>).MakeGenericType(candidate);
                    candidateItem = RequireField(candidate, "Item", typeof(GameItem), false);
                    updateStockCounts = RequireMethod(type, "UpdateCandidateStockCounts", typeof(void), list);
                    Patch(RequireMethod(type, "CollectCandidates", list, typeof(StoreClient), typeof(GameInventory),
                        typeof(List<GameInventory>), typeof(HashSet<object>)), nameof(FilterRestock), false);
                    restockKey = RequireField(type, "_restockKey", typeof(ConfigEntry<KeyCode>), true);
                    bindingRestock = RequireField(type, "_bindingRestockHotkey", typeof(bool), false);
                    Patch(RequireMethod(type, "HandleRestockHotkey", typeof(void)), nameof(RestockHotkey), true);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RightClickClose] Compatibility with " + mod.Manifest.ID + ": " + e.Message);
            }
        }

        private static MethodInfo RequireMethod(Type type, string name, Type result, params Type[] args)
        {
            var method = AccessTools.DeclaredMethod(type, name, args);
            if (method == null || method.ReturnType != result) throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static FieldInfo RequireField(Type type, string name, Type value, bool isStatic)
        {
            var field = AccessTools.DeclaredField(type, name);
            if (field == null || field.FieldType != value || field.IsStatic != isStatic)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static void Patch(MethodInfo target, string callback, bool prefix)
        {
            if (Patched.Contains(target)) return;
            var patch = new HarmonyMethod(typeof(ModCompatibility), callback);
            Patches.Patch(target, prefix: prefix ? patch : null, postfix: prefix ? null : patch);
            Patched.Add(target);
        }

        private static void RespectPinnedSwap(GameItem __0, ref bool __result)
        {
            if (Mod.Enabled && __result && __0 != null && ItemPinHelper.IsPinned(__0)) __result = false;
        }

        private static void FilterRestock(object __result)
        {
            if (!Mod.Enabled || !(__result is IList candidates)) return;
            bool changed = false;
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                var item = candidateItem.GetValue(candidates[i]) as GameItem;
                if (item == null || !ItemPinHelper.IsPinned(item)) continue;
                candidates.RemoveAt(i);
                changed = true;
            }
            // Filter before budget/variety planning, keeping counts consistent with the remaining stock.
            if (changed) updateStockCounts.Invoke(null, new[] { __result });
        }

        private static bool RestockHotkey(object __instance)
        {
            if (!Mod.Enabled || (bool)bindingRestock.GetValue(__instance)) return true;
            var key = restockKey.GetValue(null) as ConfigEntry<KeyCode>;
            if (key == null || key.Value == KeyCode.None || !Input.GetKeyDown(key.Value)) return true;
            // Resolve hovered-item/customer input first, regardless of component Update order.
            RightClick.Poll();
            return !Shortcuts.ConsumedKeys.Contains(key.Value);
        }
    }
}
