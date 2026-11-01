using Avalonia.Controls;
using SbMac.App.Models;
using SbMac.App.Services;

namespace SbMac.App.Views;

/// <summary>
/// Sidebar width, details height, and the details/activity collapsed flags. Applied once
/// when the saved preferences are attached; captured through the same debounced save as
/// window placement.
/// </summary>
public partial class MainWindow
{
    // The message table and the sidebar's usable minimums; clamping never squeezes past these.
    const double MinimumTableHeight = 220;
    const double MinimumTableWidth = 420;

    bool applyingSavedPanels;

    ColumnDefinition SidebarColumn => MainSplitLayout.ColumnDefinitions[0];

    RowDefinition DetailsRow => MessageDetailLayout.RowDefinitions[3];

    void ApplySavedPanels(WorkspacePreferences saved)
    {
        applyingSavedPanels = true;
        try
        {
            SidebarColumn.Width = new GridLength(saved.SidebarWidth, GridUnitType.Pixel);
            if (ViewModel is { } viewModel)
            {
                // Hiding captures the current row heights, so the saved height is set after it.
                viewModel.IsInspectorVisible = !saved.DetailsCollapsed;
                viewModel.IsActivityExpanded = !saved.ActivityCollapsed;
            }
            if (saved.DetailsHeight is { } height)
                inspectorHeight = new GridLength(height, GridUnitType.Pixel);
            if (!saved.DetailsCollapsed)
                UpdateInspectorVisibility(visible: true, moveFocus: false);
        }
        finally
        {
            applyingSavedPanels = false;
        }
    }

    /// <summary>Captures the sidebar and details sizes after a splitter drag.</summary>
    internal void CapturePanelLayout()
    {
        var sidebar = SidebarColumn.ActualWidth;
        var details = DetailsRow.ActualHeight;
        SavePanels(workspace => workspace with
        {
            SidebarWidth = sidebar > 0 ? sidebar : workspace.SidebarWidth,
            DetailsHeight = details > 0 ? details : workspace.DetailsHeight
        });
    }

    /// <summary>Captures the collapsed flags whenever either panel is toggled.</summary>
    void CapturePanelVisibility()
    {
        if (ViewModel is not { } viewModel) return;
        SavePanels(workspace => workspace with
        {
            DetailsCollapsed = !viewModel.IsInspectorVisible,
            ActivityCollapsed = !viewModel.IsActivityExpanded
        });
    }

    void SavePanels(Func<WorkspacePreferences, WorkspacePreferences> change)
    {
        if (workspacePreferences is null || applyingSavedPanels) return;
        SaveWorkspace(change(lastWorkspace ?? WorkspacePreferences.Default));
    }

    /// <summary>Re-fits pixel panels when the window resizes so the message table keeps its minimum.</summary>
    void ClampPanelsToLayout()
    {
        var width = MainSplitLayout.Bounds.Width;
        if (width > 0 && SidebarColumn.Width.IsAbsolute)
        {
            var maximum = Math.Max(WorkspacePreferences.MinSidebarWidth, width - MinimumTableWidth);
            SidebarColumn.Width = new GridLength(Math.Clamp(SidebarColumn.Width.Value,
                WorkspacePreferences.MinSidebarWidth, Math.Min(WorkspacePreferences.MaxSidebarWidth, maximum)), GridUnitType.Pixel);
        }

        var height = MessageDetailLayout.Bounds.Height;
        if (height > 0 && DetailsRow.Height.IsAbsolute && DetailsRow.Height.Value > 0)
        {
            // The splitter's ActualHeight is 0 while the inspector is hidden, so use its declared height.
            var reserved = MessageDetailLayout.RowDefinitions[0].ActualHeight + DetailsSplitter.Height;
            var maximum = Math.Max(WorkspacePreferences.MinDetailsHeight, height - reserved - MinimumTableHeight);
            DetailsRow.Height = new GridLength(Math.Clamp(DetailsRow.Height.Value,
                WorkspacePreferences.MinDetailsHeight, maximum), GridUnitType.Pixel);
        }
    }
}
