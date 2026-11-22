using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Linq;

namespace TS3PackageFusion;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;

            // Windows : fichiers glissés sur le .exe (ou « Ouvrir avec ») passés en arguments.
            var startupFiles = desktop.Args?.Where(arg => !arg.StartsWith('-')).ToArray() ?? [];
            if (startupFiles.Length > 0)
                window.Opened += async (_, _) => await window.AddPathsAsync(startupFiles);

            // macOS : fichiers déposés sur l'icône du Dock / Finder « Ouvrir avec ».
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            {
                activatable.Activated += (_, e) =>
                {
                    if (e is not FileActivatedEventArgs fileArgs) return;
                    var paths = fileArgs.Files.Select(item => item.TryGetLocalPath()).OfType<string>().ToArray();
                    if (paths.Length > 0) Dispatcher.UIThread.Post(async () => await window.AddPathsAsync(paths));
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
