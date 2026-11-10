namespace VGNOrb.Core.Devices;

public sealed record DeviceDescriptor(
    string Id,
    string DisplayName,
    string Transport,
    DeviceKind Kind = DeviceKind.Unknown);
