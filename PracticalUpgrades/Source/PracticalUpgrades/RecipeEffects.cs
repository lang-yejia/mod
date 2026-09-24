using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PracticalUpgrades
{
    public struct FacilityCraftingEffects
    {
        public float materialRecoveryChance;
        public float qualityShiftChance;
    }

    public static class FacilityUpgradeUtility
    {
        public static FacilityCraftingEffects EffectsFor(Thing billGiver)
        {
            FacilityCraftingEffects effects = default;
            CompAffectedByFacilities affected = billGiver?.TryGetComp<CompAffectedByFacilities>();
            if (affected == null)
            {
                return effects;
            }

            List<Thing> facilities = affected.LinkedFacilitiesListForReading;
            for (int i = 0; i < facilities.Count; i++)
            {
                Thing facility = facilities[i];
                CompUpgradeableFacility comp = facility.TryGetComp<CompUpgradeableFacility>();
                if (comp?.CurrentLevel == null || !affected.IsFacilityActive(facility))
                {
                    continue;
                }

                effects.materialRecoveryChance += comp.CurrentLevel.materialRecoveryChance;
                effects.qualityShiftChance += comp.CurrentLevel.qualityShiftChance;
            }

            effects.materialRecoveryChance = Math.Min(effects.materialRecoveryChance, 0.20f);
            effects.qualityShiftChance = Math.Min(effects.qualityShiftChance, 0.20f);
            return effects;
        }
    }

    [HarmonyPatch(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts))]
    public static class GenRecipe_MakeRecipeProducts_Patch
    {
        public static void Postfix(
            RecipeDef recipeDef,
            Pawn worker,
            List<Thing> ingredients,
            IBillGiver billGiver,
            ref IEnumerable<Thing> __result)
        {
            Thing giverThing = billGiver as Thing;
            if (__result == null || giverThing == null || giverThing.Faction != Faction.OfPlayer)
            {
                return;
            }

            FacilityCraftingEffects effects = FacilityUpgradeUtility.EffectsFor(giverThing);
            if (effects.materialRecoveryChance <= 0f && effects.qualityShiftChance <= 0f)
            {
                return;
            }

            __result = ApplyEffects(__result, recipeDef, ingredients, effects);
        }

        private static IEnumerable<Thing> ApplyEffects(
            IEnumerable<Thing> products,
            RecipeDef recipe,
            List<Thing> ingredients,
            FacilityCraftingEffects effects)
        {
            foreach (Thing product in products)
            {
                TryShiftQuality(product, effects.qualityShiftChance);
                yield return product;
            }

            foreach (Thing recovered in RecoverMaterials(recipe, ingredients, effects.materialRecoveryChance))
            {
                yield return recovered;
            }
        }

        private static void TryShiftQuality(Thing product, float chance)
        {
            if (chance <= 0f || product == null)
            {
                return;
            }

            CompQuality qualityComp = product.TryGetComp<CompQuality>();
            if (qualityComp == null || qualityComp.Quality >= QualityCategory.Legendary || !Rand.Chance(chance))
            {
                return;
            }

            qualityComp.SetQuality((QualityCategory)((int)qualityComp.Quality + 1), ArtGenerationContext.Colony);
        }

        private static IEnumerable<Thing> RecoverMaterials(RecipeDef recipe, List<Thing> ingredients, float chance)
        {
            if (chance <= 0f || ingredients.NullOrEmpty() || !EligibleRecipe(recipe))
            {
                yield break;
            }

            for (int i = 0; i < ingredients.Count; i++)
            {
                Thing ingredient = ingredients[i];
                if (!EligibleIngredient(ingredient))
                {
                    continue;
                }

                int recoveredCount = 0;
                for (int unit = 0; unit < ingredient.stackCount; unit++)
                {
                    if (Rand.Chance(chance))
                    {
                        recoveredCount++;
                    }
                }

                if (recoveredCount <= 0)
                {
                    continue;
                }

                Thing recovered = ThingMaker.MakeThing(ingredient.def);
                recovered.stackCount = recoveredCount;
                yield return recovered;
            }
        }

        private static bool EligibleRecipe(RecipeDef recipe)
        {
            if (recipe?.products.NullOrEmpty() != false || recipe.IsSurgery)
            {
                return false;
            }

            for (int i = 0; i < recipe.products.Count; i++)
            {
                if (recipe.products[i].thingDef.IsIngestible)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool EligibleIngredient(Thing ingredient)
        {
            if (ingredient == null || ingredient.stackCount <= 0 || ingredient.def.IsIngestible
                || ingredient is Pawn || ingredient is Corpse || ingredient.TryGetComp<CompQuality>() != null)
            {
                return false;
            }

            return ingredient.def.IsStuff
                || ingredient.def == ThingDefOf.ComponentIndustrial
                || ingredient.def == ThingDefOf.ComponentSpacer;
        }
    }
}
