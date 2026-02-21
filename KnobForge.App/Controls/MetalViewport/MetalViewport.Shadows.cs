using System;
using System.Collections.Generic;
using System.Numerics;
using KnobForge.Core;
using KnobForge.Core.Scene;
using KnobForge.Rendering.GPU;

namespace KnobForge.App.Controls
{
    public sealed partial class MetalViewport
    {
        private void RenderShadowPasses(
            IntPtr encoderPtr,
            in GpuUniforms baseUniforms,
            in ShadowPassConfig config,
            MetalMeshGpuResources mesh)
        {
            if (encoderPtr == IntPtr.Zero ||
                mesh.VertexBuffer.Handle == IntPtr.Zero ||
                mesh.IndexBuffer.Handle == IntPtr.Zero ||
                config.Alpha <= 1e-5f)
            {
                return;
            }

            ObjC.Void_objc_msgSend_IntPtr_UInt_UInt(
                encoderPtr,
                Selectors.SetVertexBufferOffsetAtIndex,
                mesh.VertexBuffer.Handle,
                0,
                0);

            int sampleCount = Math.Clamp(config.SampleCount, 1, ShadowSampleKernel.Length);
            const float shadowDepthBiasClip = 0.004f;

            float weightSum = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                Vector2 s = ShadowSampleKernel[i];
                float r2 = (s.X * s.X) + (s.Y * s.Y);
                weightSum += MathF.Exp(-2.5f * r2);
            }
            weightSum = MathF.Max(1e-5f, weightSum);

            for (int i = 0; i < sampleCount; i++)
            {
                Vector2 s = ShadowSampleKernel[i];
                float r2 = (s.X * s.X) + (s.Y * s.Y);
                float weight = MathF.Exp(-2.5f * r2) / weightSum;
                float jitterX = s.X * config.SoftRadiusXClip;
                float jitterY = s.Y * config.SoftRadiusYClip;

                GpuUniforms shadowUniforms = baseUniforms;
                shadowUniforms.ShadowParams = new Vector4(
                    1f,
                    config.OffsetXClip + jitterX,
                    config.OffsetYClip + jitterY,
                    config.Scale);
                float darkness = Math.Clamp(1f - config.Gray, 0f, 1f);
                shadowUniforms.ShadowColorAndOpacity = new Vector4(
                    shadowDepthBiasClip,
                    0f,
                    0f,
                    config.Alpha * darkness * weight);

                UploadUniforms(encoderPtr, shadowUniforms);
                ObjC.Void_objc_msgSend_UInt_UInt_UInt_IntPtr_UInt(
                    encoderPtr,
                    Selectors.DrawIndexedPrimitivesIndexCountIndexTypeIndexBufferIndexBufferOffset,
                    3, // MTLPrimitiveTypeTriangle
                    (nuint)mesh.IndexCount,
                    (nuint)mesh.IndexType,
                    mesh.IndexBuffer.Handle,
                    0);
            }
        }

