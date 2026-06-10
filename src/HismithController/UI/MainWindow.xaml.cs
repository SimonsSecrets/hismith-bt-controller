using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using HismithController.ViewModels;

namespace HismithController;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    // System-wide Alt+Space hotkey so the emergency stop fires even when another window is focused.
    private readonly GlobalHotkey _emergencyHotkey = new();

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // Tint the native title bar to match the theme. SourceInitialized is the first point the
        // HWND exists; PropertyChanged re-applies it when the user toggles light/dark live.
        SourceInitialized += (_, _) =>
        {
            TitleBarTheme.Apply(this, _viewModel.IsDarkTheme);

            // Register once the HWND exists; WM_HOTKEY is then dispatched on this UI thread.
            _emergencyHotkey.Pressed += OnEmergencyHotkey;
            _emergencyHotkey.Register(new WindowInteropHelper(this).Handle);
        };
        Closed += (_, _) =>
        {
            _emergencyHotkey.Pressed -= OnEmergencyHotkey;
            _emergencyHotkey.Dispose();
        };
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsDarkTheme))
            TitleBarTheme.Apply(this, _viewModel.IsDarkTheme);
    }

    // Runs on the UI thread (see GlobalHotkey.Pressed). The hotkey is global and focus-independent,
    // so this single handler covers both the focused and background cases.
    private void OnEmergencyHotkey()
    {
        if (!_viewModel.IsConnected)
            return;

        if (_viewModel.EmergencyStopCommand.CanExecute(null))
            _viewModel.EmergencyStopCommand.Execute(null);
    }
}
