using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private InspectorFocusState? CaptureInspectorFocusStateForCurrentTab()
        {
            if (_inspectorTabControl?.SelectedItem is not TabItem selectedTab)
            {
                return null;
            }

            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.FocusManager?.GetFocusedElement() is not Control focusedControl)
            {
                return null;
            }

            if (!IsControlInTabSubtree(focusedControl, selectedTab))
            {
                return null;
            }

            string? tabKey = GetInspectorTabKey(selectedTab);
            if (string.IsNullOrWhiteSpace(tabKey) || string.IsNullOrWhiteSpace(focusedControl.Name))
            {
                return null;
            }

            int? selectionStart = null;
            int? selectionEnd = null;
            if (focusedControl is TextBox textBox)
            {
                selectionStart = textBox.SelectionStart;
                selectionEnd = textBox.SelectionEnd;
            }

            return new InspectorFocusState(
                tabKey,
                focusedControl.Name,
                selectionStart,
                selectionEnd);
        }

        private void RestoreInspectorFocusStateForCurrentTab(InspectorFocusState? state)
        {
            if (state == null ||
                _inspectorTabControl?.SelectedItem is not TabItem selectedTab)
            {
                return;
            }

            string? currentTabKey = GetInspectorTabKey(selectedTab);
            if (!string.Equals(currentTabKey, state.TabKey, StringComparison.Ordinal))
            {
                return;
            }

            Control? target = this.FindControl<Control>(state.ControlName);
            if (target == null ||
                !target.IsVisible ||
                !target.IsEnabled ||
                !IsControlInTabSubtree(target, selectedTab))
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (!target.Focus())
                {
                    return;
                }

                if (target is TextBox textBox &&
                    state.SelectionStart.HasValue &&
                    state.SelectionEnd.HasValue)
                {
                    int start = Math.Max(0, state.SelectionStart.Value);
                    int end = Math.Max(start, state.SelectionEnd.Value);
                    textBox.SelectionStart = start;
                    textBox.SelectionEnd = end;
                }
            }, DispatcherPriority.Input);
        }

        private static bool IsControlInTabSubtree(Control control, TabItem tab)
        {
            return ReferenceEquals(control, tab) ||
                   control.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, tab));
        }

        private sealed class InspectorFocusState
        {
            public InspectorFocusState(
                string tabKey,
                string controlName,
                int? selectionStart,
                int? selectionEnd)
            {
                TabKey = tabKey;
                ControlName = controlName;
                SelectionStart = selectionStart;
                SelectionEnd = selectionEnd;
            }

            public string TabKey { get; }
            public string ControlName { get; }
            public int? SelectionStart { get; }
            public int? SelectionEnd { get; }
        }
    }
}
