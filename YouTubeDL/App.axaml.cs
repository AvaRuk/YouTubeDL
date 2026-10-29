using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using YouTubeDL.Services;
using YouTubeDL.ViewModels;

namespace YouTubeDL;

public partial class App : Application
{
    private AppServices? _services;
    private MainWindowViewModel? _viewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppPaths.EnsureCreated();

            try
            {
                _services = AppServices.Create();
                _viewModel = new MainWindowViewModel(_services);

                var window = new MainWindow
                {
                    DataContext = _viewModel
                };

                desktop.MainWindow = window;

                desktop.Exit += (_, _) =>
                {
                    _viewModel?.Dispose();
                    _services?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                };
            }
            catch (Exception exception)
            {
                var log = new DiagnosticsLog();
                log.WriteError("Application startup failed.", exception);

                desktop.MainWindow = new Window
                {
                    Title = "YouTubeDL — startup error",
                    Width = 900,
                    Height = 500,
                    Content = new ScrollViewer
                    {
                        Padding = new Thickness(16),
                        Content = new TextBox
                        {
                            Text = exception.ToString(),
                            IsReadOnly = true,
                            AcceptsReturn = true,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Stretch
                        }
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
