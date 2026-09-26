using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace PracticalUpgrades
{
    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.Print), new Type[] { typeof(SectionLayer) })]
    public static class ThingWithComps_Print_UpgradeOverlay_Patch
    {
        private static ThingDef toolCabinetDef;

        public static void Postfix(ThingWithComps __instance, SectionLayer layer)
        {
            toolCabinetDef = toolCabinetDef ?? DefDatabase<ThingDef>.GetNamedSilentFail("ToolCabinet");
            if (__instance.def == toolCabinetDef)
            {
                __instance.TryGetComp<CompUpgradeableFacility>()?.PrintUpgradeOverlay(layer);
            }
        }
    }

    public class FacilityUpgradeLevel
    {
        public string labelKey;
        public ThingDef moduleDef;
        public float installWork;
        public int constructionSkill;
        public float materialRecoveryChance;
        public float qualityShiftChance;
        public GraphicData overlayGraphicData;
        public List<StatModifier> statOffsets;

        public string Label => labelKey.NullOrEmpty() ? "Unnamed upgrade" : labelKey.Translate();
    }

    public class CompProperties_UpgradeableFacility : CompProperties_Facility
    {
        public ResearchProjectDef requiredResearch;
        public List<FacilityUpgradeLevel> upgradeLevels;

        public CompProperties_UpgradeableFacility()
        {
            compClass = typeof(CompUpgradeableFacility);
        }

        public FacilityUpgradeLevel LevelAt(int level)
        {
            if (upgradeLevels.NullOrEmpty())
            {
                return null;
            }

            return upgradeLevels[Mathf.Clamp(level, 0, upgradeLevels.Count - 1)];
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }

            if (upgradeLevels.NullOrEmpty() || upgradeLevels.Count < 2)
            {
                yield return parentDef.defName + " requires at least a base and one upgraded facility level.";
                yield break;
            }

            for (int i = 0; i < upgradeLevels.Count; i++)
            {
                FacilityUpgradeLevel level = upgradeLevels[i];
                if (level == null)
                {
                    yield return parentDef.defName + " has a null facility upgrade level at index " + i + ".";
                    continue;
                }

                if (i > 0 && level.moduleDef == null)
                {
                    yield return parentDef.defName + " facility upgrade level " + i + " has no moduleDef.";
                }
            }
        }
    }

    public class CompUpgradeableFacility : CompFacility, IUpgradeableBuilding
    {
        private static readonly HashSet<int> LoggedOverlayLevels = new HashSet<int>();

        private int upgradeLevel;
        private float upgradeWorkDone;

        public new CompProperties_UpgradeableFacility Props => (CompProperties_UpgradeableFacility)props;

        public int UpgradeLevel => upgradeLevel;

        public Thing ParentThing => parent;

        public ThingDef NextModuleDef => NextLevel?.moduleDef;

        public FacilityUpgradeLevel CurrentLevel => Props.LevelAt(upgradeLevel);

        public FacilityUpgradeLevel NextLevel => IsMaxLevel ? null : Props.LevelAt(upgradeLevel + 1);

        public bool IsMaxLevel => Props.upgradeLevels.NullOrEmpty() || upgradeLevel >= Props.upgradeLevels.Count - 1;

        public float UpgradeProgress
        {
            get
            {
                FacilityUpgradeLevel next = NextLevel;
                return next == null || next.installWork <= 0f ? 0f : Mathf.Clamp01(upgradeWorkDone / next.installWork);
            }
        }

        public override List<StatModifier> StatOffsets
        {
            get
            {
                FacilityUpgradeLevel current = CurrentLevel;
                return current?.statOffsets ?? base.StatOffsets;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref upgradeLevel, "upgradeLevel", 0);
            Scribe_Values.Look(ref upgradeWorkDone, "upgradeWorkDone", 0f);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && !Props.upgradeLevels.NullOrEmpty())
            {
                upgradeLevel = Mathf.Clamp(upgradeLevel, 0, Props.upgradeLevels.Count - 1);
                if (IsMaxLevel)
                {
                    upgradeWorkDone = 0f;
                }
            }
        }

        public override string TransformLabel(string label)
        {
            return upgradeLevel <= 0 || CurrentLevel == null ? base.TransformLabel(label) : CurrentLevel.Label;
        }

        public void PrintUpgradeOverlay(SectionLayer layer)
        {
            Graphic overlay = CurrentLevel?.overlayGraphicData?.Graphic;
            if (upgradeLevel <= 0 || overlay == null)
            {
                return;
            }

            overlay.Print(layer, parent, 0f);

            if (LoggedOverlayLevels.Add(upgradeLevel))
            {
                Log.Message("[Practical Upgrades] Drew tool cabinet level " + upgradeLevel
                    + " overlay from " + CurrentLevel.overlayGraphicData.texPath + ".");
            }
        }

        public override string CompInspectStringExtra()
        {
            string result = base.CompInspectStringExtra();
            string details = "PU_UpgradeLevel".Translate(CurrentLevel?.Label ?? "-");

            if (CurrentLevel != null)
            {
                details += "\n" + "PU_WorkSpeedBonus".Translate(
                    FacilityInfoUtility.SignedPercent(FacilityInfoUtility.WorkSpeedOffsetFor(this)));
            }

            if (CurrentLevel != null && CurrentLevel.materialRecoveryChance > 0f)
            {
                details += "\n" + "PU_MaterialRecovery".Translate(CurrentLevel.materialRecoveryChance.ToStringPercent());
            }

            if (CurrentLevel != null && CurrentLevel.qualityShiftChance > 0f)
            {
                details += "\n" + "PU_QualityShift".Translate(CurrentLevel.qualityShiftChance.ToStringPercent());
            }

            if (HasUpgradeDesignation() && NextLevel != null)
            {
                details += "\n" + "PU_InstallProgress".Translate(UpgradeProgress.ToStringPercent());
            }

            return result.NullOrEmpty() ? details : result + "\n" + details;
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
        {
            IEnumerable<StatDrawEntry> baseEntries = base.SpecialDisplayStats();
            if (baseEntries != null)
            {
                foreach (StatDrawEntry entry in baseEntries)
                {
                    yield return entry;
                }
            }

            FacilityUpgradeLevel level = CurrentLevel;
            if (level == null)
            {
                yield break;
            }

            float workSpeed = FacilityInfoUtility.WorkSpeedOffsetFor(this);
            string sharedReport = FacilityInfoUtility.CabinetReport(this);
            yield return FacilityInfoUtility.Entry(
                "PU_InfoUpgradeLevel".Translate(),
                level.Label,
                sharedReport,
                2790);
            yield return FacilityInfoUtility.Entry(
                "PU_InfoProvidedWorkSpeed".Translate(),
                FacilityInfoUtility.SignedPercent(workSpeed),
                sharedReport,
                2780);
            yield return FacilityInfoUtility.Entry(
                "PU_InfoMaterialRecovery".Translate(),
                FacilityInfoUtility.SignedPercent(level.materialRecoveryChance),
                FacilityInfoUtility.CabinetMaterialReport(this),
                2770);
            yield return FacilityInfoUtility.Entry(
                "PU_InfoQualityShift".Translate(),
                FacilityInfoUtility.SignedPercent(level.qualityShiftChance),
                FacilityInfoUtility.CabinetQualityReport(this),
                2760);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            if (!parent.Spawned || parent.Faction != Faction.OfPlayer || IsMaxLevel)
            {
                yield break;
            }

            Designation designation = parent.Map.designationManager.DesignationOn(parent, PUDesignationDefOf.PU_UpgradeFacility);
            if (designation != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "PU_CancelUpgradeLabel".Translate(),
                    defaultDesc = "PU_CancelUpgradeDesc".Translate(),
                    icon = TexCommand.Install,
                    action = delegate
                    {
                        parent.Map.designationManager.RemoveDesignation(designation);
                        upgradeWorkDone = 0f;
                        SoundDefOf.Designate_Cancel.PlayOneShotOnCamera();
                    }
                };
                yield break;
            }

            FacilityUpgradeLevel next = NextLevel;
            Command_Action command = new Command_Action
            {
                defaultLabel = "PU_InstallUpgradeLabel".Translate(next.Label),
                defaultDesc = "PU_InstallUpgradeDesc".Translate(next.Label),
                icon = TexCommand.Install,
                action = delegate
                {
                    parent.Map.designationManager.AddDesignation(new Designation(parent, PUDesignationDefOf.PU_UpgradeFacility));
                    SoundDefOf.Designate_Haul.PlayOneShotOnCamera();
                }
            };

            if (Props.requiredResearch != null && !Props.requiredResearch.IsFinished)
            {
                command.Disable("PU_ResearchRequired".Translate(Props.requiredResearch.LabelCap));
            }

            yield return command;
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            upgradeWorkDone = 0f;
        }

        public bool HasUpgradeDesignation()
        {
            return parent.Spawned && parent.Map.designationManager.DesignationOn(parent, PUDesignationDefOf.PU_UpgradeFacility) != null;
        }

        public bool CanInstallNextLevel(Pawn pawn, bool reportFailure)
        {
            FacilityUpgradeLevel next = NextLevel;
            if (next == null || !parent.Spawned || parent.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (Props.requiredResearch != null && !Props.requiredResearch.IsFinished)
            {
                if (reportFailure)
                {
                    JobFailReason.Is("PU_ResearchRequired".Translate(Props.requiredResearch.LabelCap));
                }
                return false;
            }

            if (pawn?.skills != null && pawn.skills.GetSkill(SkillDefOf.Construction).Level < next.constructionSkill)
            {
                if (reportFailure)
                {
                    JobFailReason.Is("PU_ConstructionSkillRequired".Translate(next.constructionSkill));
                }
                return false;
            }

            return true;
        }

        public void AddUpgradeWork(float amount)
        {
            upgradeWorkDone += Mathf.Max(0f, amount);
        }

        public bool InstallationComplete => NextLevel != null && upgradeWorkDone >= NextLevel.installWork;

        public void CompleteUpgrade()
        {
            if (IsMaxLevel)
            {
                return;
            }

            string previousLabel = CurrentLevel != null
                ? CurrentLevel.Label.CapitalizeFirst()
                : parent.def.LabelCap.ToString();
            upgradeLevel++;
            upgradeWorkDone = 0f;
            Notify_ThingChanged();

            if (parent.Spawned)
            {
                parent.DirtyMapMesh(parent.Map);
                parent.Map.designationManager.TryRemoveDesignationOn(parent, PUDesignationDefOf.PU_UpgradeFacility);
                Messages.Message("PU_UpgradeComplete".Translate(previousLabel, CurrentLevel.Label), parent, MessageTypeDefOf.PositiveEvent);
            }
        }
    }
}
