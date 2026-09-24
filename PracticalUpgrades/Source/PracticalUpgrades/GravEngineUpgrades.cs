using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace PracticalUpgrades
{
    public class GravEngineUpgradeLevel
    {
        public string labelKey;
        public ThingDef moduleDef;
        public ResearchProjectDef requiredResearch;
        public float installWork;
        public int constructionSkill;
        public int gravFieldExtenderLimit;
        public int smallThrusterLimit;
        public int largeThrusterLimit;
        public float powerOutput;
        public float energyCapacity;
        public float energyRechargePerDay;
        public float maxFuelOffsetFraction;

        public string Label => labelKey.NullOrEmpty() ? "Unnamed grav engine upgrade" : labelKey.Translate();
    }

    public class CompProperties_UpgradeableGravEngine : CompProperties
    {
        public List<GravEngineUpgradeLevel> upgradeLevels;

        public CompProperties_UpgradeableGravEngine()
        {
            compClass = typeof(CompUpgradeableGravEngine);
        }

        public GravEngineUpgradeLevel LevelAt(int level)
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
                yield return parentDef.defName + " requires a base grav engine level and at least one upgrade level.";
                yield break;
            }

            for (int i = 1; i < upgradeLevels.Count; i++)
            {
                GravEngineUpgradeLevel level = upgradeLevels[i];
                if (level == null || level.moduleDef == null)
                {
                    yield return parentDef.defName + " grav engine upgrade level " + i + " has no moduleDef.";
                }
            }
        }
    }

    public class CompUpgradeableGravEngine : ThingComp, IUpgradeableBuilding
    {
        private static readonly MethodInfo RelinkAllMethod = AccessTools.Method(typeof(CompAffectedByFacilities), "RelinkAll");

        private int upgradeLevel;
        private float upgradeWorkDone;
        private float gravEnergy;
        private bool autoUseGravEnergy = true;
        private bool slotLimitsInitialized;
        private int grandfatheredExtenderLimit;
        private int grandfatheredSmallThrusterLimit;
        private int grandfatheredLargeThrusterLimit;

        public CompProperties_UpgradeableGravEngine Props => (CompProperties_UpgradeableGravEngine)props;
        public Building_GravEngine Engine => parent as Building_GravEngine;
        public Thing ParentThing => parent;
        public int UpgradeLevel => upgradeLevel;
        public GravEngineUpgradeLevel CurrentLevel => Props.LevelAt(upgradeLevel);
        public GravEngineUpgradeLevel NextLevel => IsMaxLevel ? null : Props.LevelAt(upgradeLevel + 1);
        public ThingDef NextModuleDef => NextLevel?.moduleDef;
        public bool IsMaxLevel => Props.upgradeLevels.NullOrEmpty() || upgradeLevel >= Props.upgradeLevels.Count - 1;
        public float GravEnergy => gravEnergy;
        public bool AutoUseGravEnergy => autoUseGravEnergy;

        public float UpgradeProgress
        {
            get
            {
                GravEngineUpgradeLevel next = NextLevel;
                return next == null || next.installWork <= 0f ? 0f : Mathf.Clamp01(upgradeWorkDone / next.installWork);
            }
        }

        public bool InstallationComplete => NextLevel != null && upgradeWorkDone >= NextLevel.installWork;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref upgradeLevel, "puGravEngineUpgradeLevel", 0);
            Scribe_Values.Look(ref upgradeWorkDone, "puGravEngineUpgradeWorkDone", 0f);
            Scribe_Values.Look(ref gravEnergy, "puGravEngineEnergy", 0f);
            Scribe_Values.Look(ref autoUseGravEnergy, "puAutoUseGravEngineEnergy", true);
            Scribe_Values.Look(ref slotLimitsInitialized, "puGravSlotLimitsInitialized", false);
            Scribe_Values.Look(ref grandfatheredExtenderLimit, "puGrandfatheredExtenderLimit", 0);
            Scribe_Values.Look(ref grandfatheredSmallThrusterLimit, "puGrandfatheredSmallThrusterLimit", 0);
            Scribe_Values.Look(ref grandfatheredLargeThrusterLimit, "puGrandfatheredLargeThrusterLimit", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && !Props.upgradeLevels.NullOrEmpty())
            {
                upgradeLevel = Mathf.Clamp(upgradeLevel, 0, Props.upgradeLevels.Count - 1);
                gravEnergy = Mathf.Clamp(gravEnergy, 0f, CurrentLevel?.energyCapacity ?? 0f);
                if (IsMaxLevel)
                {
                    upgradeWorkDone = 0f;
                }
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (slotLimitsInitialized || Engine?.AffectedByFacilities == null)
            {
                return;
            }

            List<Thing> linked = Engine.AffectedByFacilities.LinkedFacilitiesListForReading;
            grandfatheredExtenderLimit = CountLinked(linked, "GravFieldExtender");
            grandfatheredSmallThrusterLimit = CountLinked(linked, "SmallThruster");
            grandfatheredLargeThrusterLimit = CountLinked(linked, "LargeThruster");
            slotLimitsInitialized = true;
        }

        public override void CompTick()
        {
            base.CompTick();
            GravEngineUpgradeLevel level = CurrentLevel;
            if (level == null || level.energyCapacity <= 0f || gravEnergy >= level.energyCapacity
                || !parent.Spawned || parent.Faction != Faction.OfPlayer)
            {
                return;
            }

            gravEnergy = Mathf.Min(level.energyCapacity, gravEnergy + level.energyRechargePerDay / GenDate.TicksPerDay);
        }

        public override string TransformLabel(string label)
        {
            return upgradeLevel <= 0 || CurrentLevel == null ? base.TransformLabel(label) : CurrentLevel.Label;
        }

        public override string CompInspectStringExtra()
        {
            GravEngineUpgradeLevel level = CurrentLevel;
            if (level == null)
            {
                return null;
            }

            string text = "PU_GravUpgradeLevel".Translate(level.Label)
                + "\n" + "PU_GravSlotLimits".Translate(
                    level.gravFieldExtenderLimit,
                    level.smallThrusterLimit,
                    level.largeThrusterLimit)
                + "\n" + "PU_GravPowerOutput".Translate(level.powerOutput.ToString("F0"))
                + "\n" + "PU_GravEnergyInspect".Translate(
                    gravEnergy.ToString("F1"),
                    level.energyCapacity.ToString("F0"),
                    level.energyRechargePerDay.ToString("F0"),
                    level.maxFuelOffsetFraction.ToStringPercent("F0"));

            if (HasUpgradeDesignation() && NextLevel != null)
            {
                text += "\n" + "PU_InstallProgress".Translate(UpgradeProgress.ToStringPercent());
            }

            return text;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            GravEngineUpgradeLevel level = CurrentLevel;
            if (parent.Spawned && parent.Faction == Faction.OfPlayer && level != null && level.energyCapacity > 0f)
            {
                yield return new Gizmo_GravEngineEnergy(this);
                yield return new Command_Toggle
                {
                    defaultLabel = "PU_AutoUseGravEnergyLabel".Translate(),
                    defaultDesc = "PU_AutoUseGravEnergyDesc".Translate(),
                    icon = TexCommand.ForbidOff,
                    isActive = () => autoUseGravEnergy,
                    toggleAction = () => autoUseGravEnergy = !autoUseGravEnergy
                };
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

            GravEngineUpgradeLevel next = NextLevel;
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

            if (next.requiredResearch != null && !next.requiredResearch.IsFinished)
            {
                command.Disable("PU_ResearchRequired".Translate(next.requiredResearch.LabelCap));
            }

            yield return command;
        }

        public bool HasUpgradeDesignation()
        {
            return parent.Spawned
                && parent.Map.designationManager.DesignationOn(parent, PUDesignationDefOf.PU_UpgradeFacility) != null;
        }

        public bool CanInstallNextLevel(Pawn pawn, bool reportFailure)
        {
            GravEngineUpgradeLevel next = NextLevel;
            if (next == null || !parent.Spawned || parent.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (next.requiredResearch != null && !next.requiredResearch.IsFinished)
            {
                if (reportFailure)
                {
                    JobFailReason.Is("PU_ResearchRequired".Translate(next.requiredResearch.LabelCap));
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

        public void CompleteUpgrade()
        {
            if (IsMaxLevel)
            {
                return;
            }

            upgradeLevel++;
            upgradeWorkDone = 0f;
            gravEnergy = Mathf.Min(gravEnergy, CurrentLevel.energyCapacity);

            CompAffectedByFacilities affected = Engine?.AffectedByFacilities;
            if (affected != null)
            {
                RelinkAllMethod?.Invoke(affected, null);
            }
            parent.TryGetComp<CompGravEnginePower>()?.UpdateDesiredPowerOutput();

            if (parent.Spawned)
            {
                parent.Map.designationManager.TryRemoveDesignationOn(parent, PUDesignationDefOf.PU_UpgradeFacility);
                Messages.Message("PU_UpgradeComplete".Translate(parent.LabelCap, CurrentLevel.Label), parent, MessageTypeDefOf.PositiveEvent);
            }
        }

        public int SlotLimitFor(ThingDef facilityDef)
        {
            GravEngineUpgradeLevel level = CurrentLevel;
            if (level == null || facilityDef == null)
            {
                return -1;
            }

            if (!slotLimitsInitialized)
            {
                return int.MaxValue;
            }

            switch (facilityDef.defName)
            {
                case "GravFieldExtender": return Mathf.Max(level.gravFieldExtenderLimit, grandfatheredExtenderLimit);
                case "SmallThruster": return Mathf.Max(level.smallThrusterLimit, grandfatheredSmallThrusterLimit);
                case "LargeThruster": return Mathf.Max(level.largeThrusterLimit, grandfatheredLargeThrusterLimit);
                default: return -1;
            }
        }

        private static int CountLinked(List<Thing> linked, string defName)
        {
            int count = 0;
            for (int i = 0; i < linked.Count; i++)
            {
                if (linked[i].def.defName == defName)
                {
                    count++;
                }
            }
            return count;
        }

        public void NotifyGrandfatheredFacilityRemoved(ThingDef facilityDef)
        {
            GravEngineUpgradeLevel level = CurrentLevel;
            if (!slotLimitsInitialized || level == null || facilityDef == null)
            {
                return;
            }

            switch (facilityDef.defName)
            {
                case "GravFieldExtender":
                    grandfatheredExtenderLimit = Mathf.Max(level.gravFieldExtenderLimit, grandfatheredExtenderLimit - 1);
                    break;
                case "SmallThruster":
                    grandfatheredSmallThrusterLimit = Mathf.Max(level.smallThrusterLimit, grandfatheredSmallThrusterLimit - 1);
                    break;
                case "LargeThruster":
                    grandfatheredLargeThrusterLimit = Mathf.Max(level.largeThrusterLimit, grandfatheredLargeThrusterLimit - 1);
                    break;
            }
        }

        public float EffectiveEnergyForFuel(float actualFuel)
        {
            GravEngineUpgradeLevel level = CurrentLevel;
            if (!autoUseGravEnergy || level == null || actualFuel <= 0f || gravEnergy <= 0f
                || level.maxFuelOffsetFraction <= 0f)
            {
                return 0f;
            }

            float fraction = Mathf.Min(level.maxFuelOffsetFraction, 0.99f);
            return Mathf.Min(gravEnergy, actualFuel * fraction / (1f - fraction));
        }

        public void ConsumeGravEnergy(float amount)
        {
            gravEnergy = Mathf.Max(0f, gravEnergy - Mathf.Max(0f, amount));
        }
    }

    public class CompProperties_GravEnginePower : CompProperties_Power
    {
        public CompProperties_GravEnginePower()
        {
            compClass = typeof(CompGravEnginePower);
            transmitsPower = true;
        }
    }

    public class CompGravEnginePower : CompPowerPlant
    {
        protected override float DesiredPowerOutput => parent.TryGetComp<CompUpgradeableGravEngine>()?.CurrentLevel?.powerOutput ?? 0f;
    }

    public class Gizmo_GravEngineEnergy : Gizmo_Slider
    {
        private readonly CompUpgradeableGravEngine comp;
        private bool dragging;

        public Gizmo_GravEngineEnergy(CompUpgradeableGravEngine comp)
        {
            this.comp = comp;
        }

        protected override bool DraggingBar
        {
            get => dragging;
            set => dragging = value;
        }

        protected override float Target
        {
            get => ValuePercent;
            set { }
        }

        protected override string Title => "PU_GravEnergyTitle".Translate();
        protected override float ValuePercent => comp.CurrentLevel.energyCapacity <= 0f
            ? 0f
            : comp.GravEnergy / comp.CurrentLevel.energyCapacity;
        protected override bool IsDraggable => false;
        protected override string BarLabel => "PU_GravEnergyBar".Translate(
            comp.GravEnergy.ToString("F0"),
            comp.CurrentLevel.energyCapacity.ToString("F0"));
        protected override string GetTooltip()
        {
            return "PU_GravEnergyTooltip".Translate(
                comp.CurrentLevel.energyRechargePerDay.ToString("F0"),
                comp.CurrentLevel.maxFuelOffsetFraction.ToStringPercent("F0"));
        }
    }

    [HarmonyPatch(typeof(CompAffectedByFacilities), "CanLinkTo")]
    public static class GravEngineFacilitySlotLimitPatch
    {
        public static void Postfix(CompAffectedByFacilities __instance, Thing facility, ref bool __result)
        {
            if (!__result || !(__instance.parent is Building_GravEngine))
            {
                return;
            }

            CompUpgradeableGravEngine upgrade = __instance.parent.TryGetComp<CompUpgradeableGravEngine>();
            int limit = upgrade?.SlotLimitFor(facility.def) ?? -1;
            if (limit < 0)
            {
                return;
            }

            int count = 0;
            List<Thing> linked = __instance.LinkedFacilitiesListForReading;
            for (int i = 0; i < linked.Count; i++)
            {
                if (linked[i].def == facility.def)
                {
                    count++;
                }
            }

            __result = count < limit;
        }
    }

    [HarmonyPatch(typeof(CompGravshipFacility), nameof(CompGravshipFacility.PostDeSpawn))]
    public static class GravEngineGrandfatheredFacilityRemovalPatch
    {
        public static void Prefix(CompGravshipFacility __instance, DestroyMode mode)
        {
            if (mode == DestroyMode.Vanish || mode == DestroyMode.WillReplace)
            {
                return;
            }

            Building_GravEngine engine = AccessTools.Field(typeof(CompGravshipFacility), "engine")
                ?.GetValue(__instance) as Building_GravEngine;
            engine?.TryGetComp<CompUpgradeableGravEngine>()?.NotifyGrandfatheredFacilityRemoved(__instance.parent.def);
        }
    }

    [HarmonyPatch(typeof(Building_GravEngine), "ConsumeFuel")]
    public static class GravEngineConsumeFuelEnergyPatch
    {
        public sealed class FuelState
        {
            public readonly List<CompRefuelable> tanks = new List<CompRefuelable>();
            public readonly List<float> fuelBefore = new List<float>();
            public float actualFuelBefore;
            public float effectiveEnergyBefore;
        }

        public static void Prefix(Building_GravEngine __instance, out FuelState __state)
        {
            __state = new FuelState();
            List<CompGravshipFacility> components = __instance.GravshipComponents;
            for (int i = 0; i < components.Count; i++)
            {
                CompGravshipFacility facility = components[i];
                if (!facility.CanBeActive || !facility.Props.providesFuel)
                {
                    continue;
                }

                CompRefuelable tank = facility.parent.GetComp<CompRefuelable>();
                if (tank != null)
                {
                    __state.tanks.Add(tank);
                    __state.fuelBefore.Add(tank.Fuel);
                    __state.actualFuelBefore += tank.Fuel;
                }
            }

            __state.effectiveEnergyBefore = __instance.TryGetComp<CompUpgradeableGravEngine>()
                ?.EffectiveEnergyForFuel(__state.actualFuelBefore) ?? 0f;
        }

        public static void Postfix(Building_GravEngine __instance, FuelState __state)
        {
            CompUpgradeableGravEngine upgrade = __instance.TryGetComp<CompUpgradeableGravEngine>();
            if (upgrade == null || __state == null || __state.tanks.Count == 0)
            {
                return;
            }

            float consumed = 0f;
            for (int i = 0; i < __state.tanks.Count; i++)
            {
                consumed += Mathf.Max(0f, __state.fuelBefore[i] - __state.tanks[i].Fuel);
            }

            if (__state.actualFuelBefore <= 0f || __state.effectiveEnergyBefore <= 0f || consumed <= 0f)
            {
                return;
            }

            float energyUsed = Mathf.Min(
                __state.effectiveEnergyBefore,
                consumed * __state.effectiveEnergyBefore / __state.actualFuelBefore);
            upgrade.ConsumeGravEnergy(energyUsed);

            Messages.Message(
                "PU_GravEnergyFuelSaved".Translate(energyUsed.ToString("F1"), consumed.ToString("F1")),
                MessageTypeDefOf.PositiveEvent,
                false);
        }
    }

    [HarmonyPatch(typeof(Building_GravEngine), "get_TotalFuel")]
    public static class GravEngineTotalFuelEnergyPatch
    {
        public static void Postfix(Building_GravEngine __instance, ref float __result)
        {
            CompUpgradeableGravEngine upgrade = __instance.TryGetComp<CompUpgradeableGravEngine>();
            if (upgrade != null)
            {
                __result += upgrade.EffectiveEnergyForFuel(__result);
            }
        }
    }
}
