using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Views {
    public partial class LyricsDialog : Window {
        public LyricsDialog() {
            InitializeComponent();
            DIALOG_Box.AddHandler(KeyDownEvent, TextBoxKeyDown, RoutingStrategies.Tunnel);
        }

        void OnOpened(object? sender, EventArgs e) {
            Dispatcher.UIThread.Post(() =>
            {
                DIALOG_Box.Focus();
            }, DispatcherPriority.Background);
        }

        void OnReset(object? sender, RoutedEventArgs e) {
            (DataContext as LyricsViewModel)!.Reset();
        }

        void OnCancel(object? sender, RoutedEventArgs e) {
            (DataContext as LyricsViewModel)!.Cancel();
            Close();
        }

        void OnFinish(object? sender, RoutedEventArgs e) {
            (DataContext as LyricsViewModel)!.Finish();
            Close();
        }

        private void TextBoxKeyDown(object? sender, KeyEventArgs e) {
            switch (e.Key) {
                case Key.S: 
                    if(e.KeyModifiers == KeyModifiers.Control || e.KeyModifiers == KeyModifiers.Meta) {
                        OnFinish(sender, e);
                        e.Handled = true;
                    }
                    break;
                case Key.Enter:
                    //If Shift+Enter, insert line break (default textbox behavior).
                    if (e.KeyModifiers == KeyModifiers.Shift) {
                        return;
                    }
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
