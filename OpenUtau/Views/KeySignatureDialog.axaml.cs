using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.Core;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.Views {
    public partial class KeySignatureDialog : Window {
        static readonly List<string> _keysText = new List<string>();

        public List<string> KeysText => _keysText;
        [Reactive] public int Key { get; set; }
        public Action<int>? OnOk { get; set; }

        public KeySignatureDialog() : this(0) { }

        public KeySignatureDialog(int key) {
            InitializeComponent();
            _keysText.Clear();
            _keysText.AddRange(MusicMath.KeysInOctave.Select((key, index) => new string($"1={key.Item1}")));
            Key = key;
            DataContext = this;
        }

        private void OnOkButtonClick(object sender, RoutedEventArgs args) {
            OnOk?.Invoke(Key);
            Close();
        }
    }
}
