using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using ForzaTechStudio.Services;

namespace ForzaTechStudio.Models
{
    public class SettingsConfig
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";

        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("gamePaths")]
        public Dictionary<string, string> GamePaths { get; set; } = new();

        [JsonPropertyName("autoDetectOnStartup")]
        public bool AutoDetectOnStartup { get; set; } = false;

        [JsonPropertyName("validateOnLoad")]
        public bool ValidateOnLoad { get; set; } = true;

        [JsonPropertyName("enableAdvancedVlayBlobPatch")]
        public bool EnableAdvancedVlayBlobPatch { get; set; } = false;

        // Appearance
        [JsonPropertyName("theme")]
        public string Theme { get; set; } = "System";

        [JsonPropertyName("backdropMaterial")]
        public string BackdropMaterial { get; set; } = "Mica";

        [JsonPropertyName("accentColor")]
        public string AccentColor { get; set; } = "System";

        [JsonPropertyName("navigationStyle")]
        public string NavigationStyle { get; set; } = "Expanded";

        [JsonPropertyName("hasShownWelcome")]
        public bool HasShownWelcome { get; set; } = false;

        // Tracks the last app version that showed the welcome screen
        [JsonPropertyName("appVersion")]
        public string AppVersion { get; set; } = "";

        [JsonPropertyName("defaultGameId")]
        public string DefaultGameId { get; set; } = "";

        public SettingsConfig()
        {
            // Initialize with empty paths for all supported games
            foreach (var game in ForzaGameCatalog.AllGames)
            {
                GamePaths.TryAdd(game.GameId, "");
            }
        }

        public bool IsValid()
        {
            return Version != null && GamePaths != null;
        }

        public void UpdateTimestamp()
        {
            LastUpdated = DateTime.UtcNow;
        }
    }
}
