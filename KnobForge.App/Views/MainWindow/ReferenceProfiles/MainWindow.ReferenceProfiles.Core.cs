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
        private const string DefaultUserReferenceProfileName = "goodone";
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

                ApplyDefaultUserReferenceProfileIfAvailable();
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

        private void ApplyDefaultUserReferenceProfileIfAvailable()
        {
            UserReferenceProfile? defaultProfile = _userReferenceProfiles.FirstOrDefault(profile =>
                string.Equals(profile.Name, DefaultUserReferenceProfileName, StringComparison.OrdinalIgnoreCase));
            if (defaultProfile is null)
            {
                return;
            }

            ModelNode? model = GetModelNode();
            if (model is null)
            {
                return;
            }

            MaterialNode material = EnsureMaterialNode(model);
            CollarNode? collar = defaultProfile.Snapshot.CollarSnapshot is not null
                ? EnsureCollarNode()
                : GetCollarNode();

            _selectedUserReferenceProfileName = defaultProfile.Name;
            model.ReferenceStyle = ReferenceKnobStyle.Custom;
            ApplyUserReferenceProfileSnapshot(_project, model, material, defaultProfile.Snapshot, collar);

            if (_referenceStyleSaveNameTextBox != null)
            {
                _referenceStyleSaveNameTextBox.Text = defaultProfile.Name;
            }
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


    }
}
