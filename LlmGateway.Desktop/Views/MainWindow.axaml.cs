using Avalonia.Controls;

namespace LlmGateway.Desktop.Views;

public sealed partial class MainWindow : Window
{
    public bool IsExitRequested { get; set; }

    public MainWindow() => InitializeComponent();
}
