using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;

namespace RightClickClose
{
    internal static class ShopFlow
    {
        private static readonly FieldInfo WalkPath = AccessTools.Field(typeof(StoreClientMono), "dOTweenPath");
        private static readonly FieldInfo WalkingIn = AccessTools.Field(typeof(StoreClientMono), "walkingIn");
        private static readonly FieldInfo ShutterElapsed = AccessTools.Field(typeof(StoreShutterButton), "elapsedTime");
        private static readonly FieldInfo ShutterDuration = AccessTools.Field(typeof(StoreShutterButton), "duration");
        private static readonly MethodInfo FinishDialogue = AccessTools.Method(typeof(DialogUIManager), "FinishConversationNow");
        private static readonly FieldInfo ScheduledEndActions = AccessTools.Field(typeof(DialogUIManager), "scheduledEndActionLines");

        private sealed class ShutterWait
        {
            internal float deadline;
            internal Action action;
            internal bool done;
        }
        private sealed class ShutterRun
        {
            internal StoreShutterButton owner;
            internal bool opening;
            internal readonly List<ShutterWait> waits = new List<ShutterWait>();
        }
        private static ShutterRun shutterRun;

        // Existing wait coroutines retain their run and still perform native unlock/cleanup callbacks.
        internal static void Reset() => shutterRun = null;

        internal static void Advance(DialogUIManager dialog)
        {
            var line = dialog.GetCurrentDialog();
            if (line == null || dialog.IsChoice()) return;
            TutorialUIManager.Instance?.DismissAdvanceDialogHint();
            // A pending choice/auto-advance callback must not advance the next line a second time.
            dialog.StopAllTrackedDialogueCoroutines();
            if (line.nextDialogue == null)
            {
                // Native final-line completion ends the conversation before executing its consequence.
                FinishDialogue.Invoke(dialog, null);
                return;
            }
            dialog.HideBubbles();
            line.ExecuteEndAction();
            if (line.endAction != null) dialog.CheckOwned();
            // End actions can replace the dialogue or dismiss the customer; never advance the replacement.
            if (dialog.GetCurrentDialog() == line) dialog.ContinueConversation();
        }

        internal static bool TrySkipAnimation(bool openingOnly)
        {
            if (TrySkipShutter(openingOnly)) return true;
            var client = StoreClientMono.Instance;
            if (client == null || !client.gameObject.activeInHierarchy) return false;
            if ((bool)WalkingIn.GetValue(client))
            {
                if (!client.IsMoving()) return false;
                client.SetImmediatlyArrive();
                return true;
            }
            var right = client.pathRight?.tween;
            if (right != null && right.IsActive() && right.IsPlaying())
            {
                right.Complete(true);
                return true;
            }
            var walk = (WalkPath.GetValue(client) as DOTweenPath)?.tween;
            if (walk != null && walk.IsActive() && walk.IsPlaying() && walk.IsBackwards())
            {
                // Rewind fires the native OnLeft callback; manually calling it would double-unlock the bell.
                walk.Rewind();
                return true;
            }
            return false;
        }

        internal static void BeginShutter(StoreShutterButton owner, bool opening)
            => shutterRun = new ShutterRun { owner = owner, opening = opening };

        internal static IEnumerator WaitForCustomer(float delay, Action action)
            => CustomerWait(delay, action, null, null);

        internal static IEnumerator WaitForDialogueEnd(float delay, Action action)
        {
            var dialog = DialogUIManager.Instance;
            return CustomerWait(delay, action, dialog, dialog?.GetCurrentDialog());
        }

        private static IEnumerator CustomerWait(float delay, Action action, DialogUIManager dialog, Dialogue line)
        {
            if (!Mod.Enabled) return MetaHelper.WaitAndDo(delay, action);
            var store = StoreUIManager.Instance;
            var player = PlayerStore.Instance;
            var client = player.currentClientInstance;
            return MetaHelper.WaitAndDo(delay, () =>
            {
                if (line != null && dialog != null)
                    (ScheduledEndActions.GetValue(dialog) as List<Dialogue>)?.Remove(line);
                // Keep non-customer dialogue unchanged; customer callbacks cannot cross visits or scenes.
                if (store != null && StoreUIManager.Instance == store &&
                    (client == null || ReferenceEquals(player.currentClientInstance, client))) action();
                else Debug.Log("[RightClickClose] Ignored delayed callback for a departed customer.");
            });
        }

        internal static IEnumerator WaitShutter(float delay, Action action)
        {
            var run = shutterRun;
            if (!Mod.Enabled || run == null) return MetaHelper.WaitAndDo(delay, action);
            var wait = new ShutterWait { deadline = Time.time + delay, action = action };
            run.waits.Add(wait);
            return Wait(run, wait);
        }

