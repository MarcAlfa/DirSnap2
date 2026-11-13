using System.Diagnostics;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using DirSnap2.Models;
using DirSnap2.Services;

namespace DirSnap2.Views;

public sealed class SnapListWindow : Window
{
    public ISnapListService SnapListService { get; }
    private readonly Action _onClosedCallback;
    private readonly ListView _listView;

    public SnapListWindow(ISnapListService snapListService, Action onClosedCallback)
    {
        SnapListService = snapListService;
        _onClosedCallback = onClosedCallback;

        Title = "📋 Snap List";
        TryApplyMicaBackdrop();
        ConfigureAppWindow();

        // Build UI Root
        var rootGrid = new Grid
        {
            Background = (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"]
        };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Header
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: Toggles
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 2: List
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3: Toolbar

        // Row 0: Header
        var headerBorder = new Border
        {
            Background = (Brush)Application.Current.Resources["AppBackgroundBrush"],
            BorderBrush = (Brush)Application.Current.Resources["AppBorderBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 10, 14, 10)
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerTitleStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        headerTitleStack.Children.Add(new TextBlock
        {
            Text = "📋 Snap List",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center
        });

        var countBadge = new Border
        {
            Background = (Brush)Application.Current.Resources["AppAccentBrush"],
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        var countText = new TextBlock
        {
            Text = SnapListService.Count.ToString(),
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        };
        SnapListService.ItemsChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() => countText.Text = SnapListService.Count.ToString());
        };
        countBadge.Child = countText;
        headerTitleStack.Children.Add(countBadge);
        Grid.SetColumn(headerTitleStack, 0);
        headerGrid.Children.Add(headerTitleStack);

        var clearBtn = new Button
        {
            Padding = new Thickness(8, 4, 8, 4),
            FontSize = 11,
            Content = "Clear All"
        };
        clearBtn.Click += (_, _) => SnapListService.Clear();
        Grid.SetColumn(clearBtn, 1);
        headerGrid.Children.Add(clearBtn);

        headerBorder.Child = headerGrid;
        Grid.SetRow(headerBorder, 0);
        rootGrid.Children.Add(headerBorder);

        // Row 1: Toggles
        var togglesBorder = new Border
        {
            Background = (Brush)Application.Current.Resources["AppSurfaceBrush"],
            BorderBrush = (Brush)Application.Current.Resources["AppBorderBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 8, 14, 8)
        };
        var togglesStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        var fullPathCheck = new CheckBox { Content = "Full path", IsChecked = SnapListService.IncludeFullPath, FontSize = 12 };
        fullPathCheck.Checked += (_, _) => SnapListService.IncludeFullPath = true;
        fullPathCheck.Unchecked += (_, _) => SnapListService.IncludeFullPath = false;

        var extCheck = new CheckBox { Content = "With extension", IsChecked = SnapListService.IncludeExtension, FontSize = 12 };
        extCheck.Checked += (_, _) => SnapListService.IncludeExtension = true;
        extCheck.Unchecked += (_, _) => SnapListService.IncludeExtension = false;

        togglesStack.Children.Add(fullPathCheck);
        togglesStack.Children.Add(extCheck);
        togglesBorder.Child = togglesStack;
        Grid.SetRow(togglesBorder, 1);
        rootGrid.Children.Add(togglesBorder);

        // Row 2: List View
        _listView = new ListView
        {
            ItemsSource = SnapListService.Items,
            SelectionMode = ListViewSelectionMode.Extended
        };
        _listView.KeyDown += ListView_KeyDown;
        Grid.SetRow(_listView, 2);
        rootGrid.Children.Add(_listView);

        // Row 3: Action Toolbar
        var toolBorder = new Border
        {
            Background = (Brush)Application.Current.Resources["AppBackgroundBrush"],
            BorderBrush = (Brush)Application.Current.Resources["AppBorderBrush"],
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 8, 10, 8)
        };
        var toolGrid = new Grid();
        for (int i = 0; i < 4; i++)
            toolGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var notepadBtn = new Button { Content = "📝 Notepad", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(2), Padding = new Thickness(4) };
        notepadBtn.Click += (_, _) => SnapListService.SendToNotepad();
        Grid.SetColumn(notepadBtn, 0);
        toolGrid.Children.Add(notepadBtn);

        var copyBtn = new Button { Content = "📋 Copy", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(2), Padding = new Thickness(4) };
        copyBtn.Click += (_, _) => SnapListService.CopyToClipboard();
        Grid.SetColumn(copyBtn, 1);
        toolGrid.Children.Add(copyBtn);

        var txtBtn = new Button { Content = "💾 .txt", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(2), Padding = new Thickness(4) };
        txtBtn.Click += ExportTxt_Click;
        Grid.SetColumn(txtBtn, 2);
        toolGrid.Children.Add(txtBtn);

        var csvBtn = new Button { Content = "📊 .csv", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(2), Padding = new Thickness(4) };
        csvBtn.Click += ExportCsv_Click;
        Grid.SetColumn(csvBtn, 3);
        toolGrid.Children.Add(csvBtn);

        toolBorder.Child = toolGrid;
        Grid.SetRow(toolBorder, 3);
        rootGrid.Children.Add(toolBorder);

        Content = rootGrid;

        Closed += (_, _) => _onClosedCallback?.Invoke();
    }

    private void TryApplyMicaBackdrop()
    {
        if (MicaController.IsSupported())
        {
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base };
        }
    }

    private void ConfigureAppWindow()
    {
        try
        {
            var appWindow = AppWindow;
            TitleBarHelper.ApplyWindowIcon(this, appWindow);

            appWindow.Resize(new SizeInt32(440, 600));

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[SnapListWindow] ConfigureAppWindow error: {ex.Message}", ex);
        }
    }

    private void ListView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Delete && _listView.SelectedItems.Count > 0)
        {
            var toRemove = _listView.SelectedItems.Cast<SnapItem>().ToList();
            foreach (var item in toRemove)
            {
                SnapListService.Remove(item);
            }
            e.Handled = true;
        }
    }

    private async void ExportTxt_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeChoices.Add("Text Documents (*.txt)", new List<string>() { ".txt" });
            picker.SuggestedFileName = "DirSnap_Export";

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                SnapListService.ExportToTxt(file.Path);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[SnapListWindow] ExportTxt error: {ex.Message}", ex);
        }
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeChoices.Add("CSV Files (*.csv)", new List<string>() { ".csv" });
            picker.SuggestedFileName = "DirSnap_Export";

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                SnapListService.ExportToCsv(file.Path);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[SnapListWindow] ExportCsv error: {ex.Message}", ex);
        }
    }
}
