using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Multiplayer.Client.AsyncTime;
using Multiplayer.Client.Util;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;
using Random = UnityEngine.Random;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch(typeof(PawnTweener))]
    [HarmonyPatch(nameof(PawnTweener.TweenedPos), MethodType.Getter)]
    static class DrawPosPatch
    {
        static bool Prefix() => Multiplayer.Client == null || Multiplayer.InInterface;

        // Give the root position during ticking
        static void Postfix(PawnTweener __instance, ref Vector3 __result)
        {
            if (Multiplayer.Client == null || Multiplayer.InInterface) return;
            __result = __instance.TweenedPosRoot();
        }
    }

    [HarmonyPatch]
    static class FixApparelSort
    {
        static MethodBase TargetMethod() =>
            MpMethodUtil.GetLambda(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.SortWornApparelIntoDrawOrder));

        static void Postfix(Apparel a, Apparel b, ref int __result)
        {
            if (__result == 0)
                __result = a.thingIDNumber.CompareTo(b.thingIDNumber);
        }
    }

    [HarmonyPatch(typeof(Pawn_MeleeVerbs), nameof(Pawn_MeleeVerbs.TryGetMeleeVerb))]
    static class TryGetMeleeVerbPatch
    {
        static bool Cancel => Multiplayer.Client != null && Multiplayer.InInterface;

        static bool Prefix()
        {
            // Namely FloatMenuUtility.GetMeleeAttackAction
            return !Cancel;
        }

        static void Postfix(Pawn_MeleeVerbs __instance, Thing target, ref Verb __result)
        {
            if (Cancel)
                __result = __instance.GetUpdatedAvailableVerbsList(false).FirstOrDefault(ve => ve.GetSelectionWeight(target) != 0).verb;
        }
    }

    [HarmonyPatch(typeof(PawnBioAndNameGenerator), nameof(PawnBioAndNameGenerator.TryGetRandomUnusedSolidName))]
    static class GenerateNewPawnInternalPatch
    {
        static MethodBase FirstOrDefault = SymbolExtensions.GetMethodInfo<IEnumerable<NameTriple>>(
            e => e.FirstOrDefault()
        );

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e)
        {
            List<CodeInstruction> insts = new List<CodeInstruction>(e);

            for (int i = insts.Count - 1; i >= 0; i--)
            {
                if (insts[i].operand == FirstOrDefault)
                    insts.Insert(
                       i + 1,
                       new CodeInstruction(OpCodes.Ldloc_1),
                       new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(GenerateNewPawnInternalPatch), nameof(Unshuffle)).MakeGenericMethod(typeof(NameTriple)))
                   );
            }

            return insts;
        }

        public static void Unshuffle<T>(List<T> list)
        {
            uint iters = Rand.iterations;

            int i = 0;
            while (i < list.Count)
            {
                int index = Mathf.Abs(MurmurHash.GetInt(Rand.seed, iters--) % (i + 1));
                (list[index], list[i]) = (list[i], list[index]);
                i++;
            }
        }
    }

    [HarmonyPatch(typeof(WorldObjectSelectionUtility), nameof(WorldObjectSelectionUtility.VisibleToCameraNow))]
    static class CaravanVisibleToCameraPatch
    {
        static void Postfix(ref bool __result)
        {
            if (!Multiplayer.InInterface)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(WealthWatcher), nameof(WealthWatcher.ForceRecount))]
    static class WealthWatcherRecalc
    {
        static bool Prefix() => Multiplayer.Client == null || !Multiplayer.InInterface;
    }

    [HarmonyPatch(typeof(FloodFillerFog), nameof(FloodFillerFog.FloodUnfog))]
    static class FloodUnfogPatch
    {
        static void Postfix(ref FloodUnfogResult __result)
        {
            if (Multiplayer.Client != null)
                __result.allOnScreen = false;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ProcessPostTickVisuals))]
    static class DrawTrackerTickPatch
    {
        static MethodInfo CellRectContains = AccessTools.Method(typeof(CellRect), nameof(CellRect.Contains));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                yield return inst;

                if (inst.operand == CellRectContains)
                {
                    yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                    yield return new CodeInstruction(OpCodes.Or);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Zone), nameof(Zone.Cells), MethodType.Getter)]
    static class ZoneCellsShufflePatch
    {
        static FieldInfo CellsShuffled = AccessTools.Field(typeof(Zone), nameof(Zone.cellsShuffled));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            bool found = false;

            foreach (var inst in insts)
            {
                yield return inst;

                if (!found && inst.operand == CellsShuffled)
                {
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ZoneCellsShufflePatch), nameof(ShouldShuffle)));
                    yield return new CodeInstruction(OpCodes.Not);
                    yield return new CodeInstruction(OpCodes.Or);
                    found = true;
                }
            }
        }

        static bool ShouldShuffle()
        {
            return Multiplayer.Client == null || Multiplayer.Ticking;
        }
    }

    [HarmonyPatch]
    static class SortArchivablesById
    {
        static MethodBase TargetMethod()
        {
            return MpMethodUtil.GetLambda(typeof(Archive), nameof(Archive.Add));
        }

        static void Postfix(IArchivable x, ref int __result)
        {
            if (x is ArchivedDialog dialog)
                __result = dialog.ID;
            else if (x is Letter letter)
                __result = letter.ID;
            else if (x is Message msg)
                __result = msg.ID;
        }
    }

    [HarmonyPatch(typeof(DangerWatcher), nameof(DangerWatcher.DangerRating), MethodType.Getter)]
    static class DangerRatingPatch
    {
        static bool Prefix() => !Multiplayer.InInterface;

        static void Postfix(DangerWatcher __instance, ref StoryDanger __result)
        {
            if (Multiplayer.InInterface)
                __result = __instance.dangerRatingInt;
        }
    }

    [HarmonyPatch(typeof(Caravan), nameof(Caravan.ImmobilizedByMass), MethodType.Getter)]
    static class ImmobilizedByMass_Patch
    {
        static bool Prefix() => !Multiplayer.InInterface;
    }

    [HarmonyPatch(typeof(StoryWatcher_PopAdaptation), nameof(StoryWatcher_PopAdaptation.Notify_PawnEvent))]
    static class CancelStoryWatcherEventInInterface
    {
        static bool Prefix() => !Multiplayer.InInterface;
    }

    [HarmonyPatch(typeof(AutoSlaughterManager), nameof(AutoSlaughterManager.Notify_ConfigChanged))]
    static class CancelAutoslaughterDirtying
    {
        static bool Prefix() => !Multiplayer.InInterface;
    }

    [HarmonyPatch(typeof(Pawn_AbilityTracker), nameof(Pawn_AbilityTracker.AllAbilitiesForReading), MethodType.Getter)]
    static class DontRecacheAbilitiesInInterface
    {
        static bool Prefix() => !Multiplayer.InInterface;

        static void Postfix(Pawn_AbilityTracker __instance, ref List<Ability> __result)
        {
            // The result can be null only if the method gets cancelled by the prefix
            if (__result == null)
                __result = __instance.allAbilitiesCached;
        }
    }

    // In vanilla WealthWatcher.ResetStaticData depends on Def indices but it runs before they are set
    // This patch runs it later
    [HarmonyPatch(typeof(ShortHashGiver), nameof(ShortHashGiver.GiveAllShortHashes))]
    [EarlyPatch]
    static class FixWealthWatcherStaticData
    {
        static void Prefix()
        {
            WealthWatcher.ResetStaticData();
        }
    }

    [HarmonyPatch(typeof(PriorityWork), nameof(PriorityWork.Clear))]
    static class PriorityWorkClearNoInterface
    {
        // This can get called in the UI but has side effects
        static bool Prefix(PriorityWork __instance)
        {
            return Multiplayer.Client == null || Multiplayer.ExecutingCmds;
        }
    }

    [HarmonyPatch(typeof(Pawn_GeneTracker), nameof(Pawn_GeneTracker.Notify_GenesChanged))]
    static class CheckWhetherBiotechIsActive
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.operand == AccessTools.PropertyGetter(typeof(ModLister), nameof(ModLister.BiotechInstalled)))
                    inst.operand = AccessTools.PropertyGetter(typeof(ModsConfig), nameof(ModsConfig.BiotechActive));
                yield return inst;
            }
        }
    }

    [HarmonyPatch]
    static class UpdateWorldStateWhenTickingOnly
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            // Handles relation and retaliation for polluting the world
            yield return AccessTools.DeclaredMethod(typeof(CompDissolutionEffect_Goodwill), nameof(CompDissolutionEffect_Goodwill.WorldUpdate));
            // Handles increasing/decreasing world pollution
            yield return AccessTools.DeclaredMethod(typeof(CompDissolutionEffect_Pollution), nameof(CompDissolutionEffect_Pollution.WorldUpdate));
        }

        static bool Prefix()
        {
            // In MP only allow updates from MultiplayerWorldComp:Tick()
            return Multiplayer.Client == null || AsyncWorldTimeComp.tickingWorld;
        }
    }

    [HarmonyPatch(typeof(QuestNode_Root_PollutionRetaliation), nameof(QuestNode_Root_PollutionRetaliation.RunInt))]
    static class ReplaceUnityRngPollutionRetaliation
    {
        // Simplified transpiler from MP Compat.
        // Source: https://github.com/rwmt/Multiplayer-Compatibility/blob/2e82e71aef64c5a5a4fc879db6f49d3c20da25cb/Source/PatchingUtilities.cs#L226
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts, MethodBase original)
        {
            var anythingPatched = false;

            var parameters = new[] { typeof(int), typeof(int) };
            var unityRandomRangeInt = AccessTools.DeclaredMethod(typeof(Random), nameof(Random.Range), parameters);
            var verseRandomRangeInt = AccessTools.DeclaredMethod(typeof(Rand), nameof(Rand.Range), parameters);

            foreach (var inst in insts)
            {
                if ((inst.opcode == OpCodes.Call || inst.opcode == OpCodes.Callvirt) && inst.operand is MethodInfo method && method == unityRandomRangeInt)
                {
                    inst.opcode = OpCodes.Call;
                    inst.operand = verseRandomRangeInt;

                    anythingPatched = true;
                }

                yield return inst;
            }

            if (!anythingPatched) Log.Warning($"No Unity RNG was patched for method: {original?.FullDescription() ?? "(unknown method)"}");
        }
    }

    [HarmonyPatch(typeof(Pawn_RecordsTracker), nameof(Pawn_RecordsTracker.ExposeData))]
    static class RecordsTrackerExposePatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            var battleActiveField =
                AccessTools.Field(typeof(Pawn_RecordsTracker), nameof(Pawn_RecordsTracker.battleActive));

            foreach (var inst in insts)
            {
                // Remove mutation of battleActive during saving which was a source of non-determinism
                if (inst.opcode == OpCodes.Stfld && inst.operand == battleActiveField)
                {
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return new CodeInstruction(OpCodes.Pop);
                }
                else
                    yield return inst;
            }
        }
    }

    [HarmonyPatch(typeof(SituationalThoughtHandler), nameof(SituationalThoughtHandler.CheckRecalculateSocialThoughts))]
    static class DontRecalculateSocialThoughtsInInterface
    {
        static bool Prefix(SituationalThoughtHandler __instance, Pawn otherPawn)
        {
            if (Multiplayer.Client == null) return true;
            if (Multiplayer.Ticking || Multiplayer.ExecutingCmds) return true;

            // This initializer needs to always run (the method itself begins with it)
            if (!__instance.cachedSocialThoughts.TryGetValue(otherPawn, out var value))
            {
                value = new SituationalThoughtHandler.CachedSocialThoughts();
                __instance.cachedSocialThoughts.Add(otherPawn, value);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(SituationalThoughtHandler), nameof(SituationalThoughtHandler.AppendSocialThoughts))]
    static class DontUpdateThoughQueryTickInInterface
    {
        private static FieldInfo queryTickField = AccessTools.Field(typeof(SituationalThoughtHandler.CachedSocialThoughts), nameof(SituationalThoughtHandler.CachedSocialThoughts.lastQueryTick));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.opcode == OpCodes.Stfld && inst.operand == queryTickField)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DontUpdateThoughQueryTickInInterface), nameof(NewQueryTick)));
                }

                yield return inst;
            }
        }

        private static int NewQueryTick(int ticks, SituationalThoughtHandler thoughtHandler, Pawn otherPawn)
        {
            return Multiplayer.Client == null || Multiplayer.Ticking || Multiplayer.ExecutingCmds ?
                ticks :
                thoughtHandler.cachedSocialThoughts[otherPawn].lastQueryTick;
        }
    }

    [HarmonyPatch(typeof(SituationalThoughtHandler), nameof(SituationalThoughtHandler.CheckRecalculateMoodThoughts))]
    static class DontRecalculateMoodThoughtsInInterface
    {
        static bool Prefix(SituationalThoughtHandler __instance)
        {
            if (Multiplayer.Client != null && !Multiplayer.Ticking && !Multiplayer.ExecutingCmds) return false;

            // Notify_SituationalThoughtsDirty was called
            if (__instance.lastMoodThoughtsRecalculationTick == -99999)
                __instance.cachedThoughts.Clear();

            return true;
        }
    }

    [HarmonyPatch(typeof(SituationalThoughtHandler), nameof(SituationalThoughtHandler.Notify_SituationalThoughtsDirty))]
    static class NotifyThoughtsDirtyPatch
    {
        private static MethodInfo clearMethod =
            AccessTools.Method(typeof(List<Thought_Situational>), nameof(List<Thought_Situational>.Clear));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.operand == clearMethod)
                    yield return new CodeInstruction(OpCodes.Pop);
                else
                    yield return inst;
            }
        }
    }

    [HarmonyPatch(typeof(PawnCapacitiesHandler), nameof(PawnCapacitiesHandler.GetLevel))]
    static class PawnCapacitiesHandlerGetLevelPatch
    {
        private static readonly PawnCapacitiesHandler.CacheStatus CachedInInterface = (PawnCapacitiesHandler.CacheStatus)3;

        private static FieldInfo statusField = AccessTools.Field(typeof(PawnCapacitiesHandler.CacheElement),
            nameof(PawnCapacitiesHandler.CacheElement.status));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            var matcher = new CodeMatcher(insts);

            // Modify cache update checking
            matcher.MatchEndForward(
                new CodeMatch(OpCodes.Ldfld, statusField),
                new CodeMatch(OpCodes.Brtrue_S)
            ).Insert(
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(PawnCapacitiesHandlerGetLevelPatch), nameof(ShouldUpdateCache))),
                new CodeInstruction(OpCodes.Ldc_I4_0),
                new CodeInstruction(OpCodes.Ceq)
            );

            // Modify status setter
            matcher.MatchEndForward(
                new CodeMatch(OpCodes.Ldc_I4_2),
                new CodeMatch(OpCodes.Stfld, statusField)
            ).Insert(
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(PawnCapacitiesHandlerGetLevelPatch), nameof(NewCacheStatus)))
            );

            return matcher.codes;
        }

        private static bool ShouldUpdateCache(PawnCapacitiesHandler.CacheStatus status)
        {
            return status == PawnCapacitiesHandler.CacheStatus.Uncached || !Multiplayer.InInterface && status == CachedInInterface;
        }

        private static PawnCapacitiesHandler.CacheStatus NewCacheStatus(PawnCapacitiesHandler.CacheStatus _)
        {
            return Multiplayer.InInterface ? CachedInInterface : PawnCapacitiesHandler.CacheStatus.Cached;
        }
    }

    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValue), typeof(Thing), typeof(bool), typeof(int))]
    static class StatWorkerGetValuePatch
    {
        private static readonly PawnCapacitiesHandler.CacheStatus CachedInInterface = (PawnCapacitiesHandler.CacheStatus)3;

        private static FieldInfo statusField = AccessTools.Field(typeof(PawnCapacitiesHandler.CacheElement),
            nameof(PawnCapacitiesHandler.CacheElement.status));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            var matcher = new CodeMatcher(insts);

            // Modify cache update checking
            matcher.MatchEndForward(
                new CodeMatch(OpCodes.Callvirt, typeof(Dictionary<Thing, StatCacheEntry>).GetMethod("TryGetValue"))
            ).Advance(1).Insert(
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(StatWorkerGetValuePatch), nameof(HasValueInCache)))
            );

            matcher.MatchEndForward(
                new CodeMatch(OpCodes.Ldfld, typeof(StatCacheEntry).GetField(nameof(StatCacheEntry.gameTick)))
            ).Advance(1).Insert(
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(Math), nameof(Math.Abs), new[] { typeof(int) }))
            );

            // Modify status setter
            matcher.MatchEndForward(
                new CodeMatch(OpCodes.Newobj)
            ).Advance(1).Insert(
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(StatWorkerGetValuePatch), nameof(NewCacheStatusCtor)))
            );

            // Modify status setter
            matcher.MatchEndForward(
                    new CodeMatch(OpCodes.Stfld, typeof(StatCacheEntry).GetField(nameof(StatCacheEntry.gameTick)))
            ).Insert(
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(StatWorkerGetValuePatch), nameof(NewCacheStatus)))
            );

            return matcher.codes;
        }

        private static bool HasValueInCache(bool hasValueInCache, StatWorker worker, Thing t)
        {
            var simulating = !Multiplayer.InInterface;
            return hasValueInCache && !(simulating && worker.temporaryStatCache[t].gameTick < 0);
        }

        private static StatCacheEntry NewCacheStatusCtor(StatCacheEntry entry)
        {
            entry.gameTick = NewCacheStatus(entry.gameTick);
            return entry;
        }

        private static int NewCacheStatus(int gameTick)
        {
            return Multiplayer.InInterface ? -gameTick : gameTick;
        }
    }

}
