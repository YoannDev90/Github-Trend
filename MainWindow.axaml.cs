using Avalonia.Controls;

namespace Github_Trend
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel = new();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _viewModel;
            Loaded += async (_, _) => await _viewModel.LoadGithubColorsAsync();
        }
    }
}