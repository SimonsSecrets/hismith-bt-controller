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
