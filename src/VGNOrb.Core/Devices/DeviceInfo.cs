namespace VGNOrb.Core.Devices;

public sealed record DeviceInfo(
    string Id,
    string DisplayName,
    string Manufacturer,
    string Model,
    string FirmwareVersion,
    string Transport)
{
    public string Summary => $"{Manufacturer} {Model}";
}
