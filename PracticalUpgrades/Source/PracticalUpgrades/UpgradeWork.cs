using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace PracticalUpgrades
{
    public interface IUpgradeableBuilding
    {
        Thing ParentThing { get; }
        ThingDef NextModuleDef { get; }
        float UpgradeProgress { get; }
        bool InstallationComplete { get; }
        bool HasUpgradeDesignation();
        bool CanInstallNextLevel(Pawn pawn, bool reportFailure);
        void AddUpgradeWork(float amount);
        void CompleteUpgrade();
    }

    public static class UpgradeableBuildingUtility
    {
        public static IUpgradeableBuilding Get(Thing thing)
        {
            if (!(thing is ThingWithComps thingWithComps))
            {
                return null;
            }

            return (IUpgradeableBuilding)thingWithComps.TryGetComp<CompUpgradeableFacility>()
                ?? thingWithComps.TryGetComp<CompUpgradeableGravEngine>();
        }
    }

    public class WorkGiver_UpgradeFacility : WorkGiver_Scanner
    {
        private const float SearchRadius = 9999f;

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            foreach (Designation designation in pawn.Map.designationManager.SpawnedDesignationsOfDef(PUDesignationDefOf.PU_UpgradeFacility))
            {
                Thing thing = designation.target.Thing;
                if (thing != null)
                {
                    yield return thing;
                }
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            IUpgradeableBuilding comp = UpgradeableBuildingUtility.Get(t);
            if (comp == null || !comp.HasUpgradeDesignation() || !comp.CanInstallNextLevel(pawn, true))
            {
                return false;
            }

            if (t.IsForbidden(pawn) || t.IsBurning() || !pawn.CanReserve(t, 1, -1, null, forced))
            {
                return false;
            }

            Thing module = FindModule(pawn, comp.NextModuleDef, forced);
            if (module == null)
            {
                JobFailReason.Is("PU_NoUpgradeModule".Translate(comp.NextModuleDef.LabelCap));
                return false;
            }

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            IUpgradeableBuilding comp = UpgradeableBuildingUtility.Get(t);
            if (comp?.NextModuleDef == null)
            {
                return null;
            }

            Thing module = FindModule(pawn, comp.NextModuleDef, forced);
            if (module == null)
            {
                return null;
            }

            Job job = JobMaker.MakeJob(PUJobDefOf.PU_UpgradeFacility, t, module);
            job.count = 1;
            return job;
        }

        private static Thing FindModule(Pawn pawn, ThingDef moduleDef, bool forced)
        {
            if (moduleDef == null)
            {
                return null;
            }

            return GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(moduleDef),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                SearchRadius,
                thing => !thing.IsForbidden(pawn) && pawn.CanReserve(thing, 1, 1, null, forced));
        }
    }

    public class JobDriver_UpgradeFacility : JobDriver
    {
        private const TargetIndex FacilityInd = TargetIndex.A;
        private const TargetIndex ModuleInd = TargetIndex.B;

        private Thing Facility => job.GetTarget(FacilityInd).Thing;

        private IUpgradeableBuilding FacilityComp => UpgradeableBuildingUtility.Get(Facility);

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Facility, job, 1, -1, null, errorOnFailed)
                && pawn.Reserve(job.GetTarget(ModuleInd).Thing, job, 1, 1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(FacilityInd);
            this.FailOnDestroyedNullOrForbidden(ModuleInd);
            this.FailOnBurningImmobile(FacilityInd);
            this.FailOn(() => FacilityComp == null || !FacilityComp.HasUpgradeDesignation());
            this.FailOn(() => FacilityComp?.NextModuleDef != job.GetTarget(ModuleInd).Thing?.def);

            yield return Toils_Goto.GotoThing(ModuleInd, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(ModuleInd, false, true, false);
            yield return Toils_Goto.GotoThing(FacilityInd, PathEndMode.Touch);

            Toil install = ToilMaker.MakeToil("InstallFacilityUpgrade");
            install.defaultCompleteMode = ToilCompleteMode.Never;
            install.tickAction = delegate
            {
                IUpgradeableBuilding comp = FacilityComp;
                if (comp == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                float speed = pawn.GetStatValue(StatDefOf.ConstructionSpeed);
                comp.AddUpgradeWork(speed);
                pawn.skills?.Learn(SkillDefOf.Construction, 0.11f);

                if (comp.InstallationComplete)
                {
                    ReadyForNextToil();
                }
            };
            install.WithProgressBar(FacilityInd, () => FacilityComp?.UpgradeProgress ?? 0f);
            install.WithEffect(EffecterDefOf.ConstructMetal, FacilityInd);
            install.PlaySustainerOrSound(SoundDefOf.Interact_ConstructDirt);
            yield return install;

            Toil finish = ToilMaker.MakeToil("FinishFacilityUpgrade");
            finish.initAction = delegate
            {
                IUpgradeableBuilding comp = FacilityComp;
                Thing carried = pawn.carryTracker.CarriedThing;
                if (comp == null || carried == null || comp.NextModuleDef != carried.def || !comp.InstallationComplete)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                carried.Destroy(DestroyMode.Vanish);
                comp.CompleteUpgrade();
            };
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }
    }
}
