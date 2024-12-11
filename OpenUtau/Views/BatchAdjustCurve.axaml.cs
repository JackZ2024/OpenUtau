using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Views {
    public partial class BatchAdjustCurve : Window {

        public BatchAdjustCurve() {
            InitializeComponent();
        }

        void OnOpened(object? sender, EventArgs e) {
            (DataContext as NotesViewModel)!.tipInfo = "";
        }
        void OnCancel(object? sender, RoutedEventArgs e) {
            Close();
        }

        void OnFinish(object? sender, RoutedEventArgs e) {
            (DataContext as NotesViewModel)!.BatchMoveCurvePoints();
        }
        private void onTextChanged(object? sender, TextChangedEventArgs e) {
            if (sender is TextBox textBox) {
                if(int.TryParse(textBox.Text, out int value )) {
                    (DataContext as NotesViewModel)!.offsetValue = value;
                }
            }
        }
    }
}
