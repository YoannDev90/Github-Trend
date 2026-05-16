using Avalonia.Controls;

namespace Github_Trend
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel = new();
        private bool _initialized;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _viewModel;
            Loaded += async (_, _) =>
            {
                if (_initialized)
                {
                    return;
                }

                _initialized = true;
                await _viewModel.InitializeAsync();
            };
        }
    }
}