        private static IEnumerator Wait(ShutterRun run, ShutterWait wait)
        {
            try
            {
                do { yield return null; } while (!wait.done && Time.time < wait.deadline);
                if (!wait.done)
                {
                    wait.done = true;
                    wait.action();
                }
            }
            finally { run.waits.Remove(wait); }
        }

        private static bool TrySkipShutter(bool openingOnly)
        {
            var shutter = StoreShutterButton.Instance;
            var run = shutterRun;
            if (shutter == null || run == null || run.owner != shutter || run.waits.Count == 0) return false;
            if (openingOnly && !run.opening) return false;
            var pending = run.waits.FindAll(wait => !wait.done);
            if (pending.Count == 0) return true;
            // Preserve original callback order and execute each exactly once, including unlock/hide actions.
            pending.Sort((a, b) => a.deadline.CompareTo(b.deadline));
            foreach (var wait in pending) wait.done = true;
            ShutterElapsed.SetValue(shutter, ShutterDuration.GetValue(shutter));
            shutter.LerpShutter(shutter.shutterGameObject);
            shutter.audioSource?.Stop();
            shutter.audioSourceShutterOpen?.Stop();
            shutter.audioSourceShutterClose?.Stop();
            foreach (var wait in pending)
            {
                try { wait.action(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
            return true;
        }

        internal static IEnumerable<CodeInstruction> ReplaceWaitCalls(IEnumerable<CodeInstruction> instructions,
            MethodBase originalMethod, string replacementName, int expectedCount)
        {
            var original = AccessTools.Method(typeof(MetaHelper), "WaitAndDo", new[] { typeof(float), typeof(Action) });
            var replacement = AccessTools.Method(typeof(ShopFlow), replacementName);
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
            if (count != expectedCount)
                throw new InvalidOperationException($"Unexpected waits in {originalMethod.DeclaringType?.Name}.{originalMethod.Name}: expected {expectedCount}, found {count}");
        }
    }

    [HarmonyPatch]
    internal static class SkippableShutterWaits
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(StoreShutterButton), "OpenShutter");
            yield return AccessTools.Method(typeof(StoreShutterButton), "EndAndClose");
        }
        private static void Prefix(StoreShutterButton __instance, MethodBase __originalMethod)
            => ShopFlow.BeginShutter(__instance, __originalMethod.Name == "OpenShutter");

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
            => ShopFlow.ReplaceWaitCalls(instructions, __originalMethod, nameof(ShopFlow.WaitShutter),
                __originalMethod.Name == "OpenShutter" ? 1 : 3);
    }

    [HarmonyPatch]
    internal static class CustomerBoundWaits
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(StoreUIManager), nameof(StoreUIManager.OnNextClientArrived));
            yield return AccessTools.Method(typeof(DialogUIManager), nameof(DialogUIManager.CurrentClientLeave));
            yield return AccessTools.Method(typeof(DialogUIManager), nameof(DialogUIManager.OnCurrentTextDisplayed));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
            => ShopFlow.ReplaceWaitCalls(instructions, __originalMethod, __originalMethod.Name == "OnCurrentTextDisplayed"
                ? nameof(ShopFlow.WaitForDialogueEnd) : nameof(ShopFlow.WaitForCustomer), 1);
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.DismissCurrentClient))]
    internal static class FinishDepartingDialogue
    {
        private static bool preparing;

        private static bool Prefix(PlayerStore __instance, bool interruptDialog)
        {
            if (!Mod.Enabled || preparing || __instance.currentClientInstance == null) return true;
            var client = __instance.currentClientInstance;
            var dialog = DialogUIManager.Instance;
            preparing = true;
            try
            {
                // Resolve earned consequences while their customer still owns the counter.
                dialog?.FlushScheduledEndActions();
                var line = dialog?.GetCurrentDialog();
                if (ReferenceEquals(__instance.currentClientInstance, client) && interruptDialog &&
                    dialog != null && dialog.IsLineStillDisplaying(line))
                {
                    // Finish the pending consequence now; native dismissal interrupts the UI exactly once.
                    line.ExecuteEndAction();
                    dialog.CheckOwned();
                }
                // An end action can itself dismiss the customer. Do not dismiss/unlock twice.
                return ReferenceEquals(__instance.currentClientInstance, client);
            }
            finally { preparing = false; }
        }

        private static void Postfix(PlayerStore __instance)
        {
            if (Mod.Enabled && __instance.currentClientInstance == null && StoreUIManager.Instance != null)
                StoreUIManager.Instance.isAboutToStartMainDialog = false;
        }
    }
}
