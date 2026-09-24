using RimWorld;
using Verse;

namespace PracticalUpgrades
{
    [DefOf]
    public static class PUDesignationDefOf
    {
        public static DesignationDef PU_UpgradeFacility;

        static PUDesignationDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(PUDesignationDefOf));
        }
    }

    [DefOf]
    public static class PUJobDefOf
    {
        public static JobDef PU_UpgradeFacility;

        static PUJobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(PUJobDefOf));
        }
    }
}
