using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using NLMCL.App.ViewModels;

namespace NLMCL.App.Views
{
    /// <summary>设置页。</summary>
    public partial class SettingsPage : UserControl
    {
        public SettingsPage()
        {
            InitializeComponent();
        }

        /// <summary>由主窗口 Ctrl+F 调用，聚焦设置搜索框。</summary>
        public void FocusSearch()
        {
            SettingsSearchBox.Focus();
            SettingsSearchBox.SelectAll();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                SettingsSearchBox.Focus();
                SettingsSearchBox.SelectAll();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape
                && SettingsSearchBox.IsKeyboardFocusWithin
                && DataContext is SettingsViewModel settings)
            {
                settings.ClearSettingsSearchCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void OnSearchGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            SearchBorder.SetResourceReference(Border.BorderBrushProperty, "Primary");
        }

        private void OnSearchLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            SearchBorder.SetResourceReference(Border.BorderBrushProperty, "BorderColor");
        }

        private void FirstRun_Click(object sender, RoutedEventArgs e)
        {
            if (!(Application.Current is App app))
            {
                return;
            }

            try
            {
                WizardWindow wizard = app.RootServices.GetRequiredService<WizardWindow>();
                wizard.Owner = Window.GetWindow(this);
                wizard.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    "无法打开首次引导：" + ex.Message,
                    "NLMCL",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
