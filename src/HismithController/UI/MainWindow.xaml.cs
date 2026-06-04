using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using HismithController.ViewModels;

namespace HismithController;

public partial class MainWindow : Window
{
    private const int VkSpace = 0x20;

    private readonly MainViewModel _viewModel;

    // System-wide hook so the Spacebar emergency stop fires even when another window is focused.
    private readonly GlobalKeyboardHook _globalHook = new();

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

            // Install from the UI thread so the hook's callbacks are dispatched back to it.
            _globalHook.KeyDown += OnGlobalKeyDown;
            _globalHook.Install();
        };
        Closed += (_, _) =>
        {
            _globalHook.KeyDown -= OnGlobalKeyDown;
            _globalHook.Dispose();
        };
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsDarkTheme))
            TitleBarTheme.Apply(this, _viewModel.IsDarkTheme);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && _viewModel.IsConnected)
        {
            e.Handled = true;
            if (_viewModel.EmergencyStopCommand.CanExecute(null))
                _viewModel.EmergencyStopCommand.Execute(null);
        }
    }

    // Runs on the UI thread (see GlobalKeyboardHook.KeyDown). Handles Space only when the app is
    // NOT the active window; when it is focused, OnPreviewKeyDown already handles Space, so we bail
    // here to avoid firing the stop twice.
    private void OnGlobalKeyDown(int vkCode)
    {
        if (vkCode != VkSpace || !_viewModel.IsConnected || IsActive)
            return;

        if (_viewModel.EmergencyStopCommand.CanExecute(null))
            _viewModel.EmergencyStopCommand.Execute(null);
    }
}
