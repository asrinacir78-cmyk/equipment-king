using System.Globalization;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Devices.Power;
using Windows.Storage.Streams;

namespace EquipmentKing;

public sealed class DeviceMonitor
{
    private static readonly string[] PropertiesToRead =
    [
        "System.Devices.BatteryLife",
        "System.Devices.Aep.IsConnected",
        "System.Devices.ContainerId",
        "System.Devices.DeviceInstanceId",
        "System.Devices.Parent",
        "System.Devices.InterfaceClassGuid"
    ];

    private static readonly (ushort Page, ushort Usage, string Category)[] HidUsages =
    [
        (0x01, 0x02, "Mouse"),
        (0x01, 0x06, "Keyboard"),
        (0x01, 0x04, "Joystick"),
        (0x01, 0x05, "Game controller"),
        (0x0C, 0x01, "Wireless accessory")
    ];

    public async Task<DeviceScanResult> ScanAsync()
    {
        var devices = new List<DeviceEntry>();
        var issues = new List<string>();

        try
        {
            var pairedBluetooth = await DeviceInformation.FindAllAsync(
                BluetoothDevice.GetDeviceSelectorFromPairingState(true), PropertiesToRead);
            foreach (var info in pairedBluetooth)
            {
                try
                {
                    var connected = await ReadBluetoothConnectionStateAsync(info);
                    AddOrMerge(devices, CreateEntry(info, "Bluetooth", connected, "Bluetooth"));
                }
                catch (Exception exception)
                {
                    App.LogException("Bluetooth device scan", exception);
                }
            }
        }
        catch (Exception exception)
        {
            issues.Add("Bluetooth scan unavailable");
            App.LogException("Bluetooth scan", exception);
        }

        foreach (var usage in HidUsages)
        {
            try
            {
                var selector = HidDevice.GetDeviceSelector(usage.Page, usage.Usage);
                var hidDevices = await DeviceInformation.FindAllAsync(selector, PropertiesToRead);
                foreach (var info in hidDevices)
                {
                    if (!info.IsEnabled || string.IsNullOrWhiteSpace(info.Name))
                        continue;
                    AddOrMerge(devices, CreateEntry(info, usage.Category, isConnected: true,
                        transport: DetermineTransport(info)));
                }
            }
            catch (Exception exception)
            {
                // Keep scanning other HID usages if a vendor driver blocks one collection.
                App.LogException($"{usage.Category} HID scan", exception);
            }
        }

        try
        {
            var allDevicesSelector = DeviceInformation.GetAqsFilterFromDeviceClass(DeviceClass.All);
            var allDevices = await DeviceInformation.FindAllAsync(allDevicesSelector, PropertiesToRead);
            foreach (var info in allDevices)
            {
                try
                {
                    if (!info.IsEnabled || string.IsNullOrWhiteSpace(info.Name) ||
                        IsBluetoothDevice(info) || !IsUsbDevice(info))
                        continue;

                    AddOrMerge(devices, CreateEntry(info, "USB", isConnected: true, transport: "USB"));
                }
                catch (Exception exception)
                {
                    App.LogException("USB device entry", exception);
                }
            }
        }
        catch (Exception exception)
        {
            issues.Add("USB device scan unavailable");
            App.LogException("USB device scan", exception);
        }

        await ApplyBatteryControllerReportsAsync(devices);
        await FillMissingBluetoothLeBatteryLevelsAsync(devices);

        devices.Sort((left, right) =>
        {
            var connected = right.IsConnected.CompareTo(left.IsConnected);
            return connected != 0 ? connected : string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
        });

        return new DeviceScanResult(devices, issues.Count == 0 ? null : string.Join(" · ", issues));
    }

    private static async Task<bool> ReadBluetoothConnectionStateAsync(DeviceInformation info)
    {
        try
        {
            using var bluetoothDevice = await BluetoothDevice.FromIdAsync(info.Id);
            if (bluetoothDevice is not null)
                return bluetoothDevice.ConnectionStatus == BluetoothConnectionStatus.Connected;
        }
        catch
        {
            // Fall back to the Association Endpoint connection property when Windows denies this API.
        }
        return ReadBoolean(info, "System.Devices.Aep.IsConnected");
    }

