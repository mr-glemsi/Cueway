using System.Text.Json.Serialization;
using Cueway.Media;

namespace Cueway.Config;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppState))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(DiscoveredApp))]
[JsonSerializable(typeof(List<DiscoveredApp>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(BridgeIncomingMessage))]
[JsonSerializable(typeof(BridgeTabDto))]
[JsonSerializable(typeof(List<BridgeTabDto>))]
[JsonSerializable(typeof(BridgeCommandMessage))]
internal sealed partial class AppJsonContext : JsonSerializerContext
{
}
