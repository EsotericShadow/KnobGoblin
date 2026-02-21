using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using KnobForge.Core;
using KnobForge.Core.Export;
using KnobForge.Core.Scene;
using KnobForge.Rendering.GPU;
using SkiaSharp;

namespace KnobForge.Rendering;

public sealed partial class KnobExporter
{
        private SKPaint? CreateShadowPaint(ShadowConfig config)
        {
            if (!config.Enabled || config.Opacity <= 0f)
            {
                return null;
            }

            byte gray = (byte)Math.Clamp((int)MathF.Round(config.Gray * 255f), 0, 255);
            byte alpha = (byte)Math.Clamp((int)MathF.Round(config.Opacity * 255f), 0, 255);

            var paint = new SKPaint
            {
                BlendMode = SKBlendMode.SrcOver,
                IsAntialias = true,
                ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(gray, gray, gray, alpha), SKBlendMode.SrcIn)
            };

            if (config.BlurPx > 0.01f)
            {
                paint.ImageFilter = SKImageFilter.CreateBlur(config.BlurPx, config.BlurPx);
            }

            return paint;
        }

        private static void ComposeFrameWithDropShadow(
            SKCanvas destinationCanvas,
            SKBitmap destinationBitmap,
            SKCanvas sourceCanvas,
            SKBitmap sourceBitmap,
            SKPaint sourceCopyPaint,
            SKPaint shadowPaint,
            ShadowConfig config)
        {
            sourceCanvas.Clear(new SKColor(0, 0, 0, 0));
            sourceCanvas.DrawBitmap(destinationBitmap, 0, 0, sourceCopyPaint);

            destinationCanvas.Clear(new SKColor(0, 0, 0, 0));

            float width = sourceBitmap.Width;
            float height = sourceBitmap.Height;
            float centerX = width * 0.5f;
            float centerY = height * 0.5f;
            float shadowScale = MathF.Max(0.5f, config.Scale);
            float shadowWidth = width * shadowScale;
            float shadowHeight = height * shadowScale;
            float left = centerX - (shadowWidth * 0.5f) + config.OffsetXPx;
            float top = centerY - (shadowHeight * 0.5f) + config.OffsetYPx;
            var shadowDst = new SKRect(left, top, left + shadowWidth, top + shadowHeight);

            destinationCanvas.DrawBitmap(sourceBitmap, shadowDst, shadowPaint);
            destinationCanvas.DrawBitmap(sourceBitmap, 0, 0, sourceCopyPaint);
        }

        private ShadowConfig ResolveLightDrivenShadowConfig(Camera camera, int resolution)
        {
            if (!_project.ShadowsEnabled)
            {
                return new ShadowConfig(false, 0f, 0f, 0f, 0f, 1f, 0f);
            }

            // Keep Selected shadow mode stable when exporting.
            _project.EnsureSelection();

            if (!TryGetDominantLightDirection(out Vector3 lightDir, out float lightPower))
            {
                return new ShadowConfig(false, 0f, 0f, 0f, 0f, 1f, 0f);
            }

            float sx = Vector3.Dot(lightDir, camera.Right);
            float sy = -Vector3.Dot(lightDir, camera.Up);
            Vector2 screenDir = new(sx, sy);
            if (screenDir.LengthSquared() <= 1e-8f)
            {
                screenDir = new Vector2(0f, 1f);
            }
            else
            {
                // Shadow falls opposite the incoming light direction.
                screenDir = -Vector2.Normalize(screenDir);
            }

            float viewIncidence = MathF.Abs(Vector3.Dot(lightDir, camera.Forward));
            float planarFactor = MathF.Sqrt(MathF.Max(0f, 1f - (viewIncidence * viewIncidence)));
            float intensityNorm = Math.Clamp(lightPower / 3f, 0.15f, 1.25f);

            float offsetMag = resolution * (0.014f + (0.028f * planarFactor)) * intensityNorm * _project.ShadowDistance;
            float blurPx = resolution * (0.010f + (0.020f * _project.ShadowSoftness * (1f - planarFactor)));
            float opacity = Math.Clamp((0.10f + (0.32f * intensityNorm)) * _project.ShadowStrength, 0f, 0.70f);
            float scale = _project.ShadowScale * (1.0f + (0.03f * planarFactor));

            return new ShadowConfig(
                true,
                opacity,
                blurPx,
                screenDir.X * offsetMag,
                screenDir.Y * offsetMag,
                scale,
                _project.ShadowGray);
        }

        private bool TryGetDominantLightDirection(out Vector3 lightDirection, out float lightPower)
        {
            lightDirection = default;
            lightPower = 0f;

            bool TryEvaluate(KnobLight light, out Vector3 dir, out float weight)
            {
                dir = default;
                weight = 0f;
                float intensity = MathF.Max(0f, light.Intensity);
                if (intensity <= 1e-5f)
                {
                    return false;
                }

                if (light.Type == LightType.Directional)
                {
                    dir = ApplyGizmoOrientation(GetDirectionalVector(light));
                    if (dir.LengthSquared() <= 1e-8f)
                    {
                        return false;
                    }

                    dir = Vector3.Normalize(dir);
                }
                else
                {
                    Vector3 lightPos = ApplyGizmoOrientation(new Vector3(light.X, light.Y, light.Z));
                    if (lightPos.LengthSquared() <= 1e-8f)
                    {
                        return false;
                    }

                    dir = Vector3.Normalize(lightPos);
                }

                float luminance = ((0.2126f * light.Color.Red) + (0.7152f * light.Color.Green) + (0.0722f * light.Color.Blue)) / 255f;
                float diffuse = MathF.Max(0f, light.DiffuseBoost);
                float diffuseTerm = 0.35f + (0.65f * MathF.Pow(diffuse, MathF.Max(0f, _project.ShadowDiffuseInfluence)));
                weight = intensity * diffuseTerm * (0.35f + (0.65f * luminance));
                return weight > 1e-6f;
            }

            if (_project.ShadowMode == ShadowLightMode.Selected)
            {
                KnobLight? selected = _project.SelectedLight;
                if (selected == null || !TryEvaluate(selected, out Vector3 selectedDir, out float selectedWeight))
                {
                    return false;
                }

                lightDirection = selectedDir;
                lightPower = selectedWeight;
                return true;
            }

            if (_project.ShadowMode == ShadowLightMode.Dominant)
            {
                float bestWeight = 0f;
                for (int i = 0; i < _project.Lights.Count; i++)
                {
                    if (!TryEvaluate(_project.Lights[i], out Vector3 dir, out float weight) || weight <= bestWeight)
                    {
                        continue;
                    }

                    bestWeight = weight;
                    lightDirection = dir;
                    lightPower = weight;
                }

                return bestWeight > 0f;
            }

            Vector3 weightedDir = Vector3.Zero;
            float totalWeight = 0f;
            for (int i = 0; i < _project.Lights.Count; i++)
            {
                if (!TryEvaluate(_project.Lights[i], out Vector3 dir, out float weight))
                {
                    continue;
                }

                weightedDir += dir * weight;
                totalWeight += weight;
            }

            if (totalWeight <= 1e-6f || weightedDir.LengthSquared() <= 1e-8f)
            {
                return false;
            }

            lightDirection = Vector3.Normalize(weightedDir);
            lightPower = totalWeight;
            return true;
        }

        private Vector3 ApplyGizmoOrientation(Vector3 value)
        {
            if (_orientation.InvertX)
            {
                value.X = -value.X;
            }

            if (_orientation.InvertY)
            {
                value.Y = -value.Y;
            }

            if (_orientation.InvertZ)
            {
                value.Z = -value.Z;
            }

            return value;
        }

        private static Vector3 GetDirectionalVector(KnobLight light)
        {
            float z = light.Z / 300f;
            Vector3 dir = new(MathF.Cos(light.DirectionRadians), MathF.Sin(light.DirectionRadians), z);
            if (dir.LengthSquared() < 1e-6f)
            {
                return Vector3.UnitZ;
            }

            return Vector3.Normalize(dir);
        }
}
