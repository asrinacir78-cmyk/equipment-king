using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace EquipmentKing;

public sealed class DeviceEntry : INotifyPropertyChanged
{
    private int? _batteryPercent;
    private bool _isConnected;

    public required string Id { get; init; }
    public string? Source { get; set; }
    public string? ContainerId { get; set; }
    public string? DeviceInstanceId { get; set; }
    public string Name { get; set; } = "Wireless accessory";
    public string Category { get; set; } = "Accessory";
    public string Icon { get; set; } = "◉";
    public string BatteryNote { get; set; } = "Battery level";

    public int? BatteryPercent
    {
        get => _batteryPercent;
        set
        {
            if (_batteryPercent == value) return;
            _batteryPercent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BatteryText));
            OnPropertyChanged(nameof(BatteryValue));
            OnPropertyChanged(nameof(BatteryBrush));
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (_isConnected == value) return;
            _isConnected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConnectionText));
            OnPropertyChanged(nameof(StatusBrush));
        }
    }

    public string ConnectionText => IsConnected ? "Connected" : "Paired";
    public string BatteryText => BatteryPercent is int percent ? $"{percent}%" : "—";
    public double BatteryValue => BatteryPercent ?? 0;
    public Brush BatteryBrush => BatteryPercent switch
    {
        <= 15 => new SolidColorBrush(Color.FromRgb(255, 128, 124)),
        null => new SolidColorBrush(Color.FromRgb(130, 145, 157)),
        _ => new SolidColorBrush(Color.FromRgb(112, 225, 184))
    };
    public Brush StatusBrush => IsConnected
        ? new SolidColorBrush(Color.FromRgb(112, 225, 184))
        : new SolidColorBrush(Color.FromRgb(130, 145, 157));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record DeviceScanResult(IReadOnlyList<DeviceEntry> Devices, string? Error = null);