    private static DeviceEntry CreateEntry(DeviceInformation info, string source, bool isConnected, string? transport = null)
    {
        var name = string.IsNullOrWhiteSpace(info.Name) ? "Wireless accessory" : info.Name.Trim();
        var propertyBattery = ReadBatteryPercent(info);
        return new DeviceEntry
        {
            Id = info.Id,
            Source = source,
            ContainerId = ReadString(info, "System.Devices.ContainerId"),
            DeviceInstanceId = ReadString(info, "System.Devices.DeviceInstanceId"),
            Name = name,
            Category = GuessCategory(name, source),
            Transport = transport ?? DetermineTransport(info),
            Icon = GuessIcon(name, source),
            IsConnected = isConnected,
            BatteryPercent = propertyBattery,
            BatteryNote = propertyBattery is null ? "Battery not reported by Windows" : "Windows device battery"
        };
    }

    private static async Task ApplyBatteryControllerReportsAsync(List<DeviceEntry> devices)
    {
        try
        {
            var batteryInfos = await DeviceInformation.FindAllAsync(Battery.GetDeviceSelector(), PropertiesToRead);
            foreach (var info in batteryInfos)
            {
                try
                {
                    var battery = await Battery.FromIdAsync(info.Id);
                    var report = battery?.GetReport();
                    if (report?.RemainingCapacityInMilliwattHours is not int remaining ||
                        report.FullChargeCapacityInMilliwattHours is not int full || full <= 0)
                        continue;

                    var owner = FindBatteryOwner(devices, info);
                    if (owner is null)
                        continue;
                    owner.BatteryPercent = Math.Clamp((int)Math.Round(remaining * 100d / full), 0, 100);
                    owner.BatteryNote = "Windows battery controller";
                }
                catch
                {
                    // A battery controller can disappear between enumeration and report retrieval.
                }
            }
        }
        catch
        {
            // Some PCs do not expose individual battery controllers.
        }
    }

    private static DeviceEntry? FindBatteryOwner(IReadOnlyList<DeviceEntry> devices, DeviceInformation batteryInfo)
    {
        var batteryContainer = ReadString(batteryInfo, "System.Devices.ContainerId");
        if (!string.IsNullOrWhiteSpace(batteryContainer))
        {
            var byContainer = devices.FirstOrDefault(device =>
                string.Equals(device.ContainerId, batteryContainer, StringComparison.OrdinalIgnoreCase));
            if (byContainer is not null) return byContainer;
        }

        var normalizedBatteryId = NormalizeId(ReadString(batteryInfo, "System.Devices.DeviceInstanceId") ?? batteryInfo.Id);
        if (normalizedBatteryId.Length < 8)
            return null;

        return devices.FirstOrDefault(device =>
        {
            var id = NormalizeId(device.DeviceInstanceId ?? device.Id);
            return id.Length >= 8 && (id.Contains(normalizedBatteryId, StringComparison.OrdinalIgnoreCase) ||
                                      normalizedBatteryId.Contains(id, StringComparison.OrdinalIgnoreCase));
        });
    }

    private static async Task FillMissingBluetoothLeBatteryLevelsAsync(List<DeviceEntry> devices)
    {
        foreach (var device in devices.Where(item => item.BatteryPercent is null && item.Source == "Bluetooth" && item.IsConnected))
        {
            try
            {
                using var bleDevice = await BluetoothLEDevice.FromIdAsync(device.Id);
                if (bleDevice is null)
                    continue;

                var services = await bleDevice.GetGattServicesForUuidAsync(
                    GattServiceUuids.Battery, BluetoothCacheMode.Uncached);
                if (services.Status != GattCommunicationStatus.Success)
                    continue;

                foreach (var service in services.Services)
                {
                    using (service)
                    {
                        var characteristics = await service.GetCharacteristicsForUuidAsync(
                            GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached);
                        if (characteristics.Status != GattCommunicationStatus.Success)
                            continue;

                        foreach (var characteristic in characteristics.Characteristics)
                        {
                            var read = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                            if (read.Status != GattCommunicationStatus.Success || read.Value.Length == 0)
                                continue;

                            using var reader = DataReader.FromBuffer(read.Value);
                            device.BatteryPercent = Math.Clamp((int)reader.ReadByte(), 0, 100);
                            device.BatteryNote = "Bluetooth LE battery service";
                            break;
                        }
                    }
                    if (device.BatteryPercent is not null)
                        break;
                }
            }
            catch
            {
                // Paired devices can be offline, use a proprietary service, or deny GATT reads.
            }
        }
    }

