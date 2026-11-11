using System.Text.Json;
using System.Text.Json.Serialization;

namespace VGNOrb.Core.Configuration;

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext
{
}
