using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace CowHerdManager
{
    [StaticConstructorOnStartup]
    internal static class Bootstrap
    {
        static Bootstrap()
        {
            new Harmony("langyejia.cowherdmanager").PatchAll();
            Log.Message("[Cow Herd Manager] Idle-wander patch loaded.");
        }
    }

    public class Building_CowHerdManagerPost : Building
    {
        private const int Radius = 30;
        private bool enabledForCows = true;

        public bool Controls(Pawn pawn)
        {
            return enabledForCows && Spawned && pawn.Spawned && pawn.Map == Map &&
                   pawn.Position.InHorDistOf(Position, Radius);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enabledForCows, "enabledForCows", true);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            yield return new Command_Toggle
            {
                defaultLabel = "CowHerdManager.ToggleLabel".Translate(),
                defaultDesc = "CowHerdManager.ToggleDesc".Translate(),
                icon = TexCommand.ForbidOff,
                isActive = () => enabledForCows,
                toggleAction = () => enabledForCows = !enabledForCows
            };
        }

        public override string GetInspectString()
        {
            string status = (enabledForCows ? "CowHerdManager.Managed" : "CowHerdManager.Vanilla").Translate();
            string details = "CowHerdManager.Inspect".Translate(status);
            string baseString = base.GetInspectString();
            return baseString.NullOrEmpty() ? details : baseString + "\n" + details;
        }
    }

    [HarmonyPatch(typeof(JobGiver_Wander), "TryGiveJob")]
    internal static class WanderPatch
    {
        private static bool Prefix(Pawn pawn, ref Job __result)
        {
            if (pawn == null || !pawn.Spawned || pawn.def != ThingDef.Named("Cow") ||
                pawn.Faction != Faction.OfPlayer || pawn.Downed || pawn.InMentalState ||
                pawn.Map == null)
                return true;

            List<Building> posts = pawn.Map.listerBuildings.AllBuildingsColonistOfDef(
                ThingDef.Named("CowHerdManagerPost"));
            for (int i = 0; i < posts.Count; i++)
            {
                Building_CowHerdManagerPost post = posts[i] as Building_CowHerdManagerPost;
                if (post == null || !post.Controls(pawn)) continue;

                Job job = JobMaker.MakeJob(JobDefOf.Wait_Wander);
                job.expiryInterval = 300;
                job.checkOverrideOnExpire = true;
                __result = job;
                return false;
            }
            return true;
        }
    }
}
