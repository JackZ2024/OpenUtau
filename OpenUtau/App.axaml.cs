using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Core;
using Serilog;

namespace OpenUtau.App {
    public class App : Application {

        // add by Jack
        private static string[]? _pendingFiles;
        // end add

        public override void Initialize() {
            Log.Information("Initializing application.");
            AvaloniaXamlLoader.Load(this);
            InitializeCulture();
            InitializeTheme();
            Log.Information("Initialized application.");
        }

        public override void OnFrameworkInitializationCompleted() {
            Log.Information("Framework initialization completed.");
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                desktop.MainWindow = new SplashWindow();

                // add by Jack
                if (OS.IsMacOS()) {
                    // 处理 macOS 的文件打开关联 (Avalonia 11.1+)
                    var activatableLifetime = this.TryGetFeature(typeof(IActivatableLifetime)) as IActivatableLifetime;
                    if (activatableLifetime != null) {
                        activatableLifetime.Activated += OnAppActivated;
                    }
                }
                // end add
            }

            base.OnFrameworkInitializationCompleted();
        }

        // add by Jack
        private void OnAppActivated(object? sender, ActivatedEventArgs e) {
            if (e.Kind == ActivationKind.File) {
                var fileArgs = e as FileActivatedEventArgs;
                var filePath = fileArgs?.Files.FirstOrDefault()?.Path.LocalPath;

                if (!string.IsNullOrEmpty(filePath)) {
                    // 只有是 ustx 工程才处理
                    if (filePath.EndsWith(".ustx", System.StringComparison.OrdinalIgnoreCase)) {
                        // 使用 Dispatcher 异步处理，防止在 App 还没完全初始化时就加载工程导致崩溃
                        Dispatcher.UIThread.Post(() => {
                            HandleOpenFile(filePath);
                        }, DispatcherPriority.Background);
                    }
                }
            }
        }
        private void HandleOpenFile(string filePath) {
            // 1. 检查路径是否有效
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath)) return;

            // 2. 获取桌面生命周期对象
            var desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

            // 3. 尝试获取 MainWindow 的 ViewModel
            // OpenUtau 的主窗口 DataContext 就是 MainWindowViewModel
            if (desktop?.MainWindow?.DataContext is MainWindowViewModel vm) {
                // OpenUtau 的 OpenProject 接收的是字符串数组 string[]
                if (!DocManager.Inst.ChangesSaved) { return; }
                vm.OpenProject(new[] { filePath });
                if (vm.Page == 0) {
                    vm.Page = 1;
                }

            } else {
                // 【关键点】冷启动处理：
                // 如果此时 MainWindow 还没创建（比如还在闪屏界面 SplashWindow），
                // 我们需要把路径存起来，等 MainWindow 初始化后再读。
                // 你可以在 App 类里定义一个静态变量
                _pendingFiles = new[] { filePath };
            }
        }
        public static string[]? GetPendingFiles() {
            var files = _pendingFiles;
            _pendingFiles = null; // 取完清空
            return files;
        }
        // end add

        public void InitializeCulture() {
            Log.Information("Initializing culture.");
            string sysLang = CultureInfo.InstalledUICulture.Name;
            string prefLang = Core.Util.Preferences.Default.Language;
            var languages = GetLanguages();
            if (languages.ContainsKey(prefLang)) {
                SetLanguage(prefLang);
            } else if (languages.ContainsKey(sysLang)) {
                SetLanguage(sysLang);
                Core.Util.Preferences.Default.Language = sysLang;
                Core.Util.Preferences.Save();
            } else {
                SetLanguage("en-US");
            }

            // Force using InvariantCulture to prevent issues caused by culture dependent string conversion, especially for floating point numbers.
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            Log.Information("Initialized culture.");
        }

        public static Dictionary<string, IResourceProvider> GetLanguages() {
            if (Current == null) {
                return new();
            }
            var result = new Dictionary<string, IResourceProvider>();
            foreach (string key in Current.Resources.Keys.OfType<string>()) {
                if (key.StartsWith("strings-") &&
                    Current.Resources.TryGetResource(key, ThemeVariant.Default, out var res) &&
                    res is IResourceProvider rp) {
                    result.Add(key.Replace("strings-", ""), rp);
                }
            }
            return result;
        }

        public static void SetLanguage(string language) {
            if (Current == null) {
                return;
            }
            var languages = GetLanguages();
            foreach (var res in languages.Values) {
                Current.Resources.MergedDictionaries.Remove(res);
            }
            if (language != "en-US") {
                Current.Resources.MergedDictionaries.Add(languages["en-US"]);
            }
            if (languages.TryGetValue(language, out var res1)) {
                Current.Resources.MergedDictionaries.Add(res1);
            }
        }

        static void InitializeTheme() {
            Log.Information("Initializing theme.");
            SetTheme();
            Log.Information("Initialized theme.");
        }

        public static void SetTheme() {
            if (Current == null) {
                return;
            }
            var light = (IResourceProvider)Current.Resources["themes-light"]!;
            var dark = (IResourceProvider)Current.Resources["themes-dark"]!;
            Current.Resources.MergedDictionaries.Remove(light);
            Current.Resources.MergedDictionaries.Remove(dark);
            if (Core.Util.Preferences.Default.Theme == 0) {
                Current.Resources.MergedDictionaries.Add(light);
                Current.RequestedThemeVariant = ThemeVariant.Light;
            } else {
                Current.Resources.MergedDictionaries.Add(dark);
                Current.RequestedThemeVariant = ThemeVariant.Dark;
            }
            ThemeManager.LoadTheme();
        }
    }
}
