using Avalonia.Interactivity;
using KnobForge.Core;
using KnobForge.Core.Scene;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private static readonly JsonSerializerOptions ReferenceProfileJsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        private const int DestructiveReferenceProfileConfirmWindowSeconds = 8;
        private string? _pendingProfileDestructiveActionToken;
        private DateTime _pendingProfileDestructiveActionExpiresUtc;

        private static string GetReferenceProfilesPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            return Path.Combine(appData, "KnobForge", "reference-profiles.json");
        }

        private void LoadUserReferenceProfiles()
        {
            _userReferenceProfiles.Clear();
            string path = GetReferenceProfilesPath();
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                UserReferenceProfileStore? store = JsonSerializer.Deserialize<UserReferenceProfileStore>(json, ReferenceProfileJsonOptions);
                if (store?.Profiles == null)
                {
                    return;
                }

                foreach (UserReferenceProfile profile in store.Profiles)
                {
                    string normalizedName = NormalizeProfileName(profile.Name);
                    if (string.IsNullOrWhiteSpace(normalizedName))
                    {
                        continue;
                    }

                    if (_userReferenceProfiles.Any(existing =>
                        string.Equals(existing.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    profile.Name = normalizedName;
                    _userReferenceProfiles.Add(profile);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ReferenceProfiles] Failed to load presets: {ex.Message}");
            }
        }

        private void SaveUserReferenceProfiles()
        {
            try
            {
                string path = GetReferenceProfilesPath();
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                UserReferenceProfileStore store = new()
                {
                    Version = 1,
                    Profiles = _userReferenceProfiles
                        .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };
                string json = JsonSerializer.Serialize(store, ReferenceProfileJsonOptions);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ReferenceProfiles] Failed to save presets: {ex.Message}");
            }
        }

        private void RebuildReferenceStyleOptions()
        {
            if (_referenceStyleCombo == null)
            {
                return;
            }

            _referenceStyleOptions.Clear();
            _referenceStyleOptions.Add(ReferenceStyleOption.CreateGroupLabel("Built-in Styles"));
            foreach (ReferenceKnobStyle style in Enum.GetValues<ReferenceKnobStyle>())
            {
                _referenceStyleOptions.Add(new ReferenceStyleOption(style, null, $"  [Built-in] {style}", true));
            }

            _referenceStyleOptions.Add(ReferenceStyleOption.CreateGroupLabel("User Profiles"));
            List<UserReferenceProfile> userProfiles = _userReferenceProfiles
                .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (userProfiles.Count == 0)
            {
                _referenceStyleOptions.Add(ReferenceStyleOption.CreateGroupLabel("  (none)"));
            }
            else
            {
                foreach (UserReferenceProfile profile in userProfiles)
                {
                    _referenceStyleOptions.Add(new ReferenceStyleOption(null, profile.Name, $"  [User] {profile.Name}", true));
                }
            }

            _referenceStyleCombo.ItemsSource = _referenceStyleOptions.ToList();
        }

        private void SelectReferenceStyleOptionForModel(ModelNode model)
        {
            if (_referenceStyleCombo == null)
            {
                return;
            }

            ReferenceStyleOption? option = null;
            if (model.ReferenceStyle == ReferenceKnobStyle.Custom &&
                !string.IsNullOrWhiteSpace(_selectedUserReferenceProfileName))
            {
                option = _referenceStyleOptions.FirstOrDefault(item =>
                    string.Equals(item.UserProfileName, _selectedUserReferenceProfileName, StringComparison.OrdinalIgnoreCase));
            }

            option ??= _referenceStyleOptions.FirstOrDefault(item => item.BuiltInStyle == model.ReferenceStyle);
            option ??= _referenceStyleOptions.FirstOrDefault(item => item.BuiltInStyle == ReferenceKnobStyle.Custom);
            _referenceStyleCombo.SelectedItem = option;

            if (_referenceStyleSaveNameTextBox != null)
            {
                _referenceStyleSaveNameTextBox.Text = option?.UserProfileName ?? string.Empty;
            }
        }

        private bool TryGetUserReferenceProfile(string profileName, out UserReferenceProfile? profile)
        {
            profile = _userReferenceProfiles.FirstOrDefault(item =>
                string.Equals(item.Name, profileName, StringComparison.OrdinalIgnoreCase));
            return profile is not null;
        }

        private void OnSaveReferenceProfileClicked(object? sender, RoutedEventArgs e)
        {
            if (_referenceStyleSaveNameTextBox == null)
            {
                return;
            }

            ModelNode? model = GetModelNode();
            if (model == null)
            {
                return;
            }

            MaterialNode material = EnsureMaterialNode(model);
            CollarNode? collar = GetCollarNode();
            string profileName = NormalizeProfileName(_referenceStyleSaveNameTextBox.Text ?? string.Empty);
            if (string.IsNullOrWhiteSpace(profileName))
            {
                SetReferenceProfileStatus("Enter a profile name before saving.", isError: true);
                return;
            }

            UserReferenceProfileSnapshot snapshot = CaptureUserReferenceProfileSnapshot(
                _project,
                model,
                material,
                collar,
                includeLightingEnvironmentShadowAndLights: true);
            int existingIndex = _userReferenceProfiles.FindIndex(item =>
                string.Equals(item.Name, profileName, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                _userReferenceProfiles[existingIndex].Name = profileName;
                _userReferenceProfiles[existingIndex].Snapshot = snapshot;
            }
            else
            {
                _userReferenceProfiles.Add(new UserReferenceProfile
                {
                    Name = profileName,
                    Snapshot = snapshot
                });
            }

            _selectedUserReferenceProfileName = profileName;
            model.ReferenceStyle = ReferenceKnobStyle.Custom;
            SaveUserReferenceProfiles();
            RebuildReferenceStyleOptions();
            SelectReferenceStyleOptionForModel(model);
            ResetReferenceProfileDestructiveConfirmation();
            SetReferenceProfileStatus(existingIndex >= 0
                ? $"Quick-saved and replaced '{profileName}'."
                : $"Quick-saved new profile '{profileName}'.");
            NotifyProjectStateChanged();
        }

        private void OnOverwriteReferenceProfileClicked(object? sender, RoutedEventArgs e)
        {
            if (!TryGetSelectedUserReferenceProfile(out UserReferenceProfile? profile) || profile is null)
            {
                SetReferenceProfileStatus("Select a user profile to overwrite.", isError: true);
                return;
            }

            ModelNode? model = GetModelNode();
            if (model == null)
            {
                return;
            }

            if (!ConfirmReferenceProfileDestructiveAction("overwrite", profile.Name))
            {
                return;
            }

            MaterialNode material = EnsureMaterialNode(model);
            CollarNode? collar = GetCollarNode();
            profile.Snapshot = CaptureUserReferenceProfileSnapshot(
                _project,
                model,
                material,
                collar,
                includeLightingEnvironmentShadowAndLights: true);
            _selectedUserReferenceProfileName = profile.Name;
            model.ReferenceStyle = ReferenceKnobStyle.Custom;
            SaveUserReferenceProfiles();
            RebuildReferenceStyleOptions();
            SelectReferenceStyleOptionForModel(model);
            SetReferenceProfileStatus($"Overwrote profile '{profile.Name}' with current settings.");
            NotifyProjectStateChanged();
        }

        private void OnRenameReferenceProfileClicked(object? sender, RoutedEventArgs e)
        {
            if (_referenceStyleSaveNameTextBox == null)
            {
                return;
            }

            if (!TryGetSelectedUserReferenceProfile(out UserReferenceProfile? profile) || profile is null)
            {
                SetReferenceProfileStatus("Select a user profile to rename.", isError: true);
                return;
            }

            string newName = NormalizeProfileName(_referenceStyleSaveNameTextBox.Text ?? string.Empty);
            if (string.IsNullOrWhiteSpace(newName))
            {
                SetReferenceProfileStatus("Enter a new name in the name box before renaming.", isError: true);
                return;
            }

            bool sameName = string.Equals(profile.Name, newName, StringComparison.OrdinalIgnoreCase);
            if (!sameName && _userReferenceProfiles.Any(item =>
                string.Equals(item.Name, newName, StringComparison.OrdinalIgnoreCase)))
            {
                SetReferenceProfileStatus($"Cannot rename to '{newName}' because it already exists.", isError: true);
                return;
            }

            string oldName = profile.Name;
            profile.Name = newName;
            _selectedUserReferenceProfileName = newName;
            SaveUserReferenceProfiles();
            RebuildReferenceStyleOptions();
            if (GetModelNode() is ModelNode model)
            {
                model.ReferenceStyle = ReferenceKnobStyle.Custom;
                SelectReferenceStyleOptionForModel(model);
            }

            ResetReferenceProfileDestructiveConfirmation();
            SetReferenceProfileStatus($"Renamed '{oldName}' to '{newName}'.");
            NotifyProjectStateChanged();
        }

        private void OnDuplicateReferenceProfileClicked(object? sender, RoutedEventArgs e)
        {
            if (_referenceStyleSaveNameTextBox == null)
            {
                return;
            }

            if (!TryGetSelectedUserReferenceProfile(out UserReferenceProfile? profile) || profile is null)
            {
                SetReferenceProfileStatus("Select a user profile to duplicate.", isError: true);
                return;
            }

            string newName = NormalizeProfileName(_referenceStyleSaveNameTextBox.Text ?? string.Empty);
            if (string.IsNullOrWhiteSpace(newName))
            {
                SetReferenceProfileStatus("Enter a new name in the name box before duplicating.", isError: true);
                return;
            }

            if (_userReferenceProfiles.Any(item =>
                string.Equals(item.Name, newName, StringComparison.OrdinalIgnoreCase)))
            {
                SetReferenceProfileStatus($"Cannot duplicate to '{newName}' because it already exists.", isError: true);
                return;
            }

            UserReferenceProfileSnapshot duplicatedSnapshot = CloneSnapshot(profile.Snapshot);
            _userReferenceProfiles.Add(new UserReferenceProfile
            {
                Name = newName,
                Snapshot = duplicatedSnapshot
            });

            _selectedUserReferenceProfileName = newName;
            SaveUserReferenceProfiles();
            RebuildReferenceStyleOptions();
            if (GetModelNode() is ModelNode model)
            {
                model.ReferenceStyle = ReferenceKnobStyle.Custom;
                SelectReferenceStyleOptionForModel(model);
            }

            ResetReferenceProfileDestructiveConfirmation();
            SetReferenceProfileStatus($"Duplicated '{profile.Name}' to '{newName}'.");
            NotifyProjectStateChanged();
        }

        private void OnDeleteReferenceProfileClicked(object? sender, RoutedEventArgs e)
        {
            if (!TryGetSelectedUserReferenceProfile(out UserReferenceProfile? profile) || profile is null)
            {
                SetReferenceProfileStatus("Select a user profile to delete.", isError: true);
                return;
            }

            if (!ConfirmReferenceProfileDestructiveAction("delete", profile.Name))
            {
                return;
            }

            _userReferenceProfiles.RemoveAll(item =>
                string.Equals(item.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(_selectedUserReferenceProfileName, profile.Name, StringComparison.OrdinalIgnoreCase))
            {
                _selectedUserReferenceProfileName = null;
            }

            SaveUserReferenceProfiles();
            RebuildReferenceStyleOptions();
            if (GetModelNode() is ModelNode model)
            {
                SelectReferenceStyleOptionForModel(model);
            }

            SetReferenceProfileStatus($"Deleted profile '{profile.Name}'.");
            NotifyProjectStateChanged();
        }

        private bool TryGetSelectedUserReferenceProfile(out UserReferenceProfile? profile)
        {
            profile = null;
            if (_referenceStyleCombo?.SelectedItem is not ReferenceStyleOption option ||
                !option.IsSelectable ||
                string.IsNullOrWhiteSpace(option.UserProfileName))
            {
                return false;
            }

            return TryGetUserReferenceProfile(option.UserProfileName, out profile);
        }

        private bool ConfirmReferenceProfileDestructiveAction(string action, string profileName)
        {
            string normalizedAction = action.Trim().ToLowerInvariant();
            string token = $"{normalizedAction}:{profileName.Trim()}";
            DateTime utcNow = DateTime.UtcNow;
            if (string.Equals(_pendingProfileDestructiveActionToken, token, StringComparison.Ordinal) &&
                utcNow <= _pendingProfileDestructiveActionExpiresUtc)
            {
                ResetReferenceProfileDestructiveConfirmation();
                return true;
            }

            _pendingProfileDestructiveActionToken = token;
            _pendingProfileDestructiveActionExpiresUtc = utcNow.AddSeconds(DestructiveReferenceProfileConfirmWindowSeconds);
            SetReferenceProfileStatus(
                $"Safety confirm: click {normalizedAction} for '{profileName}' again within {DestructiveReferenceProfileConfirmWindowSeconds}s.",
                isError: true);
            return false;
        }

        private void ResetReferenceProfileDestructiveConfirmation()
        {
            _pendingProfileDestructiveActionToken = null;
            _pendingProfileDestructiveActionExpiresUtc = default;
        }

        private void SetReferenceProfileStatus(string message, bool isError = false)
        {
            if (_referenceProfileStatusText != null)
            {
                _referenceProfileStatusText.Text = message;
                _referenceProfileStatusText.Foreground = isError
                    ? Avalonia.Media.Brushes.IndianRed
                    : Avalonia.Media.Brushes.LightGreen;
            }
        }

        private static UserReferenceProfileSnapshot CloneSnapshot(UserReferenceProfileSnapshot source)
        {
            string json = JsonSerializer.Serialize(source, ReferenceProfileJsonOptions);
            return JsonSerializer.Deserialize<UserReferenceProfileSnapshot>(json, ReferenceProfileJsonOptions) ?? new UserReferenceProfileSnapshot();
        }

        private void UpdateReferenceProfileActionEnablement(bool hasModel)
        {
            bool hasSelectedUserProfile = TryGetSelectedUserReferenceProfile(out _);
            if (_referenceStyleSaveNameTextBox != null)
            {
                _referenceStyleSaveNameTextBox.IsEnabled = hasModel;
            }

            if (_saveReferenceProfileButton != null)
            {
                _saveReferenceProfileButton.IsEnabled = hasModel;
            }

            if (_overwriteReferenceProfileButton != null)
            {
                _overwriteReferenceProfileButton.IsEnabled = hasModel && hasSelectedUserProfile;
            }

            if (_renameReferenceProfileButton != null)
            {
                _renameReferenceProfileButton.IsEnabled = hasModel && hasSelectedUserProfile;
            }

            if (_duplicateReferenceProfileButton != null)
            {
                _duplicateReferenceProfileButton.IsEnabled = hasModel && hasSelectedUserProfile;
            }

            if (_deleteReferenceProfileButton != null)
            {
                _deleteReferenceProfileButton.IsEnabled = hasModel && hasSelectedUserProfile;
            }
        }

        private static string NormalizeProfileName(string value)
        {
            return value.Trim();
        }

        private static MaterialNode EnsureMaterialNode(ModelNode model)
        {
            MaterialNode? material = model.Children.OfType<MaterialNode>().FirstOrDefault();
            if (material is not null)
            {
                return material;
            }

            material = new MaterialNode("DefaultMaterial");
            model.AddChild(material);
            return material;
        }

        private static UserReferenceProfileSnapshot CaptureUserReferenceProfileSnapshot(
            KnobProject project,
            ModelNode model,
            MaterialNode material,
            CollarNode? collar = null,
            bool includeLightingEnvironmentShadowAndLights = false)
        {
            UserReferenceProfileSnapshot snapshot = new()
            {
                Radius = model.Radius,
                Height = model.Height,
                Bevel = model.Bevel,
                TopRadiusScale = model.TopRadiusScale,
                RadialSegments = model.RadialSegments,
                RotationRadians = model.RotationRadians,
                SpiralRidgeHeight = model.SpiralRidgeHeight,
                SpiralRidgeWidth = model.SpiralRidgeWidth,
                SpiralRidgeHeightVariance = model.SpiralRidgeHeightVariance,
                SpiralRidgeWidthVariance = model.SpiralRidgeWidthVariance,
                SpiralHeightVarianceThreshold = model.SpiralHeightVarianceThreshold,
                SpiralWidthVarianceThreshold = model.SpiralWidthVarianceThreshold,
                SpiralTurns = model.SpiralTurns,
                CrownProfile = model.CrownProfile,
                BevelCurve = model.BevelCurve,
                BodyTaper = model.BodyTaper,
                BodyBulge = model.BodyBulge,
                GripType = model.GripType,
                GripStyle = model.GripStyle,
                BodyStyle = model.BodyStyle,
                MountPreset = model.MountPreset,
                GripStart = model.GripStart,
                GripHeight = model.GripHeight,
                GripDensity = model.GripDensity,
                GripPitch = model.GripPitch,
                GripDepth = model.GripDepth,
                GripWidth = model.GripWidth,
                GripSharpness = model.GripSharpness,
                BoreRadiusRatio = model.BoreRadiusRatio,
                BoreDepthRatio = model.BoreDepthRatio,
                CollarWidthRatio = model.CollarWidthRatio,
                CollarHeightRatio = model.CollarHeightRatio,
                IndicatorGrooveDepthRatio = model.IndicatorGrooveDepthRatio,
                IndicatorGrooveWidthDegrees = model.IndicatorGrooveWidthDegrees,
                IndicatorRadiusRatio = model.IndicatorRadiusRatio,
                IndicatorLengthRatio = model.IndicatorLengthRatio,
                IndicatorEnabled = model.IndicatorEnabled,
                IndicatorShape = model.IndicatorShape,
                IndicatorRelief = model.IndicatorRelief,
                IndicatorProfile = model.IndicatorProfile,
                IndicatorWidthRatio = model.IndicatorWidthRatio,
                IndicatorLengthRatioTop = model.IndicatorLengthRatioTop,
                IndicatorPositionRatio = model.IndicatorPositionRatio,
                IndicatorThicknessRatio = model.IndicatorThicknessRatio,
                IndicatorRoundness = model.IndicatorRoundness,
                IndicatorColorX = model.IndicatorColor.X,
                IndicatorColorY = model.IndicatorColor.Y,
                IndicatorColorZ = model.IndicatorColor.Z,
                IndicatorColorBlend = model.IndicatorColorBlend,
                IndicatorCadWallsEnabled = model.IndicatorCadWallsEnabled,
                MaterialBaseColorX = material.BaseColor.X,
                MaterialBaseColorY = material.BaseColor.Y,
                MaterialBaseColorZ = material.BaseColor.Z,
                MaterialMetallic = material.Metallic,
                MaterialRoughness = material.Roughness,
                MaterialPearlescence = material.Pearlescence,
                MaterialRustAmount = material.RustAmount,
                MaterialWearAmount = material.WearAmount,
                MaterialGunkAmount = material.GunkAmount,
                MaterialRadialBrushStrength = material.RadialBrushStrength,
                MaterialRadialBrushDensity = material.RadialBrushDensity,
                MaterialSurfaceCharacter = material.SurfaceCharacter,
                MaterialDiffuseStrength = material.DiffuseStrength,
                MaterialSpecularStrength = material.SpecularStrength,
                MaterialSpecularPower = material.SpecularPower,
                MaterialPartMaterialsEnabled = material.PartMaterialsEnabled,
                MaterialTopBaseColorX = material.TopBaseColor.X,
                MaterialTopBaseColorY = material.TopBaseColor.Y,
                MaterialTopBaseColorZ = material.TopBaseColor.Z,
                MaterialTopMetallic = material.TopMetallic,
                MaterialTopRoughness = material.TopRoughness,
                MaterialBevelBaseColorX = material.BevelBaseColor.X,
                MaterialBevelBaseColorY = material.BevelBaseColor.Y,
                MaterialBevelBaseColorZ = material.BevelBaseColor.Z,
                MaterialBevelMetallic = material.BevelMetallic,
                MaterialBevelRoughness = material.BevelRoughness,
                MaterialSideBaseColorX = material.SideBaseColor.X,
                MaterialSideBaseColorY = material.SideBaseColor.Y,
                MaterialSideBaseColorZ = material.SideBaseColor.Z,
                MaterialSideMetallic = material.SideMetallic,
                MaterialSideRoughness = material.SideRoughness,
                BrushPaintingEnabled = project.BrushPaintingEnabled,
                BrushType = project.BrushType,
                BrushChannel = project.BrushChannel,
                ScratchAbrasionType = project.ScratchAbrasionType,
                BrushSizePx = project.BrushSizePx,
                BrushOpacity = project.BrushOpacity,
                BrushSpread = project.BrushSpread,
                BrushDarkness = project.BrushDarkness,
                PaintCoatMetallic = project.PaintCoatMetallic,
                PaintCoatRoughness = project.PaintCoatRoughness,
                PaintColorX = project.PaintColor.X,
                PaintColorY = project.PaintColor.Y,
                PaintColorZ = project.PaintColor.Z,
                ScratchWidthPx = project.ScratchWidthPx,
                ScratchDepth = project.ScratchDepth,
                ScratchDragResistance = project.ScratchDragResistance,
                ScratchDepthRamp = project.ScratchDepthRamp,
                ScratchExposeColorX = project.ScratchExposeColor.X,
                ScratchExposeColorY = project.ScratchExposeColor.Y,
                ScratchExposeColorZ = project.ScratchExposeColor.Z,
                SpiralNormalInfluenceEnabled = project.SpiralNormalInfluenceEnabled,
                SpiralNormalLodFadeStart = project.SpiralNormalLodFadeStart,
                SpiralNormalLodFadeEnd = project.SpiralNormalLodFadeEnd,
                SpiralRoughnessLodBoost = project.SpiralRoughnessLodBoost,
                CollarSnapshot = collar is null ? null : CaptureCollarStateSnapshot(collar)
            };

            if (includeLightingEnvironmentShadowAndLights)
            {
                snapshot.HasLightingEnvironmentShadowSnapshot = true;
                snapshot.Mode = project.Mode;
                snapshot.EnvironmentTopColorX = project.EnvironmentTopColor.X;
                snapshot.EnvironmentTopColorY = project.EnvironmentTopColor.Y;
                snapshot.EnvironmentTopColorZ = project.EnvironmentTopColor.Z;
                snapshot.EnvironmentBottomColorX = project.EnvironmentBottomColor.X;
                snapshot.EnvironmentBottomColorY = project.EnvironmentBottomColor.Y;
                snapshot.EnvironmentBottomColorZ = project.EnvironmentBottomColor.Z;
                snapshot.EnvironmentIntensity = project.EnvironmentIntensity;
                snapshot.EnvironmentRoughnessMix = project.EnvironmentRoughnessMix;
                snapshot.ShadowsEnabled = project.ShadowsEnabled;
                snapshot.ShadowMode = project.ShadowMode;
                snapshot.ShadowStrength = project.ShadowStrength;
                snapshot.ShadowSoftness = project.ShadowSoftness;
                snapshot.ShadowDistance = project.ShadowDistance;
                snapshot.ShadowScale = project.ShadowScale;
                snapshot.ShadowQuality = project.ShadowQuality;
                snapshot.ShadowGray = project.ShadowGray;
                snapshot.ShadowDiffuseInfluence = project.ShadowDiffuseInfluence;
                snapshot.Lights = project.Lights.Select(CaptureLightState).ToList();
                snapshot.SelectedLightIndex = project.SelectedLightIndex;
            }

            return snapshot;
        }

        private void ApplyUserReferenceProfileSnapshot(
            KnobProject project,
            ModelNode model,
            MaterialNode material,
            UserReferenceProfileSnapshot snapshot,
            CollarNode? collar = null)
        {
            if (snapshot.HasLightingEnvironmentShadowSnapshot)
            {
                project.Mode = snapshot.Mode;
                project.EnvironmentTopColor = new Vector3(
                    snapshot.EnvironmentTopColorX,
                    snapshot.EnvironmentTopColorY,
                    snapshot.EnvironmentTopColorZ);
                project.EnvironmentBottomColor = new Vector3(
                    snapshot.EnvironmentBottomColorX,
                    snapshot.EnvironmentBottomColorY,
                    snapshot.EnvironmentBottomColorZ);
                project.EnvironmentIntensity = snapshot.EnvironmentIntensity;
                project.EnvironmentRoughnessMix = snapshot.EnvironmentRoughnessMix;
                project.ShadowsEnabled = snapshot.ShadowsEnabled;
                project.ShadowMode = snapshot.ShadowMode;
                project.ShadowStrength = snapshot.ShadowStrength;
                project.ShadowSoftness = snapshot.ShadowSoftness;
                project.ShadowDistance = snapshot.ShadowDistance;
                project.ShadowScale = snapshot.ShadowScale;
                project.ShadowQuality = snapshot.ShadowQuality;
                project.ShadowGray = snapshot.ShadowGray;
                project.ShadowDiffuseInfluence = snapshot.ShadowDiffuseInfluence;
                ApplyLightStates(snapshot.Lights, snapshot.SelectedLightIndex);
            }

            model.Radius = snapshot.Radius;
            model.Height = snapshot.Height;
            model.Bevel = snapshot.Bevel;
            model.TopRadiusScale = snapshot.TopRadiusScale;
            model.RadialSegments = snapshot.RadialSegments;
            model.RotationRadians = snapshot.RotationRadians;
            model.SpiralRidgeHeight = snapshot.SpiralRidgeHeight;
            model.SpiralRidgeWidth = snapshot.SpiralRidgeWidth;
            model.SpiralRidgeHeightVariance = snapshot.SpiralRidgeHeightVariance;
            model.SpiralRidgeWidthVariance = snapshot.SpiralRidgeWidthVariance;
            model.SpiralHeightVarianceThreshold = snapshot.SpiralHeightVarianceThreshold;
            model.SpiralWidthVarianceThreshold = snapshot.SpiralWidthVarianceThreshold;
            model.SpiralTurns = snapshot.SpiralTurns;
            model.CrownProfile = snapshot.CrownProfile;
            model.BevelCurve = snapshot.BevelCurve;
            model.BodyTaper = snapshot.BodyTaper;
            model.BodyBulge = snapshot.BodyBulge;
            model.GripType = snapshot.GripType;
            model.GripStyle = snapshot.GripStyle;
            model.BodyStyle = snapshot.BodyStyle;
            model.MountPreset = snapshot.MountPreset;
            model.GripStart = snapshot.GripStart;
            model.GripHeight = snapshot.GripHeight;
            model.GripDensity = snapshot.GripDensity;
            model.GripPitch = snapshot.GripPitch;
            model.GripDepth = snapshot.GripDepth;
            model.GripWidth = snapshot.GripWidth;
            model.GripSharpness = snapshot.GripSharpness;
            model.BoreRadiusRatio = snapshot.BoreRadiusRatio;
            model.BoreDepthRatio = snapshot.BoreDepthRatio;
            model.CollarWidthRatio = snapshot.CollarWidthRatio;
            model.CollarHeightRatio = snapshot.CollarHeightRatio;
            model.IndicatorGrooveDepthRatio = snapshot.IndicatorGrooveDepthRatio;
            model.IndicatorGrooveWidthDegrees = snapshot.IndicatorGrooveWidthDegrees;
            model.IndicatorRadiusRatio = snapshot.IndicatorRadiusRatio;
            model.IndicatorLengthRatio = snapshot.IndicatorLengthRatio;
            model.IndicatorEnabled = snapshot.IndicatorEnabled;
            model.IndicatorShape = snapshot.IndicatorShape;
            model.IndicatorRelief = snapshot.IndicatorRelief;
            model.IndicatorProfile = snapshot.IndicatorProfile;
            model.IndicatorWidthRatio = snapshot.IndicatorWidthRatio;
            model.IndicatorLengthRatioTop = snapshot.IndicatorLengthRatioTop;
            model.IndicatorPositionRatio = snapshot.IndicatorPositionRatio;
            model.IndicatorThicknessRatio = snapshot.IndicatorThicknessRatio;
            model.IndicatorRoundness = snapshot.IndicatorRoundness;
            model.IndicatorColor = new Vector3(snapshot.IndicatorColorX, snapshot.IndicatorColorY, snapshot.IndicatorColorZ);
            model.IndicatorColorBlend = snapshot.IndicatorColorBlend;
            model.IndicatorCadWallsEnabled = snapshot.IndicatorCadWallsEnabled;

            material.BaseColor = new Vector3(snapshot.MaterialBaseColorX, snapshot.MaterialBaseColorY, snapshot.MaterialBaseColorZ);
            material.Metallic = snapshot.MaterialMetallic;
            material.Roughness = snapshot.MaterialRoughness;
            material.Pearlescence = snapshot.MaterialPearlescence;
            material.RustAmount = snapshot.MaterialRustAmount;
            material.WearAmount = snapshot.MaterialWearAmount;
            material.GunkAmount = snapshot.MaterialGunkAmount;
            material.RadialBrushStrength = snapshot.MaterialRadialBrushStrength;
            material.RadialBrushDensity = snapshot.MaterialRadialBrushDensity;
            material.SurfaceCharacter = snapshot.MaterialSurfaceCharacter;
            material.DiffuseStrength = snapshot.MaterialDiffuseStrength;
            material.SpecularStrength = snapshot.MaterialSpecularStrength;
            material.SpecularPower = snapshot.MaterialSpecularPower;
            material.PartMaterialsEnabled = snapshot.MaterialPartMaterialsEnabled;
            material.TopBaseColor = new Vector3(
                snapshot.MaterialTopBaseColorX,
                snapshot.MaterialTopBaseColorY,
                snapshot.MaterialTopBaseColorZ);
            material.TopMetallic = snapshot.MaterialTopMetallic;
            material.TopRoughness = snapshot.MaterialTopRoughness;
            material.BevelBaseColor = new Vector3(
                snapshot.MaterialBevelBaseColorX,
                snapshot.MaterialBevelBaseColorY,
                snapshot.MaterialBevelBaseColorZ);
            material.BevelMetallic = snapshot.MaterialBevelMetallic;
            material.BevelRoughness = snapshot.MaterialBevelRoughness;
            material.SideBaseColor = new Vector3(
                snapshot.MaterialSideBaseColorX,
                snapshot.MaterialSideBaseColorY,
                snapshot.MaterialSideBaseColorZ);
            material.SideMetallic = snapshot.MaterialSideMetallic;
            material.SideRoughness = snapshot.MaterialSideRoughness;

            project.BrushPaintingEnabled = snapshot.BrushPaintingEnabled;
            project.BrushType = snapshot.BrushType;
            project.BrushChannel = snapshot.BrushChannel;
            project.ScratchAbrasionType = snapshot.ScratchAbrasionType;
            project.BrushSizePx = snapshot.BrushSizePx;
            project.BrushOpacity = snapshot.BrushOpacity;
            project.BrushSpread = snapshot.BrushSpread;
            project.BrushDarkness = snapshot.BrushDarkness;
            project.PaintCoatMetallic = snapshot.PaintCoatMetallic;
            project.PaintCoatRoughness = snapshot.PaintCoatRoughness;
            project.PaintColor = new Vector3(snapshot.PaintColorX, snapshot.PaintColorY, snapshot.PaintColorZ);
            project.ScratchWidthPx = snapshot.ScratchWidthPx;
            project.ScratchDepth = snapshot.ScratchDepth;
            project.ScratchDragResistance = snapshot.ScratchDragResistance;
            project.ScratchDepthRamp = snapshot.ScratchDepthRamp;
            project.ScratchExposeColor = new Vector3(
                snapshot.ScratchExposeColorX,
                snapshot.ScratchExposeColorY,
                snapshot.ScratchExposeColorZ);

            project.SpiralNormalInfluenceEnabled = snapshot.SpiralNormalInfluenceEnabled;
            project.SpiralNormalLodFadeStart = snapshot.SpiralNormalLodFadeStart;
            project.SpiralNormalLodFadeEnd = snapshot.SpiralNormalLodFadeEnd;
            project.SpiralRoughnessLodBoost = snapshot.SpiralRoughnessLodBoost;

            if (collar is not null && snapshot.CollarSnapshot is not null)
            {
                ApplyCollarStateSnapshot(collar, snapshot.CollarSnapshot);
            }
        }

        private sealed class UserReferenceProfileStore
        {
            public int Version { get; set; } = 1;
            public List<UserReferenceProfile> Profiles { get; set; } = new();
        }

        private sealed class UserReferenceProfile
        {
            public string Name { get; set; } = string.Empty;
            public UserReferenceProfileSnapshot Snapshot { get; set; } = new();
        }

        private sealed class UserReferenceProfileSnapshot
        {
            public bool HasLightingEnvironmentShadowSnapshot { get; set; }
            public LightingMode Mode { get; set; } = LightingMode.Both;
            public float EnvironmentTopColorX { get; set; } = 0.34f;
            public float EnvironmentTopColorY { get; set; } = 0.36f;
            public float EnvironmentTopColorZ { get; set; } = 0.37f;
            public float EnvironmentBottomColorX { get; set; }
            public float EnvironmentBottomColorY { get; set; }
            public float EnvironmentBottomColorZ { get; set; }
            public float EnvironmentIntensity { get; set; } = 0.36f;
            public float EnvironmentRoughnessMix { get; set; } = 1f;
            public bool ShadowsEnabled { get; set; } = true;
            public ShadowLightMode ShadowMode { get; set; } = ShadowLightMode.Weighted;
            public float ShadowStrength { get; set; } = 1f;
            public float ShadowSoftness { get; set; } = 0.55f;
            public float ShadowDistance { get; set; } = 1f;
            public float ShadowScale { get; set; } = 1f;
            public float ShadowQuality { get; set; } = 0.65f;
            public float ShadowGray { get; set; } = 0.14f;
            public float ShadowDiffuseInfluence { get; set; } = 1f;
            public List<LightStateSnapshot> Lights { get; set; } = new();
            public int SelectedLightIndex { get; set; }
            public float Radius { get; set; } = 220f;
            public float Height { get; set; } = 120f;
            public float Bevel { get; set; } = 18f;
            public float TopRadiusScale { get; set; } = 0.86f;
            public int RadialSegments { get; set; } = 180;
            public float RotationRadians { get; set; }
            public float SpiralRidgeHeight { get; set; } = 19.89f;
            public float SpiralRidgeWidth { get; set; } = 18.92f;
            public float SpiralRidgeHeightVariance { get; set; } = 0.15f;
            public float SpiralRidgeWidthVariance { get; set; } = 0.12f;
            public float SpiralHeightVarianceThreshold { get; set; } = 0.45f;
            public float SpiralWidthVarianceThreshold { get; set; } = 0.45f;
            public float SpiralTurns { get; set; } = 150f;
            public float CrownProfile { get; set; }
            public float BevelCurve { get; set; } = 1f;
            public float BodyTaper { get; set; }
            public float BodyBulge { get; set; }
            public GripType GripType { get; set; } = GripType.None;
            public GripStyle GripStyle { get; set; } = GripStyle.BoutiqueSynthPremium;
            public BodyStyle BodyStyle { get; set; } = BodyStyle.Straight;
            public MountPreset MountPreset { get; set; } = MountPreset.Custom;
            public float GripStart { get; set; } = 0.15f;
            public float GripHeight { get; set; } = 0.55f;
            public float GripDensity { get; set; } = 60f;
            public float GripPitch { get; set; } = 6f;
            public float GripDepth { get; set; } = 1.2f;
            public float GripWidth { get; set; } = 1.2f;
            public float GripSharpness { get; set; } = 1f;
            public float BoreRadiusRatio { get; set; }
            public float BoreDepthRatio { get; set; }
            public float CollarWidthRatio { get; set; }
            public float CollarHeightRatio { get; set; }
            public float IndicatorGrooveDepthRatio { get; set; }
            public float IndicatorGrooveWidthDegrees { get; set; } = 7f;
            public float IndicatorRadiusRatio { get; set; } = 0.70f;
            public float IndicatorLengthRatio { get; set; } = 0.22f;
            public bool IndicatorEnabled { get; set; } = true;
            public IndicatorShape IndicatorShape { get; set; } = IndicatorShape.Bar;
            public IndicatorRelief IndicatorRelief { get; set; } = IndicatorRelief.Extrude;
            public IndicatorProfile IndicatorProfile { get; set; } = IndicatorProfile.Straight;
            public float IndicatorWidthRatio { get; set; } = 0.06f;
            public float IndicatorLengthRatioTop { get; set; } = 0.28f;
            public float IndicatorPositionRatio { get; set; } = 0.46f;
            public float IndicatorThicknessRatio { get; set; } = 0.012f;
            public float IndicatorRoundness { get; set; }
            public float IndicatorColorX { get; set; } = 0.97f;
            public float IndicatorColorY { get; set; } = 0.96f;
            public float IndicatorColorZ { get; set; } = 0.92f;
            public float IndicatorColorBlend { get; set; } = 1f;
            public bool IndicatorCadWallsEnabled { get; set; } = true;
            public float MaterialBaseColorX { get; set; } = 0.55f;
            public float MaterialBaseColorY { get; set; } = 0.16f;
            public float MaterialBaseColorZ { get; set; } = 0.16f;
            public float MaterialMetallic { get; set; } = 1f;
            public float MaterialRoughness { get; set; } = 0.04f;
            public float MaterialPearlescence { get; set; }
            public float MaterialRustAmount { get; set; }
            public float MaterialWearAmount { get; set; }
            public float MaterialGunkAmount { get; set; }
            public float MaterialRadialBrushStrength { get; set; } = 0.65f;
            public float MaterialRadialBrushDensity { get; set; } = 280.5f;
            public float MaterialSurfaceCharacter { get; set; } = 1f;
            public float MaterialSpecularPower { get; set; } = 64f;
            public float MaterialDiffuseStrength { get; set; } = 1f;
            public float MaterialSpecularStrength { get; set; } = 1f;
            public bool MaterialPartMaterialsEnabled { get; set; }
            public float MaterialTopBaseColorX { get; set; } = 0.55f;
            public float MaterialTopBaseColorY { get; set; } = 0.16f;
            public float MaterialTopBaseColorZ { get; set; } = 0.16f;
            public float MaterialTopMetallic { get; set; } = 1f;
            public float MaterialTopRoughness { get; set; } = 0.04f;
            public float MaterialBevelBaseColorX { get; set; } = 0.55f;
            public float MaterialBevelBaseColorY { get; set; } = 0.16f;
            public float MaterialBevelBaseColorZ { get; set; } = 0.16f;
            public float MaterialBevelMetallic { get; set; } = 1f;
            public float MaterialBevelRoughness { get; set; } = 0.04f;
            public float MaterialSideBaseColorX { get; set; } = 0.55f;
            public float MaterialSideBaseColorY { get; set; } = 0.16f;
            public float MaterialSideBaseColorZ { get; set; } = 0.16f;
            public float MaterialSideMetallic { get; set; } = 1f;
            public float MaterialSideRoughness { get; set; } = 0.04f;
            public bool BrushPaintingEnabled { get; set; }
            public PaintBrushType BrushType { get; set; } = PaintBrushType.Spray;
            public PaintChannel BrushChannel { get; set; } = PaintChannel.Rust;
            public ScratchAbrasionType ScratchAbrasionType { get; set; } = ScratchAbrasionType.Needle;
            public float BrushSizePx { get; set; } = 32f;
            public float BrushOpacity { get; set; } = 0.5f;
            public float BrushSpread { get; set; } = 0.35f;
            public float BrushDarkness { get; set; } = 0.58f;
            public float PaintCoatMetallic { get; set; } = 0.02f;
            public float PaintCoatRoughness { get; set; } = 0.56f;
            public float PaintColorX { get; set; } = 0.85f;
            public float PaintColorY { get; set; } = 0.24f;
            public float PaintColorZ { get; set; } = 0.24f;
            public float ScratchWidthPx { get; set; } = 20f;
            public float ScratchDepth { get; set; } = 0.45f;
            public float ScratchDragResistance { get; set; } = 0.38f;
            public float ScratchDepthRamp { get; set; } = 0.0015f;
            public float ScratchExposeColorX { get; set; } = 0.88f;
            public float ScratchExposeColorY { get; set; } = 0.88f;
            public float ScratchExposeColorZ { get; set; } = 0.90f;
            public bool SpiralNormalInfluenceEnabled { get; set; } = true;
            public float SpiralNormalLodFadeStart { get; set; } = 4.22f;
            public float SpiralNormalLodFadeEnd { get; set; } = 4.23f;
            public float SpiralRoughnessLodBoost { get; set; } = 0.78f;
            public CollarStateSnapshot? CollarSnapshot { get; set; }
        }

        private sealed class ReferenceStyleOption
        {
            public ReferenceStyleOption(ReferenceKnobStyle? builtInStyle, string? userProfileName, string displayName, bool isSelectable = true)
            {
                BuiltInStyle = builtInStyle;
                UserProfileName = userProfileName;
                DisplayName = displayName;
                IsSelectable = isSelectable;
            }

            public ReferenceKnobStyle? BuiltInStyle { get; }
            public string? UserProfileName { get; }
            public string DisplayName { get; }
            public bool IsSelectable { get; }

            public static ReferenceStyleOption CreateGroupLabel(string displayName)
            {
                return new ReferenceStyleOption(null, null, displayName, false);
            }

            public override string ToString() => DisplayName;
        }
    }
}
