using HarmonyLib;
using Verse;

namespace PracticalUpgrades
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            const string harmonyId = "langyejia.practicalupgrades";
            Harmony harmony = new Harmony(harmonyId);
            harmony.PatchAll();

            var printMethod = AccessTools.DeclaredMethod(
                typeof(ThingWithComps),
                nameof(ThingWithComps.Print),
                new[] { typeof(SectionLayer) });
            bool overlayPatchApplied = printMethod != null
                && Harmony.GetPatchInfo(printMethod)?.Owners.Contains(harmonyId) == true;
            Log.Message("[Practical Upgrades] Tool cabinet overlay patch applied: " + overlayPatchApplied);
            LongEventHandler.ExecuteWhenFinished(OverlayScaleCalibrator.Calibrate);
        }
    }
}
