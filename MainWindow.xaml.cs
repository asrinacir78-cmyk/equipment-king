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
        Loaded += async (_, _) => await RefreshDevicesAsync();
        _refreshTimer.Tick += async (_, _) => await RefreshDevicesAsync();
        _refreshTimer.Start();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();

    private async Task RefreshDevicesAsync()
    {
        if (_refreshInProgress)
            return;

        _refreshInProgress = true;
        RefreshButton.IsEnabled = false;
        UpdateText.Text = "Scanning Windows device reports…";

        try
        {
            var result = await _monitor.ScanAsync();
            _devices.Clear();
            foreach (var device in result.Devices)
                _devices.Add(device);

            ConnectedCount.Text = result.Devices.Count(device => device.IsConnected).ToString();
            BatteryCount.Text = result.Devices.Count(device => device.BatteryPercent is not null).ToString();
            EmptyState.Visibility = _devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateText.Text = result.Error is null ? $"Updated {DateTime.Now:t}" : result.Error;
        }
        catch
        {
            UpdateText.Text = "Windows device scan failed";
            EmptyState.Visibility = _devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            _refreshInProgress = false;
        }
    }
}
