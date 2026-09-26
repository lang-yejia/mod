using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorks.Pickle;
using Verse;

namespace PracticalUpgrades.PickleTests
{
    [PickleSteps]
    public sealed class PracticalUpgradeSteps
    {
        private const string HarmonyOwner = "langyejia.practicalupgrades";

        [Then("the practical upgrade definitions are loaded")]
        public void DefinitionsAreLoaded(PickleContext context)
        {
            string[] commonThingDefs =
            {
                "ToolCabinet",
                "PU_PrecisionToolModule",
                "PU_UltraPrecisionToolModule",
                "PU_PersonaToolController"
            };

            foreach (string defName in commonThingDefs)
            {
                context.Assert(DefDatabase<ThingDef>.GetNamedSilentFail(defName) != null, "Missing ThingDef: " + defName);
            }

            context.Assert(
                DefDatabase<ResearchProjectDef>.GetNamedSilentFail("PU_ModularToolSystems") != null,
                "Missing tool cabinet research project.");

            string[] commonRecipes =
            {
                "PU_MakePrecisionToolModule",
                "PU_MakeUltraPrecisionToolModule",
                "PU_MakePersonaToolController"
            };

            foreach (string defName in commonRecipes)
            {
                context.Assert(DefDatabase<RecipeDef>.GetNamedSilentFail(defName) != null, "Missing RecipeDef: " + defName);
            }

            context.Assert(DefDatabase<JobDef>.GetNamedSilentFail("PU_UpgradeFacility") != null, "Missing upgrade JobDef.");
            context.Assert(DefDatabase<DesignationDef>.GetNamedSilentFail("PU_UpgradeFacility") != null, "Missing upgrade DesignationDef.");
            context.Assert(DefDatabase<WorkGiverDef>.GetNamedSilentFail("PU_UpgradeFacility") != null, "Missing upgrade WorkGiverDef.");
        }

        [Then("the tool cabinet upgrade levels match the design")]
        public void ToolCabinetLevelsMatch(PickleContext context)
        {
            ThingDef cabinet = DefDatabase<ThingDef>.GetNamed("ToolCabinet");
            CompProperties_UpgradeableFacility props = cabinet.GetCompProperties<CompProperties_UpgradeableFacility>();
            context.Assert(props != null, "Tool cabinet upgrade comp is missing.");
            context.Assert(props.upgradeLevels.Count == 4, "Tool cabinet should have four total levels.");

            float[] speeds = { 0.06f, 0.10f, 0.15f, 0.25f };
            float[] recovery = { 0f, 0f, 0.05f, 0.10f };
            float[] quality = { 0f, 0f, 0f, 0.10f };
            string[] modules = { null, "PU_PrecisionToolModule", "PU_UltraPrecisionToolModule", "PU_PersonaToolController" };

            for (int i = 0; i < props.upgradeLevels.Count; i++)
            {
                FacilityUpgradeLevel level = props.upgradeLevels[i];
                StatModifier speed = level.statOffsets.FirstOrDefault(modifier => modifier.stat == StatDefOf.WorkTableWorkSpeedFactor);
                context.Assert(speed != null && Nearly(speed.value, speeds[i]), "Unexpected work speed at cabinet level " + i + ".");
                context.Assert(Nearly(level.materialRecoveryChance, recovery[i]), "Unexpected recovery chance at cabinet level " + i + ".");
                context.Assert(Nearly(level.qualityShiftChance, quality[i]), "Unexpected quality chance at cabinet level " + i + ".");
                context.Assert((level.moduleDef == null ? null : level.moduleDef.defName) == modules[i], "Unexpected module at cabinet level " + i + ".");
            }
        }

