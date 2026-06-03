using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using HismithController.ViewModels;

namespace HismithController;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // Tint the native title bar to match the theme. SourceInitialized is the first point the
        // HWND exists; PropertyChanged re-applies it when the user toggles light/dark live.
        SourceInitialized += (_, _) => TitleBarTheme.Apply(this, _viewModel.IsDarkTheme);
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
}
