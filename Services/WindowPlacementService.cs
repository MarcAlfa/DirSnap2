using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using DirSnap2.Models;

namespace DirSnap2.Services;

public static class WindowPlacementService
{
    private static RectInt32 _lastNormalBounds;

    public static void RestoreWindow(AppWindow appWindow, WindowPlacement placement)
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

            var targetRect = new RectInt32(placement.X, placement.Y, placement.Width, placement.Height);
            var displayAtRect = DisplayArea.GetFromRect(targetRect, DisplayAreaFallback.None);

            if (displayAtRect != null)
            {
                appWindow.MoveAndResize(targetRect);
            }
            else if (targetDisplay != null)
            {
                var work = targetDisplay.WorkArea;
                int w = Math.Min(placement.Width, work.Width - 40);
                int h = Math.Min(placement.Height, work.Height - 40);
                int x = work.X + Math.Max(0, (work.Width - w) / 2);
                int y = work.Y + Math.Max(0, (work.Height - h) / 2);
                appWindow.MoveAndResize(new RectInt32(x, y, w, h));
            }
            else
            {
                var primary = DisplayArea.Primary;
                int w = Math.Min(placement.Width, primary.WorkArea.Width - 40);
                int h = Math.Min(placement.Height, primary.WorkArea.Height - 40);
                int x = primary.WorkArea.X + (primary.WorkArea.Width - w) / 2;
                int y = primary.WorkArea.Y + (primary.WorkArea.Height - h) / 2;
                appWindow.MoveAndResize(new RectInt32(x, y, w, h));
            }

            _lastNormalBounds = new RectInt32(appWindow.Position.X, appWindow.Position.Y, appWindow.Size.Width, appWindow.Size.Height);

            appWindow.Changed += (sender, args) =>
            {
                if (!(args.DidPositionChange || args.DidSizeChange)) return;
                if (sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
                {
                    _lastNormalBounds = new RectInt32(sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height);
                }
            };

            if (placement.Maximized)
            {
                if (appWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.Maximize();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowPlacementService] RestoreWindow error: {ex.Message}");
        }
    }

    public static void SaveWindow(AppWindow appWindow, WindowPlacement placement)
    {
        try
        {
            if (appWindow.Presenter is not OverlappedPresenter presenter)
                return;

            if (presenter.State == OverlappedPresenterState.Minimized)
                return;

            placement.Maximized = presenter.State == OverlappedPresenterState.Maximized;

            var bounds = placement.Maximized ? _lastNormalBounds : new RectInt32(appWindow.Position.X, appWindow.Position.Y, appWindow.Size.Width, appWindow.Size.Height);

            if (bounds.Width >= 800 && bounds.Height >= 500)
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
            Debug.WriteLine($"[WindowPlacementService] SaveWindow error: {ex.Message}");
        }
    }
}
