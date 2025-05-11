using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Views {
    public partial class EditBarsDialog : Window {

        public EditBarsDialog() {
            InitializeComponent();
        }

        void OnOpened(object? sender, EventArgs e) {
        }
        void OnCancel(object? sender, RoutedEventArgs e) {
            (DataContext as EditBarsViewModel)!.Cancel();
            Close();
        }

        void OnFinish(object? sender, RoutedEventArgs e) {
            (DataContext as EditBarsViewModel)!.Finish();
            Close();
        }
        private void OnKeyDown(object? sender, KeyEventArgs e) {
            switch (e.Key) {
                case Key.Enter:
                    OnFinish(sender, e);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    OnCancel(sender, e);
                    e.Handled = true;
                    break;
                default:
                    break;
            }
        }
    }
}