        private IReadOnlyList<ShadowPassConfig> ResolveShadowPassConfigs(
            KnobProject? project,
            Vector3 cameraRight,
            Vector3 cameraUp,
            Vector3 cameraForward,
            float viewportWidthPx,
            float viewportHeightPx)
        {
            _resolvedShadowPasses.Clear();
            _shadowLightContributions.Clear();
            if (project == null || !project.ShadowsEnabled)
            {
                return _resolvedShadowPasses;
            }

            // Keep Selected shadow mode stable even if UI selection briefly leaves the light list.
            project.EnsureSelection();

            switch (project.ShadowMode)
            {
                case ShadowLightMode.Selected:
                {
                    KnobLight? selected = project.SelectedLight;
                    if (selected != null &&
                        TryEvaluateShadowLight(project, selected, cameraRight, cameraUp, cameraForward, out Vector2 shadowVec, out float weight, out float planar))
                    {
                        _shadowLightContributions.Add(new ShadowLightContribution(shadowVec, weight, planar));
                    }

                    break;
                }

                case ShadowLightMode.Dominant:
                {
                    float bestWeight = 0f;
                    Vector2 bestVec = default;
                    float bestPlanar = 0f;
                    for (int i = 0; i < project.Lights.Count; i++)
                    {
                        if (!TryEvaluateShadowLight(project, project.Lights[i], cameraRight, cameraUp, cameraForward, out Vector2 shadowVec, out float weight, out float planar))
                        {
                            continue;
                        }

                        if (weight <= bestWeight)
                        {
                            continue;
                        }

                        bestWeight = weight;
                        bestVec = shadowVec;
                        bestPlanar = planar;
                    }

                    if (bestWeight > 1e-6f && bestVec.LengthSquared() > 1e-8f)
                    {
                        _shadowLightContributions.Add(new ShadowLightContribution(bestVec, bestWeight, bestPlanar));
                    }

                    break;
                }

                default:
                {
                    for (int i = 0; i < project.Lights.Count; i++)
                    {
                        if (!TryEvaluateShadowLight(project, project.Lights[i], cameraRight, cameraUp, cameraForward, out Vector2 shadowVec, out float weight, out float planar))
                        {
                            continue;
                        }

                        _shadowLightContributions.Add(new ShadowLightContribution(shadowVec, weight, planar));
                    }

                    break;
                }
            }

            if (_shadowLightContributions.Count == 0)
            {
                return _resolvedShadowPasses;
            }

            bool allowMultipleLights = project.ShadowMode == ShadowLightMode.Weighted && _shadowLightContributions.Count > 1;
            BuildShadowPassConfigs(project, viewportWidthPx, viewportHeightPx, allowMultipleLights);
            return _resolvedShadowPasses;
        }

        private void BuildShadowPassConfigs(
            KnobProject project,
            float viewportWidthPx,
            float viewportHeightPx,
            bool allowMultipleLights)
        {
            _shadowLightContributions.Sort((a, b) => b.Weight.CompareTo(a.Weight));

            int passCount;
            if (allowMultipleLights)
            {
                int desiredPassCount = 1 + (int)MathF.Round(Math.Clamp(project.ShadowQuality, 0f, 1f) * (MaxShadowPassLights - 1));
                desiredPassCount = Math.Clamp(desiredPassCount, 1, MaxShadowPassLights);
                if (_shadowLightContributions.Count >= 2)
                {
                    desiredPassCount = Math.Max(2, desiredPassCount);
                }

                passCount = Math.Min(desiredPassCount, _shadowLightContributions.Count);
            }
            else
            {
                passCount = 1;
            }

            float totalWeight = 0f;
            for (int i = 0; i < passCount; i++)
            {
                totalWeight += _shadowLightContributions[i].Weight;
            }

            totalWeight = MathF.Max(1e-6f, totalWeight);
            float baseSize = MathF.Max(1f, MathF.Min(viewportWidthPx, viewportHeightPx));
            float clipScaleX = 2f / MathF.Max(1f, viewportWidthPx);
            float clipScaleY = 2f / MathF.Max(1f, viewportHeightPx);
            float distanceUser = MathF.Max(0f, project.ShadowDistance);
            float softness = Math.Clamp(project.ShadowSoftness, 0f, 1f);
            float gray = project.ShadowGray;
            float quality = Math.Clamp(project.ShadowQuality, 0f, 1f);
            int sampleBudget = 1 + (int)MathF.Round(quality * 15f);
            int samplesPerPass = allowMultipleLights
                ? Math.Max(1, (int)MathF.Ceiling(sampleBudget / (float)passCount))
                : sampleBudget;

            float totalPowerNorm = Math.Clamp(totalWeight / 3f, 0.15f, 1.35f);
            float alphaBudget = Math.Clamp((0.08f + (0.26f * totalPowerNorm)) * project.ShadowStrength, 0f, 0.85f);

            for (int i = 0; i < passCount; i++)
            {
                ShadowLightContribution contribution = _shadowLightContributions[i];
                if (contribution.ShadowVec.LengthSquared() <= 1e-8f || contribution.Weight <= 1e-6f)
                {
                    continue;
                }

                Vector2 screenDirection = Vector2.Normalize(contribution.ShadowVec);
                float planar = contribution.Planar;
                float powerNorm = Math.Clamp(contribution.Weight / 3f, 0.15f, 1.35f);
                float weightRatio = Math.Clamp(contribution.Weight / totalWeight, 0f, 1f);
                float spread = allowMultipleLights ? 1f - weightRatio : 0f;
                float offsetMagPx = baseSize * (0.010f + (0.032f * planar)) * powerNorm * distanceUser;
                float scale = project.ShadowScale * (1.0f + (0.035f * planar));
                float softRadiusPx = baseSize * (0.003f + (0.026f * softness * (0.45f + (0.55f * spread))));
                float alpha = allowMultipleLights ? alphaBudget * weightRatio : alphaBudget;
                if (alpha <= 1e-5f)
                {
                    continue;
                }

                float offsetXClip = screenDirection.X * offsetMagPx * clipScaleX;
                float offsetYClip = -screenDirection.Y * offsetMagPx * clipScaleY;
                float softRadiusXClip = softRadiusPx * clipScaleX;
                float softRadiusYClip = softRadiusPx * clipScaleY;

                _resolvedShadowPasses.Add(new ShadowPassConfig(
                    true,
                    offsetXClip,
                    offsetYClip,
                    MathF.Max(0.5f, scale),
                    alpha,
                    gray,
                    softRadiusXClip,
                    softRadiusYClip,
                    samplesPerPass));
            }
        }