        [Then("Odyssey definitions and grav engine upgrades are correctly gated")]
        public void OdysseyDefinitionsAreGated(PickleContext context)
        {
            bool odysseyActive = ModsConfig.OdysseyActive;
            ThingDef engine = DefDatabase<ThingDef>.GetNamedSilentFail("GravEngine");
            string[] modules =
            {
                "PU_GravFieldCalibrationAssembly",
                "PU_GravResonanceController",
                "PU_MultiphaseGravCore"
            };
            string[] moduleTextures =
            {
                "PracticalUpgrades/Items/GravFieldCalibrationAssembly",
                "PracticalUpgrades/Items/GravResonanceController",
                "PracticalUpgrades/Items/MultiphaseGravCore"
            };
            string[] recipes =
            {
                "PU_MakeGravFieldCalibrationAssembly",
                "PU_MakeGravResonanceController",
                "PU_MakeMultiphaseGravCore"
            };

            context.Assert((engine != null) == odysseyActive, "GravEngine presence does not match Odyssey state.");
            for (int i = 0; i < modules.Length; i++)
            {
                string defName = modules[i];
                ThingDef module = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                context.Assert(
                    (module != null) == odysseyActive,
                    defName + " DLC gating is incorrect.");

                if (odysseyActive)
                {
                    context.Assert(module.graphicData != null && module.graphicData.texPath == moduleTextures[i], defName + " uses the wrong texture path.");
                    context.Assert(module.graphicData.Graphic != null && module.graphicData.Graphic.MatSingle.mainTexture != null, defName + " texture did not load.");
                }
            }
            foreach (string defName in recipes)
            {
                context.Assert(
                    (DefDatabase<RecipeDef>.GetNamedSilentFail(defName) != null) == odysseyActive,
                    defName + " DLC gating is incorrect.");
            }

            if (!odysseyActive)
            {
                return;
            }

            context.Assert(engine.GetCompProperties<CompProperties_UpgradeableGravEngine>() != null, "Grav engine upgrade comp is missing.");
            context.Assert(engine.GetCompProperties<CompProperties_GravEnginePower>() != null, "Grav engine power comp is missing.");
        }

        [Then("the grav engine upgrade levels match the design")]
        public void GravEngineLevelsMatch(PickleContext context)
        {
            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            ThingDef engine = DefDatabase<ThingDef>.GetNamed("GravEngine");
            CompProperties_UpgradeableGravEngine props = engine.GetCompProperties<CompProperties_UpgradeableGravEngine>();
            context.Assert(props != null && props.upgradeLevels.Count == 4, "Grav engine should have four total levels.");

            int[] extenders = { 2, 3, 4, 6 };
            int[] smallThrusters = { 2, 3, 4, 4 };
            int[] largeThrusters = { 0, 0, 4, 6 };
            float[] power = { 0f, 1000f, 2000f, 3000f };
            float[] capacity = { 0f, 100f, 250f, 500f };
            float[] recharge = { 0f, 20f, 50f, 100f };
            float[] offset = { 0f, 0.25f, 0.50f, 0.75f };
            string[] research = { null, "BasicGravtech", "StandardGravtech", "AdvancedGravtech" };

            for (int i = 0; i < props.upgradeLevels.Count; i++)
            {
                GravEngineUpgradeLevel level = props.upgradeLevels[i];
                context.Assert(level.gravFieldExtenderLimit == extenders[i], "Unexpected extender limit at grav level " + i + ".");
                context.Assert(level.smallThrusterLimit == smallThrusters[i], "Unexpected small thruster limit at grav level " + i + ".");
                context.Assert(level.largeThrusterLimit == largeThrusters[i], "Unexpected large thruster limit at grav level " + i + ".");
                context.Assert(Nearly(level.powerOutput, power[i]), "Unexpected power at grav level " + i + ".");
                context.Assert(Nearly(level.energyCapacity, capacity[i]), "Unexpected capacity at grav level " + i + ".");
                context.Assert(Nearly(level.energyRechargePerDay, recharge[i]), "Unexpected recharge at grav level " + i + ".");
                context.Assert(Nearly(level.maxFuelOffsetFraction, offset[i]), "Unexpected fuel offset at grav level " + i + ".");
                context.Assert((level.requiredResearch == null ? null : level.requiredResearch.defName) == research[i], "Unexpected research at grav level " + i + ".");
            }
        }

        [Then("grav engine module recipes are attached to vanilla gravtech")]
        public void GravRecipesMatch(PickleContext context)
        {
            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            AssertRecipe(context, "PU_MakeGravFieldCalibrationAssembly", "BasicGravtech", "TableMachining");
            AssertRecipe(context, "PU_MakeGravResonanceController", "StandardGravtech", "TableMachining");
            AssertRecipe(context, "PU_MakeMultiphaseGravCore", "AdvancedGravtech", "FabricationBench");
        }

