using System.Configuration;
using System.Data;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace EquipmentKing;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly object LogLock = new();
    internal static bool IsScanWorker { get; private set; }

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

        if (e.Args.Length >= 2 && e.Args[0] == "--equipment-king-scan-worker")
        {
            IsScanWorker = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunScanWorkerAsync(e.Args[1]);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static async Task RunScanWorkerAsync(string resultPath)
    {
        try
        {
            LogMessage("Scan worker started");
            var result = await new DeviceMonitor().ScanAsync();
            var payload = ScanWorkerPayload.From(result);
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(payload));
            LogMessage($"Scan worker completed with {result.Devices.Count} devices");
            Environment.ExitCode = 0;
        }
        catch (Exception exception)
        {
            LogException("Scan worker failed", exception);
            try
            {
                await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(
                    new ScanWorkerPayload([], "Device scan failed in the worker process.")));
            }
            catch (Exception writeException)
            {
                LogException("Could not write scan worker result", writeException);
            }
            Environment.ExitCode = 1;
        }
        finally
        {
            Current.Shutdown(Environment.ExitCode);
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("Unhandled Windows UI exception", e.Exception);
        e.Handled = true;
        if (IsScanWorker)
        {
            Environment.ExitCode = 2;
            Current.Shutdown(2);
            return;
        }
        MessageBox.Show(
            "A device scan ran into a problem. Equipment King stayed open, and details were saved in the local log.",
            "Equipment King",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    public static void LogException(string context, Exception exception)
    {
        LogMessage($"{context}{Environment.NewLine}{exception}");
    }

    public static void LogMessage(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EquipmentKing");
            Directory.CreateDirectory(directory);
            var logPath = Path.Combine(directory, "equipment-king.log");
            lock (LogLock)
            {
                File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must not interrupt scanning or prevent the window from staying open.
        }
    }
}

internal sealed record ScanWorkerPayload(IReadOnlyList<ScanWorkerDevice> Devices, string? Error)
{
    public static ScanWorkerPayload From(DeviceScanResult result) => new(
        result.Devices.Select(device => new ScanWorkerDevice(
            device.Id,
            device.Source,
            device.ContainerId,
            device.DeviceInstanceId,
            device.Name,
            device.Category,
            device.Transport,
            device.Icon,
            device.IsConnected,
            device.BatteryPercent,
            device.BatteryNote)).ToArray(),
        result.Error);
}

internal sealed record ScanWorkerDevice(
    string Id,
    string? Source,
    string? ContainerId,
    string? DeviceInstanceId,
    string Name,
    string Category,
    string Transport,
    string Icon,
    bool IsConnected,
    int? BatteryPercent,
    string BatteryNote)
{
    public DeviceEntry ToDeviceEntry() => new()
    {
        Id = Id,
        Source = Source,
        ContainerId = ContainerId,
        DeviceInstanceId = DeviceInstanceId,
        Name = Name,
        Category = Category,
        Transport = Transport,
        Icon = Icon,
        IsConnected = IsConnected,
        BatteryPercent = BatteryPercent,
        BatteryNote = BatteryNote
    };
}