        private bool TryEvaluateShadowLight(
            KnobProject project,
            KnobLight light,
            Vector3 cameraRight,
            Vector3 cameraUp,
            Vector3 cameraForward,
            out Vector2 shadowVec,
            out float weight,
            out float planar)
        {
            shadowVec = default;
            weight = 0f;
            planar = 0f;

            float intensity = MathF.Max(0f, light.Intensity);
            if (intensity <= 1e-5f)
            {
                return false;
            }

            Vector3 dir;
            if (light.Type == LightType.Directional)
            {
                dir = ApplyLightOrientation(GetDirectionalVector(light));
                if (dir.LengthSquared() <= 1e-8f)
                {
                    return false;
                }

                dir = Vector3.Normalize(dir);
            }
            else
            {
                Vector3 lightPos = ApplyLightOrientation(new Vector3(light.X, light.Y, light.Z));
                if (lightPos.LengthSquared() <= 1e-8f)
                {
                    return false;
                }

                dir = Vector3.Normalize(lightPos);
            }

            float distNorm = light.Type == LightType.Point
                ? MathF.Max(0.2f, new Vector3(light.X, light.Y, light.Z).Length() / MathF.Max(1f, (_meshResources?.ReferenceRadius ?? 220f) * 2f))
                : 1f;
            float attenuation = light.Type == LightType.Point
                ? 1f / (1f + (MathF.Max(0f, light.Falloff) * distNorm * distNorm))
                : 1f;

            float luminance = ((0.2126f * light.Color.Red) + (0.7152f * light.Color.Green) + (0.0722f * light.Color.Blue)) / 255f;
            float diffuse = MathF.Max(0f, light.DiffuseBoost);
            float diffuseTerm = 0.35f + (0.65f * MathF.Pow(diffuse, MathF.Max(0f, project.ShadowDiffuseInfluence)));
            weight = intensity * attenuation * diffuseTerm * (0.35f + (0.65f * luminance));
            if (weight <= 1e-6f)
            {
                return false;
            }

            float sx = Vector3.Dot(dir, cameraRight);
            float sy = -Vector3.Dot(dir, cameraUp);
            Vector2 projected = new(sx, sy);
            float projectedLen = projected.Length();
            if (projectedLen <= 1e-6f)
            {
                return false;
            }

            float parallaxScale = light.Type == LightType.Point
                ? Math.Clamp(1.15f / MathF.Max(0.35f, distNorm), 0.45f, 1.75f)
                : 1f;
            shadowVec = -projected * parallaxScale;
            float viewIncidence = MathF.Abs(Vector3.Dot(dir, cameraForward));
            planar = MathF.Sqrt(MathF.Max(0f, 1f - (viewIncidence * viewIncidence)));
            return true;
        }

    }
}
