using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using KnobForge.App.ProjectFiles;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace KnobForge.App.Views
{
    public partial class ProjectLauncherWindow : Window
    {
        private readonly Button _newProjectButton;
        private readonly Button _openSelectedProjectButton;
        private readonly Button _browseProjectButton;
        private readonly ListBox _projectListBox;
        private readonly TextBlock _statusTextBlock;
        private readonly ObservableCollection<ProjectCard> _projectCards = new();

        public ProjectLauncherWindow()
        {
            InitializeComponent();

            _newProjectButton = this.FindControl<Button>("NewProjectButton")
                ?? throw new InvalidOperationException("NewProjectButton not found.");
            _openSelectedProjectButton = this.FindControl<Button>("OpenSelectedProjectButton")
                ?? throw new InvalidOperationException("OpenSelectedProjectButton not found.");
            _browseProjectButton = this.FindControl<Button>("BrowseProjectButton")
                ?? throw new InvalidOperationException("BrowseProjectButton not found.");
            _projectListBox = this.FindControl<ListBox>("ProjectListBox")
                ?? throw new InvalidOperationException("ProjectListBox not found.");
            _statusTextBlock = this.FindControl<TextBlock>("LauncherStatusTextBlock")
                ?? throw new InvalidOperationException("LauncherStatusTextBlock not found.");

            _projectListBox.ItemsSource = _projectCards;
            _projectListBox.SelectionChanged += OnProjectSelectionChanged;
            _projectListBox.DoubleTapped += OnProjectListDoubleTapped;
            _newProjectButton.Click += OnNewProjectButtonClicked;
            _openSelectedProjectButton.Click += OnOpenSelectedProjectButtonClicked;
            _browseProjectButton.Click += OnBrowseProjectButtonClicked;
            Opened += OnLauncherOpened;

            UpdateSelectionActions();
        }

        public event Action<ProjectLauncherResult>? LaunchRequested;

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnLauncherOpened(object? sender, EventArgs e)
        {
            ReloadProjects();
        }

        private void ReloadProjects()
        {
            DisposeThumbnails();
            _projectCards.Clear();

            IReadOnlyList<KnobProjectLauncherEntry> entries = KnobProjectFileStore.GetLauncherEntries();
            foreach (KnobProjectLauncherEntry entry in entries)
            {
                _projectCards.Add(new ProjectCard(entry));
            }

            if (_projectCards.Count == 0)
            {
                _statusTextBlock.Text = "No saved projects yet. Click New Project to get started.";
            }
            else
            {
                _statusTextBlock.Text = $"Showing {_projectCards.Count} project(s).";
            }

            if (_projectCards.Count > 0)
            {
                _projectListBox.SelectedIndex = 0;
            }

            UpdateSelectionActions();
        }

        private void OnProjectSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            UpdateSelectionActions();
        }

        private void OnProjectListDoubleTapped(object? sender, RoutedEventArgs e)
        {
            if (_projectListBox.SelectedItem is ProjectCard card)
            {
                LaunchRequested?.Invoke(new ProjectLauncherResult(card.FilePath));
            }
        }

        private void OnNewProjectButtonClicked(object? sender, RoutedEventArgs e)
        {
            LaunchRequested?.Invoke(new ProjectLauncherResult(null));
        }

        private void OnOpenSelectedProjectButtonClicked(object? sender, RoutedEventArgs e)
        {
            if (_projectListBox.SelectedItem is not ProjectCard card)
            {
                return;
            }

            LaunchRequested?.Invoke(new ProjectLauncherResult(card.FilePath));
        }

        private async void OnBrowseProjectButtonClicked(object? sender, RoutedEventArgs e)
        {
            FilePickerOpenOptions options = new()
            {
                AllowMultiple = false,
                Title = "Open KnobForge Project",
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("KnobForge Project")
                    {
                        Patterns = new[] { $"*{KnobProjectFileStore.FileExtension}" }
                    }
                }
            };

            string suggestedFolder = KnobProjectFileStore.EnsureDefaultProjectsDirectory();
            if (Directory.Exists(suggestedFolder))
            {
                var folder = await StorageProvider.TryGetFolderFromPathAsync(suggestedFolder);
                if (folder != null)
                {
                    options.SuggestedStartLocation = folder;
                }
            }

            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(options);
            if (files.Count == 0)
            {
                return;
            }

            string? path = files[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                path = files[0].Path.LocalPath;
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                LaunchRequested?.Invoke(new ProjectLauncherResult(path));
            }
        }

        private void UpdateSelectionActions()
        {
            _openSelectedProjectButton.IsEnabled = _projectListBox.SelectedItem is ProjectCard;
        }

        protected override void OnClosed(EventArgs e)
        {
            DisposeThumbnails();
            base.OnClosed(e);
        }

        private void DisposeThumbnails()
        {
            foreach (ProjectCard card in _projectCards)
            {
                card.Dispose();
            }
        }

        public sealed class ProjectLauncherResult
        {
            public ProjectLauncherResult(string? projectPath)
            {
                ProjectPath = projectPath;
            }

            public string? ProjectPath { get; }
            public bool IsNewProject => string.IsNullOrWhiteSpace(ProjectPath);
        }

        private sealed class ProjectCard : IDisposable
        {
            public ProjectCard(KnobProjectLauncherEntry entry)
            {
                FilePath = entry.FilePath;
                DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName)
                    ? Path.GetFileNameWithoutExtension(entry.FilePath)
                    : entry.DisplayName.Trim();
                DateTime saved = entry.SavedUtc.ToUniversalTime();
                SavedDisplay = saved == DateTime.MinValue
                    ? "Saved: unknown"
                    : $"Saved: {saved.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture)}";
                Thumbnail = KnobProjectFileStore.TryDecodeThumbnail(entry.ThumbnailPngBase64);
            }

            public string FilePath { get; }
            public string DisplayName { get; }
            public string SavedDisplay { get; }
            public Bitmap? Thumbnail { get; }

            public void Dispose()
            {
                Thumbnail?.Dispose();
            }
        }
    }
}
