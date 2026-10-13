using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace EquipmentKing;

internal static class DeviceScanWorker
{
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(60);

    public static async Task<DeviceScanResult> RunAsync()
    {
        var resultPath = Path.Combine(Path.GetTempPath(), $"equipment-king-scan-{Guid.NewGuid():N}.json");
        using var process = new Process { StartInfo = CreateStartInfo(resultPath) };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Windows did not start the device scan worker.");

            App.LogMessage($"Started scan worker process {process.Id}");
            using var timeout = new CancellationTokenSource(ScanTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                TryStop(process);
                throw new TimeoutException($"Device scan worker exceeded {ScanTimeout.TotalSeconds:0} seconds.");
            }

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Device scan worker exited with code {process.ExitCode}.");

            if (!File.Exists(resultPath))
                throw new InvalidOperationException("Device scan worker exited without returning a result.");

            var json = await File.ReadAllTextAsync(resultPath);
            var payload = JsonSerializer.Deserialize<ScanWorkerPayload>(json)
                ?? throw new InvalidOperationException("Device scan worker returned an empty result.");
            return new DeviceScanResult(payload.Devices.Select(device => device.ToDeviceEntry()).ToArray(), payload.Error);
        }
        catch (Exception exception)
        {
            App.LogException("Device scan worker process", exception);
            throw;
        }
        finally
        {
            try { File.Delete(resultPath); }
            catch (Exception exception) { App.LogException("Delete temporary scan result", exception); }
        }
    }

    private static ProcessStartInfo CreateStartInfo(string resultPath)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Windows did not report the application path.");

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = AppContext.BaseDirectory
        };

        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entryAssembly = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(entryAssembly))
                throw new InvalidOperationException("Could not locate the app assembly for the scan worker.");
            startInfo.ArgumentList.Add(entryAssembly);
        }

        startInfo.ArgumentList.Add("--equipment-king-scan-worker");
        startInfo.ArgumentList.Add(resultPath);
        return startInfo;
    }

    private static void TryStop(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception exception)
        {
            App.LogException("Stop timed-out scan worker", exception);
        }
    }
}
