using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace EquipmentKing;

public partial class MainWindow : Window
{
    private readonly DeviceMonitor _monitor = new();
    private readonly ObservableCollection<DeviceEntry> _devices = [];
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool _refreshInProgress;

    public MainWindow()
    {
        InitializeComponent();
        DeviceList.ItemsSource = _devices;
        Loaded += Window_Loaded;
        _refreshTimer.Tick += RefreshTimer_Tick;
        Closed += (_, _) => _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();

    private async void RefreshTimer_Tick(object? sender, EventArgs e) => await RefreshDevicesAsync();

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();

    private async Task RefreshDevicesAsync()
    {
        if (_refreshInProgress)
            return;

        _refreshInProgress = true;

        try
        {
            RefreshButton.IsEnabled = false;
            UpdateText.Text = "Scanning Windows device reports…";
            var result = await _monitor.ScanAsync();
            _devices.Clear();
            foreach (var device in result.Devices)
                _devices.Add(device);

            ConnectedCount.Text = result.Devices.Count(device => device.IsConnected).ToString();
            BatteryCount.Text = result.Devices.Count(device => device.BatteryPercent is not null).ToString();
            EmptyState.Visibility = _devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateText.Text = result.Error is null ? $"Updated {DateTime.Now:t}" : result.Error;
        }
        catch (Exception exception)
        {
            App.LogException("Device scan", exception);
            UpdateText.Text = "Scan failed. Details saved to the local log.";
            EmptyState.Visibility = _devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _refreshInProgress = false;
            if (IsLoaded && IsVisible)
                RefreshButton.IsEnabled = true;
        }
    }
}
