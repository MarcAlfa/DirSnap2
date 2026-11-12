using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using DirSnap2.Models;

namespace DirSnap2.Services;

/// <summary>
/// Saves and restores position, size, maximized state and monitor of a window.
/// Works for any number of windows (main window, detached AI Assistant...).
/// </summary>
public static class WindowPlacementService
{
    // Last "restored" (not maximized) bounds of each window, used when it is saved while maximized
    private static readonly Dictionary<ulong, RectInt32> LastNormalBounds = new();

    public static void RestoreWindow(AppWindow appWindow, WindowPlacement placement, int minWidth = 800, int minHeight = 500)
    {
        try
        {
            var displays = DisplayArea.FindAll();
            DisplayArea? targetDisplay = null;

            if (displays.Count > 0)
            {
                if (placement.MonitorIndex >= 0 && placement.MonitorIndex < displays.Count)
                {
                    targetDisplay = displays[placement.MonitorIndex];
                }
                else
                {
                    targetDisplay = DisplayArea.Primary;
                }
            }

            int width = Math.Max(placement.Width, minWidth);
            int height = Math.Max(placement.Height, minHeight);
            var targetRect = new RectInt32(placement.X, placement.Y, width, height);
            var displayAtRect = DisplayArea.GetFromRect(targetRect, DisplayAreaFallback.None);

            if (displayAtRect != null)
            {
                appWindow.MoveAndResize(targetRect);
            }
            else
            {
                // Saved monitor gone or position off-screen: center on the saved (or primary) monitor
                var work = (targetDisplay ?? DisplayArea.Primary).WorkArea;
                int w = Math.Min(width, work.Width - 40);
                int h = Math.Min(height, work.Height - 40);
                int x = work.X + Math.Max(0, (work.Width - w) / 2);
                int y = work.Y + Math.Max(0, (work.Height - h) / 2);
                appWindow.MoveAndResize(new RectInt32(x, y, w, h));
            }

            TrackNormalBounds(appWindow);

            if (placement.Maximized && appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[WindowPlacementService] RestoreWindow error: {ex.Message}", ex);
        }
    }

    /// <summary>Remembers the restored bounds while the window moves, so a maximized window saves its normal size.</summary>
    public static void TrackNormalBounds(AppWindow appWindow)
    {
        ulong id = appWindow.Id.Value;
        bool firstTime = !LastNormalBounds.ContainsKey(id);
        LastNormalBounds[id] = new RectInt32(appWindow.Position.X, appWindow.Position.Y, appWindow.Size.Width, appWindow.Size.Height);
        if (!firstTime)
            return;

        appWindow.Changed += (sender, args) =>
        {
            if (!(args.DidPositionChange || args.DidSizeChange)) return;
            if (sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
            {
                LastNormalBounds[sender.Id.Value] = new RectInt32(sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height);
            }
        };
    }

    public static void SaveWindow(AppWindow appWindow, WindowPlacement placement, int minWidth = 800, int minHeight = 500)
    {
        try
        {
            if (appWindow.Presenter is not OverlappedPresenter presenter)
                return;

            if (presenter.State == OverlappedPresenterState.Minimized)
                return;

            placement.Maximized = presenter.State == OverlappedPresenterState.Maximized;

            var current = new RectInt32(appWindow.Position.X, appWindow.Position.Y, appWindow.Size.Width, appWindow.Size.Height);
            var bounds = placement.Maximized && LastNormalBounds.TryGetValue(appWindow.Id.Value, out var normal)
                ? normal
                : current;

            if (bounds.Width >= minWidth && bounds.Height >= minHeight)
            {
                placement.X = bounds.X;
                placement.Y = bounds.Y;
                placement.Width = bounds.Width;
                placement.Height = bounds.Height;
            }

            // Find monitor index
            var displays = DisplayArea.FindAll();
            var currentDisplay = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);

            for (int i = 0; i < displays.Count; i++)
            {
                if (displays[i].DisplayId.Value == currentDisplay.DisplayId.Value)
                {
                    placement.MonitorIndex = i;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[WindowPlacementService] SaveWindow error: {ex.Message}", ex);
        }
    }
}
