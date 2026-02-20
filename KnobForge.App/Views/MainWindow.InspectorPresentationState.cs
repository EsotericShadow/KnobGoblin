using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private readonly Dictionary<string, InspectorPresentationState> _inspectorPresentationStates = new(StringComparer.Ordinal);

        private void RememberInspectorPresentationStateForCurrentTab()
        {
            if (_inspectorTabControl?.SelectedItem is not TabItem selectedTab)
            {
                return;
            }

            InspectorPresentationState? state = CaptureInspectorPresentationState(selectedTab);
            if (state == null)
            {
                return;
            }

            _inspectorPresentationStates[state.TabKey] = state;
        }

        private void RestoreInspectorPresentationStateForCurrentTab()
        {
            if (_inspectorTabControl?.SelectedItem is not TabItem selectedTab)
            {
                return;
            }

            string? tabKey = GetInspectorTabKey(selectedTab);
            if (string.IsNullOrWhiteSpace(tabKey) ||
                !_inspectorPresentationStates.TryGetValue(tabKey, out InspectorPresentationState? state))
            {
                return;
            }

            ApplyInspectorPresentationState(selectedTab, state);
        }

        private static InspectorPresentationState? CaptureInspectorPresentationState(TabItem tab)
        {
            string? tabKey = GetInspectorTabKey(tab);
            if (string.IsNullOrWhiteSpace(tabKey))
            {
                return null;
            }

            List<Expander> expanders = GetTabExpanders(tab);
            ScrollViewer? scrollViewer = GetTabScrollViewer(tab);
            double verticalOffset = scrollViewer?.Offset.Y ?? 0d;
            var expanderStates = new List<bool>(expanders.Count);
            for (int i = 0; i < expanders.Count; i++)
            {
                expanderStates.Add(expanders[i].IsExpanded);
            }

            return new InspectorPresentationState(tabKey, verticalOffset, expanderStates);
        }

        private static void ApplyInspectorPresentationState(TabItem tab, InspectorPresentationState state)
        {
            List<Expander> expanders = GetTabExpanders(tab);
            int expanderCount = Math.Min(expanders.Count, state.ExpanderStates.Count);
            for (int i = 0; i < expanderCount; i++)
            {
                bool desired = state.ExpanderStates[i];
                if (expanders[i].IsExpanded != desired)
                {
                    expanders[i].IsExpanded = desired;
                }
            }

            ScrollViewer? scrollViewer = GetTabScrollViewer(tab);
            if (scrollViewer == null)
            {
                return;
            }

            double requestedOffsetY = Math.Max(0d, state.VerticalOffset);
            Dispatcher.UIThread.Post(
                () => SetScrollViewerVerticalOffset(scrollViewer, requestedOffsetY),
                DispatcherPriority.Background);
            Dispatcher.UIThread.Post(
                () => SetScrollViewerVerticalOffset(scrollViewer, requestedOffsetY),
                DispatcherPriority.Render);
        }

        private static void SetScrollViewerVerticalOffset(ScrollViewer scrollViewer, double offsetY)
        {
            Vector currentOffset = scrollViewer.Offset;
            if (Math.Abs(currentOffset.Y - offsetY) <= 0.5d)
            {
                return;
            }

            scrollViewer.Offset = new Vector(currentOffset.X, offsetY);
        }

        private static List<Expander> GetTabExpanders(TabItem tab)
        {
            return EnumerateVisualDescendants(tab)
                .OfType<Expander>()
                .ToList();
        }

        private static ScrollViewer? GetTabScrollViewer(TabItem tab)
        {
            return EnumerateVisualDescendants(tab)
                .OfType<ScrollViewer>()
                .FirstOrDefault();
        }

        private static IEnumerable<Visual> EnumerateVisualDescendants(Visual root)
        {
            foreach (Visual child in root.GetVisualChildren())
            {
                yield return child;
                foreach (Visual descendant in EnumerateVisualDescendants(child))
                {
                    yield return descendant;
                }
            }
        }

        private static string? GetInspectorTabKey(TabItem tab)
        {
            if (!string.IsNullOrWhiteSpace(tab.Name))
            {
                return tab.Name;
            }

            if (tab.Header is string header && !string.IsNullOrWhiteSpace(header))
            {
                return header.Trim();
            }

            return tab.Header?.ToString();
        }

        private sealed class InspectorPresentationState
        {
            public InspectorPresentationState(string tabKey, double verticalOffset, List<bool> expanderStates)
            {
                TabKey = tabKey;
                VerticalOffset = verticalOffset;
                ExpanderStates = expanderStates;
            }

            public string TabKey { get; }
            public double VerticalOffset { get; }
            public List<bool> ExpanderStates { get; }
        }
    }
}