        [Then("grav energy fuel calculations are correct")]
        public void GravEnergyFuelCalculations(PickleContext context)
        {
            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            CompUpgradeableGravEngine comp = MakeGravComp();
            SetField(comp, "upgradeLevel", 1);
            SetField(comp, "gravEnergy", 100f);
            context.Assert(Nearly(comp.EffectiveEnergyForFuel(300f), 100f), "Level 1 should offset 25% of 400 total fuel.");

            SetField(comp, "upgradeLevel", 2);
            SetField(comp, "gravEnergy", 250f);
            context.Assert(Nearly(comp.EffectiveEnergyForFuel(250f), 250f), "Level 2 should offset 50% of 500 total fuel.");

            SetField(comp, "upgradeLevel", 3);
            SetField(comp, "gravEnergy", 500f);
            context.Assert(Nearly(comp.EffectiveEnergyForFuel(100f), 300f), "Level 3 should offset 75% of 400 total fuel.");

            SetField(comp, "autoUseGravEnergy", false);
            context.Assert(Nearly(comp.EffectiveEnergyForFuel(100f), 0f), "Disabled auto-use must not offset fuel.");
        }

        [Then("the grav engine information entries are complete")]
        public void GravInformationEntries(PickleContext context)
        {
            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            CompUpgradeableGravEngine comp = MakeGravComp();
            string[] expectedLabels =
            {
                "PU_InfoGravUpgradeLevel".Translate(),
                "PU_InfoGravFacilitySlots".Translate(),
                "PU_InfoGravPowerOutput".Translate(),
                "PU_InfoGravEnergyStorage".Translate(),
                "PU_InfoGravEnergyRecharge".Translate(),
                "PU_InfoGravFuelOffset".Translate()
            };

            for (int level = 0; level < 4; level++)
            {
                SetField(comp, "upgradeLevel", level);
                List<StatDrawEntry> entries = comp.SpecialDisplayStats().ToList();
                foreach (string label in expectedLabels)
                {
                    context.Assert(entries.Any(entry => entry.LabelCap.ToString() == label), "Missing info entry '" + label + "' at grav level " + level + ".");
                }
            }
        }

        [Then("the practical upgrade Harmony patches are active")]
        public void HarmonyPatchesAreActive(PickleContext context)
        {
            AssertPatched(context, AccessTools.Method(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts)));
            AssertPatched(context, AccessTools.Method(typeof(ThingDef), nameof(ThingDef.SpecialDisplayStats)));

            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            AssertPatched(context, AccessTools.Method(typeof(CompAffectedByFacilities), "CanLinkTo"));
            AssertPatched(context, AccessTools.Method(typeof(Building_GravEngine), "ConsumeFuel"));
            AssertPatched(context, AccessTools.PropertyGetter(typeof(Building_GravEngine), "TotalFuel"));
        }

        private static CompUpgradeableGravEngine MakeGravComp()
        {
            ThingDef engine = DefDatabase<ThingDef>.GetNamed("GravEngine");
            CompProperties_UpgradeableGravEngine props = engine.GetCompProperties<CompProperties_UpgradeableGravEngine>();
            CompUpgradeableGravEngine comp = new CompUpgradeableGravEngine();
            comp.Initialize(props);
            return comp;
        }

        private static void AssertRecipe(PickleContext context, string recipeName, string researchName, string userName)
        {
            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamed(recipeName);
            context.Assert(recipe.researchPrerequisite != null && recipe.researchPrerequisite.defName == researchName, recipeName + " has the wrong research prerequisite.");
            context.Assert(recipe.recipeUsers != null && recipe.recipeUsers.Any(def => def.defName == userName), recipeName + " is not attached to " + userName + ".");
        }

        private static void AssertPatched(PickleContext context, MethodBase method)
        {
            context.Assert(method != null, "Expected patched method was not found.");
            Patches patches = Harmony.GetPatchInfo(method);
            context.Assert(patches != null && patches.Owners.Contains(HarmonyOwner), "Harmony patch is missing on " + method.DeclaringType.Name + "." + method.Name + ".");
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = AccessTools.Field(target.GetType(), name);
            if (field == null)
            {
                throw new MissingFieldException(target.GetType().FullName, name);
            }
            field.SetValue(target, value);
        }

        private static bool Nearly(float actual, float expected)
        {
            return Math.Abs(actual - expected) < 0.0001f;
        }
    }
}
