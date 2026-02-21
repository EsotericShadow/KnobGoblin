using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using KnobForge.Core;
using KnobForge.Core.Scene;
using SkiaSharp;


namespace KnobForge.Rendering
{
    public sealed partial class PreviewRenderer
    {
        private SKColor ShadeFace(
            Vector3 centroid,
            Vector3 normal,
            Vector3 viewDir,
            float referenceRadius,
            float topRadius,
            ModelNode modelNode,
            SpiralNormalMap? spiralNormalMap,
            MaterialNode? materialNode,
            float modelCos,
            float modelSin)
        {
            _project.EnsureSelection();

            Vector3 baseColor = materialNode?.BaseColor ?? new Vector3(0.55f, 0.16f, 0.16f);
            float metallic = Math.Clamp(materialNode?.Metallic ?? 0f, 0f, 1f);
            float roughness = Math.Clamp(materialNode?.Roughness ?? 0.5f, 0.04f, 1f);
            float pearlescence = Math.Clamp(materialNode?.Pearlescence ?? 0f, 0f, 1f);
            float rustAmount = Math.Clamp(materialNode?.RustAmount ?? 0f, 0f, 1f);
            float wearAmount = Math.Clamp(materialNode?.WearAmount ?? 0f, 0f, 1f);
            float gunkAmount = Math.Clamp(materialNode?.GunkAmount ?? 0f, 0f, 1f);
            float shininess = 4f + ((128f - 4f) * (1f - roughness));
            float brushStrength = Math.Clamp(materialNode?.RadialBrushStrength ?? 0f, 0f, 1f);
            float brushDensity = MathF.Max(1f, materialNode?.RadialBrushDensity ?? 56f);
            float brushDensityFactor = Math.Clamp((brushDensity - 4f) / 316f, 0f, 1f);
            float surfaceCharacter = Math.Clamp(materialNode?.SurfaceCharacter ?? 0f, 0f, 1f);
            Vector3 ambientColor = new Vector3(0.03f, 0.03f, 0.03f);
            float diffuseStrength = 1.0f;
            float specularStrength = 1.0f;

            if (materialNode != null)
            {
                diffuseStrength = materialNode.DiffuseStrength;
                specularStrength = materialNode.SpecularStrength;
            }

            Vector3 shadingNormal = normal;
            if (Vector3.Dot(shadingNormal, viewDir) < 0f)
            {
                shadingNormal = -shadingNormal;
            }
            float microInfluence = _project.SpiralNormalInfluenceEnabled ? 1f : 0f;
            float fadeStart = MathF.Max(0.1f, _project.SpiralNormalLodFadeStart);
            float fadeEnd = MathF.Max(fadeStart + 1e-3f, _project.SpiralNormalLodFadeEnd);
            float roughnessLodBoostFactor = MathF.Max(0f, _project.SpiralRoughnessLodBoost);

            Vector3 topTangent = new(1f, 0f, 0f);
            topTangent -= shadingNormal * Vector3.Dot(shadingNormal, topTangent);
            if (topTangent.LengthSquared() <= 1e-8f)
            {
                topTangent = new Vector3(0f, 1f, 0f);
                topTangent -= shadingNormal * Vector3.Dot(shadingNormal, topTangent);
            }

            if (topTangent.LengthSquared() <= 1e-8f)
            {
                topTangent = Vector3.UnitX;
            }
            else
            {
                topTangent = Vector3.Normalize(topTangent);
            }

            Vector3 topBitangent = Vector3.Normalize(Vector3.Cross(shadingNormal, topTangent));
            float localX = (centroid.X * modelCos) + (centroid.Y * modelSin);
            float localY = (-centroid.X * modelSin) + (centroid.Y * modelCos);
            float indicatorMask = ComputeIndicatorMask(
                localX,
                localY,
                topRadius,
                modelNode.IndicatorEnabled,
                modelNode.IndicatorShape,
                modelNode.IndicatorWidthRatio,
                modelNode.IndicatorLengthRatioTop,
                modelNode.IndicatorPositionRatio,
                modelNode.IndicatorRoundness);
            indicatorMask *= SmoothStep(0.55f, 0.95f, MathF.Abs(shadingNormal.Z));
            float topMask = Math.Clamp((MathF.Abs(shadingNormal.Z) - 0.55f) / 0.40f, 0f, 1f);
            topMask = MathF.Pow(topMask, 1.6f + ((0.8f - 1.6f) * surfaceCharacter));
            topMask *= 1f - Math.Clamp(indicatorMask, 0f, 1f);
            Vector2 uv = new(
                (centroid.X / MathF.Max(topRadius * 2f, 1e-4f)) + 0.5f,
                (centroid.Y / MathF.Max(topRadius * 2f, 1e-4f)) + 0.5f);
            bool uvInside = uv.X >= 0f && uv.X <= 1f && uv.Y >= 0f && uv.Y <= 1f;
            float uvFootprint = 1f / MathF.Max(1e-5f, topRadius * 2f * _currentShadeZoom);
            float texelsPerPixel = 1f / MathF.Max(uvFootprint * SpiralNormalMapSize, 1e-5f);
            float microDetailVisibility = SmoothStep(fadeStart, fadeEnd, texelsPerPixel);

            if (spiralNormalMap != null && uvInside && topMask > 0f)
            {
                Vector3 mapNormal = SampleSpiralNormalBilinear(spiralNormalMap, uv.X, uv.Y);
                Vector3 microNormal =
                    (topTangent * mapNormal.X) +
                    (topBitangent * mapNormal.Y) +
                    (shadingNormal * mapNormal.Z);
                if (microNormal.LengthSquared() > 1e-8f)
                {
                    microNormal = Vector3.Normalize(microNormal);
                    float densityInfluence = Lerp(0.65f, 1.35f, brushDensityFactor);
                    float microBlend = brushStrength * densityInfluence * topMask * microDetailVisibility * microInfluence;
                    microBlend = Math.Clamp(microBlend, 0f, 1f);
                    shadingNormal = Vector3.Normalize(Vector3.Lerp(shadingNormal, microNormal, microBlend));
                }
            }

            // Fade out unresolved high-frequency cap detail to avoid distance moire.
            float capFlatten = (1f - microDetailVisibility) * topMask * microInfluence * 0.35f;
            shadingNormal = Vector3.Normalize(Vector3.Lerp(shadingNormal, Vector3.UnitZ, Math.Clamp(capFlatten, 0f, 1f)));

            float roughnessLodBoost = (1f - microDetailVisibility) * roughnessLodBoostFactor * (0.35f + (0.65f * surfaceCharacter)) * microInfluence;
            roughness = Math.Clamp(roughness + roughnessLodBoost, 0.04f, 1f);
            float indicatorBlend = Math.Clamp(modelNode.IndicatorColorBlend * indicatorMask, 0f, 1f);
            baseColor = Vector3.Lerp(baseColor, modelNode.IndicatorColor, indicatorBlend);

            // Literal paint-mask weathering (R=rust, G=wear, B=gunk) in local object space.
            float paintU = (localX / MathF.Max(referenceRadius * 2f, 1e-4f)) + 0.5f;
            float paintV = (localY / MathF.Max(referenceRadius * 2f, 1e-4f)) + 0.5f;
            Vector4 paintSample = Vector4.Zero;
            if (paintU >= 0f && paintU <= 1f && paintV >= 0f && paintV <= 1f)
            {
                paintSample = _project.SamplePaintMaskBilinear(paintU, paintV);
            }

            float brushDarkness = Math.Clamp(_project.BrushDarkness, 0f, 1f);
            Vector3 scratchExposeColor = new(
                Math.Clamp(_project.ScratchExposeColor.X, 0f, 1f),
                Math.Clamp(_project.ScratchExposeColor.Y, 0f, 1f),
                Math.Clamp(_project.ScratchExposeColor.Z, 0f, 1f));
            float darknessGain = Lerp(0.45f, 1.45f, brushDarkness);
            float rustRaw = Math.Clamp(paintSample.X, 0f, 1f);
            float wearRaw = Math.Clamp(paintSample.Y, 0f, 1f);
            float gunkRaw = Math.Clamp(paintSample.Z, 0f, 1f);
            float scratchRaw = Math.Clamp(paintSample.W, 0f, 1f);

            float rustNoiseA = ValueNoise2D((paintU * 192f) + 11.3f, (paintV * 217f) + 6.7f);
            float rustNoiseB = ValueNoise2D((paintU * 67f) + 41.1f, (paintV * 59f) + 13.5f);
            float rustSplotch = SmoothStep(0.32f, 0.90f, (rustNoiseA * 0.72f) + (rustNoiseB * 0.58f));
            float rustStrength = Lerp(0.30f, 1.00f, rustAmount);
            float wearStrength = Lerp(0.15f, 0.70f, wearAmount);
            float gunkStrength = Lerp(0.35f, 1.20f, gunkAmount);
            float scratchStrength = Lerp(0.30f, 1.00f, wearAmount);
            float rustMask = Math.Clamp(rustRaw * rustSplotch * darknessGain * rustStrength, 0f, 1f);
            float wearMask = Math.Clamp(wearRaw * Lerp(0.30f, 0.80f, brushDarkness) * wearStrength, 0f, 1f);
            float gunkMask = Math.Clamp(gunkRaw * Lerp(0.55f, 1.65f, brushDarkness) * gunkStrength, 0f, 1f);
            float scratchMask = Math.Clamp(scratchRaw * Lerp(0.45f, 1.00f, brushDarkness) * scratchStrength, 0f, 1f);

            float rustHue = ValueNoise2D((paintU * 103f) + 3.1f, (paintV * 97f) + 17.2f);
            Vector3 rustDark = new(0.23f, 0.08f, 0.04f);
            Vector3 rustMid = new(0.46f, 0.17f, 0.07f);
            Vector3 rustOrange = new(0.71f, 0.29f, 0.09f);
            Vector3 rustColor = Vector3.Lerp(
                Vector3.Lerp(rustDark, rustMid, Math.Clamp(rustHue * 1.25f, 0f, 1f)),
                rustOrange,
                Math.Clamp((rustHue - 0.35f) / 0.65f, 0f, 1f));
            Vector3 gunkColor = new(0.02f, 0.02f, 0.018f);
            Vector3 wearColor = Vector3.Lerp(baseColor, new Vector3(0.80f, 0.79f, 0.76f), 0.45f);

            baseColor = Vector3.Lerp(baseColor, rustColor, Math.Clamp(rustMask * 0.88f, 0f, 1f));
            baseColor = Vector3.Lerp(baseColor, gunkColor, Math.Clamp(gunkMask * 0.96f, 0f, 1f));
            baseColor = Vector3.Lerp(baseColor, wearColor, Math.Clamp(wearMask * 0.24f, 0f, 1f));
            float grimeDarken = Math.Clamp((rustMask * 0.18f + gunkMask * 0.55f) * (0.25f + (0.75f * brushDarkness)), 0f, 0.85f);
            baseColor *= 1f - grimeDarken;
            baseColor = Vector3.Lerp(baseColor, scratchExposeColor, Math.Clamp(scratchMask, 0f, 1f));

            roughness = Math.Clamp(
                roughness +
                (rustMask * 0.34f) +
                (gunkMask * 0.62f) -
                (wearMask * 0.05f) -
                (scratchMask * 0.14f),
                0.04f,
                1f);
            metallic = Math.Clamp(
                metallic -
                (rustMask * 0.62f) -
                (gunkMask * 0.30f) +
                (scratchMask * 0.10f),
                0f,
                1f);
            shininess = 4f + ((128f - 4f) * (1f - roughness));

            Vector3 radial = new(-centroid.Y, centroid.X, 0f);
            Vector3 tangent = radial.LengthSquared() > 1e-8f ? Vector3.Normalize(radial) : Vector3.UnitX;
            tangent -= shadingNormal * Vector3.Dot(shadingNormal, tangent);
            if (tangent.LengthSquared() <= 1e-8f)
            {
                tangent = Vector3.Cross(Vector3.UnitZ, shadingNormal);
                tangent = tangent.LengthSquared() > 1e-8f ? Vector3.Normalize(tangent) : Vector3.UnitX;
            }
            else
            {
                tangent = Vector3.Normalize(tangent);
            }

            Vector3 bitangent = Vector3.Normalize(Vector3.Cross(shadingNormal, tangent));
            float anisotropy = Math.Clamp(
                brushStrength * topMask * (0.35f + (0.65f * surfaceCharacter)) * Lerp(0.8f, 1.2f, brushDensityFactor),
                0f,
                0.95f);
            float alpha = MathF.Max(0.02f, roughness * roughness);
            float alphaT = MathF.Max(0.02f, alpha * (1f - anisotropy));
            float alphaB = MathF.Max(0.02f, alpha * (1f + anisotropy));

            float NdotV = MathF.Max(0f, Vector3.Dot(shadingNormal, viewDir));
            float maxBase = MathF.Max(1e-6f, MathF.Max(baseColor.X, MathF.Max(baseColor.Y, baseColor.Z)));
            Vector3 metalSpecColor = baseColor / maxBase;
            Vector3 F0 = Vector3.Lerp(new Vector3(0.04f), metalSpecColor, metallic);
            Vector3 fresnelView = F0 + (Vector3.One - F0) * MathF.Pow(1f - NdotV, 5f);

            Vector3 accum = Hadamard(baseColor, ambientColor) * (1f - metallic);
            for (int i = 0; i < _project.Lights.Count; i++)
            {
                var light = _project.Lights[i];
                Vector3 lightColor = new(light.Color.Red / 255f, light.Color.Green / 255f, light.Color.Blue / 255f);

                Vector3 L;
                float attenuation;
                if (light.Type == LightType.Directional)
                {
                    L = Vector3.Normalize(ApplyGizmoOrientation(GetDirectionalVector(light)));
                    attenuation = 1f;
                }
                else
                {
                    Vector3 lightPos = ApplyGizmoOrientation(new Vector3(light.X, light.Y, light.Z));
                    Vector3 delta = lightPos - centroid;
                    float dist = MathF.Max(1e-4f, delta.Length());
                    L = delta / dist;
                    float distNorm = dist / MathF.Max(1f, referenceRadius * 2f);
                    attenuation = 1f / (1f + MathF.Max(0f, light.Falloff) * distNorm * distNorm);
                }

                if (ForceSecondLightNoAttenuation && i == 1)
                {
                    attenuation = 1f;
                }

                float NdotL = MathF.Max(0f, Vector3.Dot(shadingNormal, L));
                float diffuseFactor = NdotL;
                float effectiveDiffuse = diffuseFactor * (1f - metallic * 0.92f);
                Vector3 hRaw = L + viewDir;
                Vector3 halfVec = hRaw.LengthSquared() > 1e-8f ? Vector3.Normalize(hRaw) : viewDir;
                float NdotH = MathF.Max(0f, Vector3.Dot(shadingNormal, halfVec));
                float VdotH = MathF.Max(0f, Vector3.Dot(viewDir, halfVec));
                float TdotH = Vector3.Dot(tangent, halfVec);
                float BdotH = Vector3.Dot(bitangent, halfVec);
                float rawSpec = MathF.Pow(NdotH, shininess);
                float rawSpecBase = rawSpec;

                float dDenom = ((TdotH * TdotH) / (alphaT * alphaT)) +
                               ((BdotH * BdotH) / (alphaB * alphaB)) +
                               (NdotH * NdotH);
                float D = 1f / ((MathF.PI * alphaT * alphaB * dDenom * dDenom) + 1e-6f);

                float k = ((roughness + 1f) * (roughness + 1f)) / 8f;
                float Gv = NdotV / ((NdotV * (1f - k)) + k);
                float Gl = NdotL / ((NdotL * (1f - k)) + k);
                float G = Gv * Gl;
                Vector3 F = F0 + (Vector3.One - F0) * MathF.Pow(1f - VdotH, 5f);
                Vector3 specBrdf = F * ((D * G) / MathF.Max(4f * NdotV * NdotL, 1e-4f));

                ApplyModeShaping(_project.Mode, light, ref effectiveDiffuse, ref rawSpec);

                float intensity = MathF.Max(0f, light.Intensity) * attenuation;
                effectiveDiffuse *= intensity;
                float specShapeScale = rawSpecBase > 1e-5f ? (rawSpec / rawSpecBase) : 1f;
                float metalSpecBoost = 1f + ((2f - 1f) * metallic);
                float artisticSpecBoost = 0.55f + 0.45f * MathF.Max(0f, light.SpecularBoost);
                Vector3 specularTerm = specBrdf * NdotL;
                specularTerm *= specularStrength * intensity * metalSpecBoost * MathF.Max(0f, specShapeScale) * artisticSpecBoost;

                accum += Hadamard(baseColor, lightColor) * (effectiveDiffuse * diffuseStrength);
                accum += Hadamard(lightColor, specularTerm);
            }

            Vector3 envTop = _project.EnvironmentTopColor;
            Vector3 envBottom = _project.EnvironmentBottomColor;
            float envIntensity = MathF.Max(0f, _project.EnvironmentIntensity);
            float envRoughMix = Math.Clamp(_project.EnvironmentRoughnessMix, 0f, 1f);

            Vector3 reflection = Vector3.Reflect(-viewDir, shadingNormal);
            float hemi = Math.Clamp(reflection.Y * 0.5f + 0.5f, 0f, 1f);
            Vector3 envBase = envBottom + ((envTop - envBottom) * hemi);
            float horizonBand = MathF.Exp(-MathF.Abs(reflection.Y) * 12f);
            float skyHotspot = MathF.Pow(Math.Clamp((reflection.Z * 0.5f) + 0.5f, 0f, 1f), 24f) * hemi;
            Vector3 horizonColor = Vector3.Lerp(envTop, Vector3.One, 0.35f);
            Vector3 envColor = envBase + (horizonColor * (0.40f * horizonBand)) + (Vector3.One * (0.25f * skyHotspot));
            float envSpecWeight = 0.20f + (1.15f * metallic);
            Vector3 chromeTint = Vector3.Lerp(metalSpecColor, Vector3.One, 0.35f);
            Vector3 specTint = Vector3.Lerp(Vector3.One, chromeTint, metallic);
            Vector3 envSpecular = Hadamard(Hadamard(envColor, fresnelView), specTint) * envSpecWeight;
            float envDiffuseWeight = MathF.Max(0f, 1f - metallic);
            Vector3 envDiffuse = Hadamard(baseColor, envColor) * envDiffuseWeight;
            float envDiffuseEnergy = 0.35f;
            float roughEnergy = 1.12f + ((0.45f - 1.12f) * (roughness * envRoughMix));
            float anisotropicEnergy = 1f + ((1.35f - 1f) * anisotropy);
            float envBrush = Lerp(1f, 1.08f, brushStrength * topMask * (0.35f + (0.65f * surfaceCharacter)));
            accum += envDiffuse * (envIntensity * envDiffuseEnergy);
            accum += envSpecular * (envIntensity * roughEnergy * anisotropicEnergy * envBrush);

            if (pearlescence > 1e-4f)
            {
                float pearlEdge = MathF.Pow(1f - NdotV, 1.35f);
                Vector3 rv = reflection + viewDir;
                Vector3 rvn = rv.LengthSquared() > 1e-8f ? Vector3.Normalize(rv) : viewDir;
                float pearlPhase = Math.Clamp((Vector3.Dot(rvn, new Vector3(0.23f, 0.67f, 0.71f)) * 0.5f) + 0.5f, 0f, 1f);
                Vector3 pearlTint = new(
                    0.5f + (0.5f * MathF.Cos((MathF.Tau * (pearlPhase + 0.00f)))),
                    0.5f + (0.5f * MathF.Cos((MathF.Tau * (pearlPhase + 0.33f)))),
                    0.5f + (0.5f * MathF.Cos((MathF.Tau * (pearlPhase + 0.67f)))));
                float pearlStrength = pearlescence * (0.15f + (0.85f * pearlEdge));
                accum += pearlTint * pearlStrength * (0.20f + (0.80f * envIntensity));
            }

            accum = accum / (Vector3.One + accum);
            accum = Clamp01(accum);
            return new SKColor((byte)(accum.X * 255f), (byte)(accum.Y * 255f), (byte)(accum.Z * 255f), 255);
        }
    }
}
