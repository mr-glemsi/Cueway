using System.Text.Json.Serialization;
using Cueway.Media;
using Cueway.Startup;

namespace Cueway.Config;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppState))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(CustomThemePreset))]
[JsonSerializable(typeof(List<CustomThemePreset>))]
[JsonSerializable(typeof(DiscoveredApp))]
[JsonSerializable(typeof(List<DiscoveredApp>))]
[JsonSerializable(typeof(IgnoredAppEntry))]
[JsonSerializable(typeof(List<IgnoredAppEntry>))]
[JsonSerializable(typeof(SavedSiteVolumeEntry))]
[JsonSerializable(typeof(List<SavedSiteVolumeEntry>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(BridgeIncomingMessage))]
[JsonSerializable(typeof(BridgeTabDto))]
[JsonSerializable(typeof(List<BridgeTabDto>))]
[JsonSerializable(typeof(BridgeCommandMessage))]
[JsonSerializable(typeof(GitHubReleaseDto))]
[JsonSerializable(typeof(GitHubReleaseAssetDto))]
[JsonSerializable(typeof(List<GitHubReleaseAssetDto>))]
internal sealed partial class AppJsonContext : JsonSerializerContext
{
}
