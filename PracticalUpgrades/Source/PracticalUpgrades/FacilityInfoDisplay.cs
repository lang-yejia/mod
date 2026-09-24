using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PracticalUpgrades
{
    [HarmonyPatch(typeof(CompAffectedByFacilities), nameof(CompAffectedByFacilities.GetStatsExplanation))]
    public static class CompAffectedByFacilities_GetStatsExplanation_UpgradeLevels_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            CompAffectedByFacilities __instance,
            StatDef stat,
            StringBuilder sb,
            string whitespace)
        {
            if (stat != StatDefOf.WorkTableWorkSpeedFactor || !HasActiveUpgradeableFacility(__instance))
            {
                return true;
            }

            List<FacilityExplanationGroup> groups = BuildGroups(__instance, stat);
            if (groups.Count == 0)
            {
                return false;
            }

            sb.AppendLine();
            sb.AppendLine(whitespace + "StatsReport_Facilities".Translate() + ":");
            for (int i = 0; i < groups.Count; i++)
            {
                FacilityExplanationGroup group = groups[i];
                sb.Append(whitespace + "    ");
                if (group.count > 1)
                {
                    sb.Append(group.count + "x ");
                }

                sb.AppendLine(group.label + ": "
                    + group.totalOffset.ToStringByStyle(stat.toStringStyle, ToStringNumberSense.Offset));
            }

            return false;
        }

        private static bool HasActiveUpgradeableFacility(CompAffectedByFacilities affected)
        {
            List<Thing> facilities = affected.LinkedFacilitiesListForReading;
            for (int i = 0; i < facilities.Count; i++)
            {
                Thing facility = facilities[i];
                if (facility.TryGetComp<CompUpgradeableFacility>() != null && affected.IsFacilityActive(facility))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<FacilityExplanationGroup> BuildGroups(
            CompAffectedByFacilities affected,
            StatDef stat)
        {
            List<FacilityExplanationGroup> groups = new List<FacilityExplanationGroup>();
            List<Thing> facilities = affected.LinkedFacilitiesListForReading;
            for (int i = 0; i < facilities.Count; i++)
            {
                Thing facility = facilities[i];
                if (!affected.IsFacilityActive(facility))
                {
                    continue;
                }

                CompFacility facilityComp = facility.TryGetComp<CompFacility>();
                List<StatModifier> offsets = facilityComp?.StatOffsets;
                if (offsets == null)
                {
                    continue;
                }

                float offset = StatUtility.GetStatOffsetFromList(offsets, stat);
                if (offset == 0f)
                {
                    continue;
                }

                CompUpgradeableFacility upgradeComp = facilityComp as CompUpgradeableFacility;
                string key;
                string label;
                if (upgradeComp?.CurrentLevel != null)
                {
                    key = "upgrade:" + facility.def.defName + ":" + upgradeComp.UpgradeLevel;
                    label = upgradeComp.CurrentLevel.Label.CapitalizeFirst();
                }
                else
                {
                    key = "def:" + facility.def.defName;
                    label = facility.LabelCap;
                }

                int groupIndex = groups.FindIndex(group => group.key == key);
                if (groupIndex >= 0)
                {
                    FacilityExplanationGroup group = groups[groupIndex];
                    group.count++;
                    group.totalOffset += offset;
                    groups[groupIndex] = group;
                }
                else
                {
                    groups.Add(new FacilityExplanationGroup
                    {
                        key = key,
                        label = label,
                        count = 1,
                        totalOffset = offset
                    });
                }
            }

            return groups;
        }

        private struct FacilityExplanationGroup
        {
            public string key;
            public string label;
            public int count;
            public float totalOffset;
        }
    }

    public static class FacilityInfoUtility
    {
        private const float CombinedEffectCap = 0.20f;

        public static float WorkSpeedOffsetFor(CompUpgradeableFacility comp)
        {
            List<StatModifier> offsets = comp?.CurrentLevel?.statOffsets;
            if (offsets == null)
            {
                return 0f;
            }

            for (int i = 0; i < offsets.Count; i++)
            {
                if (offsets[i].stat == StatDefOf.WorkTableWorkSpeedFactor)
                {
                    return offsets[i].value;
                }
            }

            return 0f;
        }

        public static List<CompUpgradeableFacility> ActiveUpgradeFacilities(Thing workTable)
        {
            List<CompUpgradeableFacility> result = new List<CompUpgradeableFacility>();
            CompAffectedByFacilities affected = workTable?.TryGetComp<CompAffectedByFacilities>();
            if (affected == null)
            {
                return result;
            }

            List<Thing> facilities = affected.LinkedFacilitiesListForReading;
            for (int i = 0; i < facilities.Count; i++)
            {
                Thing facility = facilities[i];
                CompUpgradeableFacility comp = facility.TryGetComp<CompUpgradeableFacility>();
                if (comp?.CurrentLevel != null && affected.IsFacilityActive(facility))
                {
                    result.Add(comp);
                }
            }

            return result;
        }

        public static StatDrawEntry Entry(string label, string value, string report, int priority)
        {
            return new StatDrawEntry(
                StatCategoryDefOf.Building,
                label,
                value,
                report,
                priority,
                null,
                null,
                false,
                false);
        }

        public static string SignedPercent(float value)
        {
            return (value > 0f ? "+" : string.Empty) + value.ToStringPercent("F0");
        }

        public static string CabinetReport(CompUpgradeableFacility comp)
        {
            FacilityUpgradeLevel level = comp.CurrentLevel;
            return "PU_CabinetSummaryReport".Translate(
                level.Label,
                SignedPercent(WorkSpeedOffsetFor(comp)),
                SignedPercent(level.materialRecoveryChance),
                SignedPercent(level.qualityShiftChance));
        }

        public static string CabinetMaterialReport(CompUpgradeableFacility comp)
        {
            return CabinetReport(comp) + "\n\n" + "PU_MaterialRecoveryRule".Translate(CombinedEffectCap.ToStringPercent("F0"));
        }

        public static string CabinetQualityReport(CompUpgradeableFacility comp)
        {
            return CabinetReport(comp) + "\n\n" + "PU_QualityShiftRule".Translate(CombinedEffectCap.ToStringPercent("F0"));
        }

        public static string WorkTableReport(List<CompUpgradeableFacility> facilities, bool materialReport)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("PU_WorkTableEffectsIntro".Translate());
            report.AppendLine();
            report.AppendLine("PU_EffectSources".Translate());

            for (int i = 0; i < facilities.Count; i++)
            {
                CompUpgradeableFacility comp = facilities[i];
                FacilityUpgradeLevel level = comp.CurrentLevel;
                report.AppendLine("PU_EffectSourceLine".Translate(
                    level.Label,
                    SignedPercent(WorkSpeedOffsetFor(comp)),
                    SignedPercent(level.materialRecoveryChance),
                    SignedPercent(level.qualityShiftChance)));
            }

            report.AppendLine();
            report.Append(materialReport
                ? "PU_MaterialRecoveryRule".Translate(CombinedEffectCap.ToStringPercent("F0"))
                : "PU_QualityShiftRule".Translate(CombinedEffectCap.ToStringPercent("F0")));
            return report.ToString();
        }
    }

    [HarmonyPatch(typeof(ThingDef), nameof(ThingDef.SpecialDisplayStats))]
    public static class ThingDef_SpecialDisplayStats_UpgradeEffects_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(StatRequest req, ref IEnumerable<StatDrawEntry> __result)
        {
            if (!req.HasThing)
            {
                return;
            }

            List<CompUpgradeableFacility> facilities = FacilityInfoUtility.ActiveUpgradeFacilities(req.Thing);
            if (facilities.Count == 0)
            {
                return;
            }

            __result = AppendUpgradeEffects(__result, facilities);
        }

        private static IEnumerable<StatDrawEntry> AppendUpgradeEffects(
            IEnumerable<StatDrawEntry> original,
            List<CompUpgradeableFacility> facilities)
        {
            if (original != null)
            {
                foreach (StatDrawEntry entry in original)
                {
                    yield return entry;
                }
            }

            float materialRecovery = 0f;
            float qualityShift = 0f;
            for (int i = 0; i < facilities.Count; i++)
            {
                FacilityUpgradeLevel level = facilities[i].CurrentLevel;
                materialRecovery += level.materialRecoveryChance;
                qualityShift += level.qualityShiftChance;
            }

            materialRecovery = UnityEngine.Mathf.Min(materialRecovery, 0.20f);
            qualityShift = UnityEngine.Mathf.Min(qualityShift, 0.20f);

            yield return FacilityInfoUtility.Entry(
                "PU_InfoWorkTableMaterialRecovery".Translate(),
                FacilityInfoUtility.SignedPercent(materialRecovery),
                FacilityInfoUtility.WorkTableReport(facilities, true),
                2750);
            yield return FacilityInfoUtility.Entry(
                "PU_InfoWorkTableQualityShift".Translate(),
                FacilityInfoUtility.SignedPercent(qualityShift),
                FacilityInfoUtility.WorkTableReport(facilities, false),
                2740);
        }
    }
}
