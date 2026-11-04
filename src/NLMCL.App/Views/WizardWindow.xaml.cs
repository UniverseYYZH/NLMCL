using System;
using System.Windows;
using NLMCL.App.ViewModels;

namespace NLMCL.App.Views
{
    /// <summary>首次使用引导窗口（三步）。</summary>
    public partial class WizardWindow : Window
    {
        private readonly WizardViewModel _viewModel;

        public WizardWindow(WizardViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            InitializeComponent();
            DataContext = _viewModel;

            _viewModel.Completed += OnCompleted;

            Loaded += async (sender, args) => await _viewModel.InitializeAsync().ConfigureAwait(true);
        }

        private void OnCompleted()
        {
            _viewModel.Completed -= OnCompleted;

            DialogResult = true;
            Close();
        }
    }
}
