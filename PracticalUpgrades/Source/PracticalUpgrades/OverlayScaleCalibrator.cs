using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace PracticalUpgrades
{
    public static class OverlayScaleCalibrator
    {
        private const byte AlphaThreshold = 8;
        private static bool calibrated;

        public static void Calibrate()
        {
            if (calibrated)
            {
                return;
            }

            calibrated = true;
            ThingDef cabinetDef = DefDatabase<ThingDef>.GetNamedSilentFail("ToolCabinet");
            CompProperties_UpgradeableFacility props = cabinetDef?.GetCompProperties<CompProperties_UpgradeableFacility>();
            if (cabinetDef?.graphicData == null || props?.upgradeLevels.NullOrEmpty() != false)
            {
                Log.Warning("[Practical Upgrades] Could not auto-calibrate tool cabinet overlays: definition data is missing.");
                return;
            }

            string northPath = cabinetDef.graphicData.texPath + "_north";
            Texture2D originalTexture = ContentFinder<Texture2D>.Get(northPath, false)
                ?? ContentFinder<Texture2D>.Get(cabinetDef.graphicData.texPath, false);
            if (!TryMeasure(originalTexture, out AlphaMetrics original))
            {
                Log.Warning("[Practical Upgrades] Could not measure the active tool cabinet texture at " + northPath + ".");
                return;
            }

            string eastPath = cabinetDef.graphicData.texPath + "_east";
            Texture2D eastTexture = ContentFinder<Texture2D>.Get(eastPath, false);
            if (TryMeasure(eastTexture, out AlphaMetrics east))
            {
                original = new AlphaMetrics(
                    (original.WidthCoverage + east.HeightCoverage) * 0.5f,
                    (original.HeightCoverage + east.WidthCoverage) * 0.5f);
            }

            Vector2 originalDrawSize = cabinetDef.graphicData.drawSize;
            for (int levelIndex = 1; levelIndex < props.upgradeLevels.Count; levelIndex++)
            {
                FacilityUpgradeLevel level = props.upgradeLevels[levelIndex];
                GraphicData graphicData = level?.overlayGraphicData;
                Texture2D overlayTexture = graphicData == null
                    ? null
                    : ContentFinder<Texture2D>.Get(graphicData.texPath, false);
                if (!TryMeasure(overlayTexture, out AlphaMetrics overlay))
                {
                    Log.Warning("[Practical Upgrades] Could not measure overlay texture for tool cabinet level " + levelIndex + ".");
                    continue;
                }

                graphicData.drawSize = new Vector2(
                    originalDrawSize.x * original.WidthCoverage / overlay.WidthCoverage,
                    originalDrawSize.y * original.HeightCoverage / overlay.HeightCoverage);
                AccessTools.Field(typeof(GraphicData), "cachedGraphic").SetValue(graphicData, null);

                Log.Message("[Practical Upgrades] Auto-calibrated tool cabinet level " + levelIndex
                    + " overlay to drawSize " + graphicData.drawSize
                    + "; original alpha coverage " + original.WidthCoverage.ToStringPercent("F1")
                    + " x " + original.HeightCoverage.ToStringPercent("F1")
                    + ", overlay alpha coverage " + overlay.WidthCoverage.ToStringPercent("F1")
                    + " x " + overlay.HeightCoverage.ToStringPercent("F1") + ".");
            }
        }

        private static bool TryMeasure(Texture2D source, out AlphaMetrics metrics)
        {
            metrics = default;
            if (source == null || source.width <= 0 || source.height <= 0)
            {
                return false;
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture temporary = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            Texture2D readable = null;

            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
                readable.Apply(false, false);

                Color32[] pixels = readable.GetPixels32();
                int minX = source.width;
                int minY = source.height;
                int maxX = -1;
                int maxY = -1;
                for (int y = 0; y < source.height; y++)
                {
                    int row = y * source.width;
                    for (int x = 0; x < source.width; x++)
                    {
                        if (pixels[row + x].a <= AlphaThreshold)
                        {
                            continue;
                        }

                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                    }
                }

                if (maxX < minX || maxY < minY)
                {
                    return false;
                }

                metrics = new AlphaMetrics(
                    (maxX - minX + 1f) / source.width,
                    (maxY - minY + 1f) / source.height);
                return true;
            }
            catch (Exception exception)
            {
                Log.Warning("[Practical Upgrades] Texture alpha measurement failed: " + exception.Message);
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
                if (readable != null)
                {
                    UnityEngine.Object.Destroy(readable);
                }
            }
        }

        private readonly struct AlphaMetrics
        {
            public readonly float WidthCoverage;
            public readonly float HeightCoverage;

            public AlphaMetrics(float widthCoverage, float heightCoverage)
            {
                WidthCoverage = widthCoverage;
                HeightCoverage = heightCoverage;
            }
        }
    }
}