    private static void AddOrMerge(List<DeviceEntry> devices, DeviceEntry candidate)
    {
        var existing = devices.FirstOrDefault(item =>
            (!string.IsNullOrWhiteSpace(candidate.ContainerId) &&
             string.Equals(item.ContainerId, candidate.ContainerId, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(item.Id, candidate.Id, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            devices.Add(candidate);
            return;
        }

        existing.IsConnected |= candidate.IsConnected;
        if (candidate.BatteryPercent is not null)
            existing.BatteryPercent = Math.Max(existing.BatteryPercent ?? 0, candidate.BatteryPercent.Value);
        if (existing.Name.StartsWith("HID-compliant", StringComparison.OrdinalIgnoreCase) &&
            !candidate.Name.StartsWith("HID-compliant", StringComparison.OrdinalIgnoreCase))
            existing.Name = candidate.Name;
        existing.ContainerId ??= candidate.ContainerId;
        existing.DeviceInstanceId ??= candidate.DeviceInstanceId;
        if (TransportPriority(candidate.Transport) > TransportPriority(existing.Transport))
            existing.Transport = candidate.Transport;
        if (candidate.Category != "Wireless accessory") existing.Category = candidate.Category;
    }

    private static string DetermineTransport(DeviceInformation info)
    {
        if (IsBluetoothDevice(info)) return "Bluetooth";
        return IsUsbDevice(info) ? "USB" : "HID";
    }

    private static bool IsBluetoothDevice(DeviceInformation info)
    {
        var identity = $"{info.Id} {ReadString(info, "System.Devices.DeviceInstanceId")}";
        return identity.Contains("BTH", StringComparison.OrdinalIgnoreCase) ||
               identity.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUsbDevice(DeviceInformation info)
    {
        var identity = $"{info.Id} {ReadString(info, "System.Devices.DeviceInstanceId")} {ReadString(info, "System.Devices.Parent")}";
        return identity.Contains("USB\\", StringComparison.OrdinalIgnoreCase) ||
               identity.Contains("USB#", StringComparison.OrdinalIgnoreCase);
    }

    private static int TransportPriority(string transport) => transport switch
    {
        "Bluetooth" or "USB" => 2,
        "HID" => 1,
        _ => 0
    };

    private static int? ReadBatteryPercent(DeviceInformation info)
    {
        if (!info.Properties.TryGetValue("System.Devices.BatteryLife", out var raw) || raw is null)
            return null;
        try
        {
            var percent = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
            return percent is >= 0 and <= 100 ? percent : null;
        }
        catch (FormatException) { return null; }
        catch (InvalidCastException) { return null; }
        catch (OverflowException) { return null; }
    }

    private static bool ReadBoolean(DeviceInformation info, string key)
    {
        if (!info.Properties.TryGetValue(key, out var raw) || raw is null)
            return false;
        try { return Convert.ToBoolean(raw, CultureInfo.InvariantCulture); }
        catch (Exception) { return false; }
    }

    private static string? ReadString(DeviceInformation info, string key)
    {
        if (!info.Properties.TryGetValue(key, out var raw) || raw is null)
            return null;
        return Convert.ToString(raw, CultureInfo.InvariantCulture);
    }

    private static string NormalizeId(string value) => new(value.Where(char.IsLetterOrDigit).ToArray());

    private static string GuessCategory(string name, string fallback)
    {
        var text = name.ToLowerInvariant();
        if (text.Contains("mouse") || text.Contains("trackpad") || text.Contains("trackball")) return "Mouse";
        if (text.Contains("keyboard") || text.Contains("keypad")) return "Keyboard";
        if (text.Contains("headphone") || text.Contains("headset") || text.Contains("earbud")) return "Audio";
        if (text.Contains("gamepad") || text.Contains("controller") || text.Contains("joystick")) return "Controller";
        return fallback;
    }

    private static string GuessIcon(string name, string fallback) => GuessCategory(name, fallback) switch
    {
        "Mouse" => "🖱",
        "Keyboard" => "⌨",
        "Audio" => "♫",
        "Controller" => "🎮",
        _ => "◉"
    };
}
