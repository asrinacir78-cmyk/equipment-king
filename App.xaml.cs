using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace EquipmentKing;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                LogException("Unhandled process exception", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogException("Unobserved background task exception", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("Unhandled Windows UI exception", e.Exception);
        e.Handled = true;
        MessageBox.Show(
            "A device scan ran into a problem. Equipment King stayed open, and details were saved in the local log.",
            "Equipment King",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    public static void LogException(string context, Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EquipmentKing");
            Directory.CreateDirectory(directory);
            var logPath = Path.Combine(directory, "equipment-king.log");
            File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must not interrupt scanning or prevent the window from staying open.
        }
    }
}

