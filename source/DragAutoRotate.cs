using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace RightClickClose
{
    internal static class DragAutoRotate
    {
        private static readonly Action<ItemMouseDragHandler, Vector3> Drag =
            AccessTools.MethodDelegate<Action<ItemMouseDragHandler, Vector3>>(AccessTools.Method(typeof(ItemMouseDragHandler), "Drag"));
        private static readonly AccessTools.FieldRef<ItemMouseDragHandler, KeyCode> ReleaseKey =
            AccessTools.FieldRefAccess<ItemMouseDragHandler, KeyCode>("lastMouseClickedKey");
        private static readonly AccessTools.FieldRef<InputActionManager, Vector3> LastMousePosition =
            AccessTools.FieldRefAccess<InputActionManager, Vector3>("lastMousePosition");
        private static int depth;
        private static SlotMarker preview;
        private static GameItemElement visualItem;
        private static Vector2 pivot, position;
        private static Quaternion rotation;

        private static bool Enabled => Mod.Enabled && Shortcuts.AutoRotate.Value;

        internal static SlotMarker FindSlot(GameInventory inventory, GameItem item, int amount,
            Vector2 center, GridShape shape, TagSystem state)
        {
            var slot = inventory.TryInventorySlot(item, amount, center, shape, state);
            preview = null;
            var drag = ItemMouseDragHandler.current;
            if (!Enabled || drag == null || !drag.IsDraggingItem || drag.GetCurrentItem() != item ||
                !(inventory is GameGridInventory || inventory is GameGridScrollableInventory) ||
                shape == null || !float.IsFinite(center.x) || !float.IsFinite(center.y) ||
                !GeneralHelper.MayPlayerInsertInto(inventory) || (slot != null && slot.IsValid())) return slot;

            // Keep the same center and flip. Each candidate goes through every native placement check.
            for (int turns = 1; turns < 4; turns++)
            {
                var candidate = new GridShapeBuilder(shape).SetOrientation(shape.orientation + 4 - turns);
                var rotated = inventory.TryInventorySlot(item, amount, center, candidate, state);
                if (rotated == null || !rotated.IsValid()) continue;
                preview = rotated;
                return rotated;
            }
            return slot;
        }

        internal static void Begin()
        {
            if (depth++ != 0) return;
            RestoreVisual();
            preview = null;
        }

        internal static void RefreshBeforeRelease(ItemMouseDragHandler handler, ISet<KeyCode> keys)
        {
            if (Enabled && handler.IsDraggingItem && keys.Contains(ReleaseKey(handler)) &&
                handler.GetCurrentItem() is GameItemElement item && !item.IsDestroyed())
            {
                // Release runs before the native mouse-move event, so include this frame's movement.
                var manager = InputActionManager.current;
                Drag(handler, manager != null ? Input.mousePosition - LastMousePosition(manager) : Vector3.zero);
            }
        }

        internal static void Finish(ItemMouseDragHandler handler, MethodBase method, Exception error)
        {
            try
            {
                if (depth != 1 || error != null || !Enabled || !handler.IsDraggingItem ||
                    !(handler.GetCurrentItem() is GameItemElement item) || item.IsDestroyed()) return;
                // Animation, quantity changes and moving inventories can change the slot without a mouse move.
                if (method.Name == "OnEventUpdate") Drag(handler, Vector3.zero);
                if (preview == null || preview.item != item || !preview.IsValid() || preview.itemGridShape == null) return;
                int orientation = item.tempShape.orientation;
                int turns = (orientation - preview.itemGridShape.orientation + 4) % 4;
                if (turns == 0) return;
                visualItem = item;
                pivot = item.fakeRotator.rectTransform.pivot;
                rotation = item.fakeRotator.rectTransform.localRotation;
                position = item.fakeMiddle.rectTransform.anchoredPosition;
                try
                {
                    for (int i = 0; i < turns; i++) item.RotCW();
                }
                finally
                {
                    // Only the ghost keeps the rotated transform. Neither held nor stored shape changes.
                    item.tempShape.SetOrientation(orientation);
                }
            }
            finally { depth--; }
        }

        internal static void RestoreVisual()
        {
            var item = visualItem;
            visualItem = null;
            if (item == null || item.fakeRotator == null || item.fakeMiddle == null) return;
            item.fakeRotator.rectTransform.pivot = pivot;
            item.fakeRotator.rectTransform.localRotation = rotation;
            item.fakeMiddle.rectTransform.anchoredPosition = position;
        }

        internal static void BeforeValidate(GameItemElement item)
        {
            if (visualItem == item) RestoreVisual();
        }

        internal static void Reset() { RestoreVisual(); preview = null; }
    }

    [HarmonyPatch]
    internal static class RotateDragSlot
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "Drag", "RefreshDragHighlight", "EndDrag" })
                yield return AccessTools.Method(typeof(ItemMouseDragHandler), name);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var original = AccessTools.Method(typeof(GameInventory), "TryInventorySlot",
                new[] { typeof(GameItem), typeof(int), typeof(Vector2), typeof(GridShape), typeof(TagSystem) });
            var replacement = AccessTools.Method(typeof(DragAutoRotate), nameof(DragAutoRotate.FindSlot));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    count++;
                }
                yield return instruction;
            }
            if (count != (__originalMethod.Name == "EndDrag" ? 2 : 1))
                throw new InvalidOperationException("Unexpected drag placement calls in " + __originalMethod.Name);
        }
    }

    [HarmonyPatch]
    internal static class DragPreviewScope
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "OnEventPress", "OnEventRelease", "OnEventMouseScroll", "OnEventUpdate",
                "Drag", "RefreshDragHighlight", "EndDrag", "OnEventReset", "AbortDrag", "StartDrag" })
                yield return AccessTools.Method(typeof(ItemMouseDragHandler), name);
        }

        private static void Prefix() => DragAutoRotate.Begin();
        private static void Finalizer(ItemMouseDragHandler __instance, MethodBase __originalMethod, Exception __exception)
            => DragAutoRotate.Finish(__instance, __originalMethod, __exception);
    }

    [HarmonyPatch(typeof(ItemMouseDragHandler), "OnEventRelease")]
    internal static class RefreshDropTarget
    {
        [HarmonyPrefix, HarmonyPriority(Priority.Last)]
        private static void Prefix(ItemMouseDragHandler __instance, ISet<KeyCode> keysReleased)
            => DragAutoRotate.RefreshBeforeRelease(__instance, keysReleased);
    }

    [HarmonyPatch(typeof(GameItemElement), "UpdateRectTransform")]
    internal static class RestoreDragBeforeValidate
    {
        private static void Prefix(GameItemElement __instance) => DragAutoRotate.BeforeValidate(__instance);
    }
}
