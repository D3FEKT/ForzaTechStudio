using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ForzaTechStudio.Services;

public sealed class ForzaGameDefinition
{
    public required string GameId { get; init; }
    public required string DisplayName { get; init; }
    public required string[] SignatureFiles { get; init; }
    public required string[] DetectionAliases { get; init; }
    public bool SupportsAutoDetect { get; init; } = true;
    public string[] RegistryPaths { get; init; } = [];
    public string? SteamAppId { get; init; }
    public string[]? SteamFolderNames { get; init; }
    public string[] CommonPaths { get; init; } = [];
}

public static class ForzaGameCatalog
{
    public static IReadOnlyList<ForzaGameDefinition> AllGames { get; } =
    [
        new()
        {
            GameId = "FH2",
            DisplayName = "Forza Horizon 2 (Xbox One)",
            SignatureFiles = ["Media"],
            DetectionAliases = ["fh2", "horizon 2"],
            SupportsAutoDetect = false,
        },
        new()
        {
            GameId = "FH3",
            DisplayName = "Forza Horizon 3",
            SignatureFiles = ["forzahorizon3.exe", "forza_x64_release.exe", "Media"],
            DetectionAliases = ["fh3", "horizon 3"],
            RegistryPaths =
            [
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft.SunriseBaseGame",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft.SunriseBaseGame"
            ],
            CommonPaths =
            [
                @"C:\Program Files\WindowsApps\Microsoft.SunriseBaseGame",
                @"D:\Program Files\WindowsApps\Microsoft.SunriseBaseGame",
                @"E:\Program Files\WindowsApps\Microsoft.SunriseBaseGame",
                @"C:\XboxGames\Forza Horizon 3",
                @"D:\XboxGames\Forza Horizon 3"
            ]
        },
        new()
        {
            GameId = "FH4",
            DisplayName = "Forza Horizon 4",
            SignatureFiles = ["forzahorizon4.exe", "Media"],
            DetectionAliases = ["fh4", "horizon 4"],
            RegistryPaths =
            [
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft.OpusReleaseFinal",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft.OpusReleaseFinal"
            ],
            SteamAppId = "1293830",
            SteamFolderNames = ["ForzaHorizon4", "Forza Horizon 4"],
            CommonPaths =
            [
                @"C:\Program Files\WindowsApps\Microsoft.OpusReleaseFinal",
                @"D:\Program Files\WindowsApps\Microsoft.OpusReleaseFinal",
                @"E:\Program Files\WindowsApps\Microsoft.OpusReleaseFinal",
                @"C:\XboxGames\Forza Horizon 4",
                @"D:\XboxGames\Forza Horizon 4",
                @"C:\Program Files (x86)\Steam\steamapps\common\ForzaHorizon4",
                @"D:\Program Files (x86)\Steam\steamapps\common\ForzaHorizon4"
            ]
        },
        new()
        {
            GameId = "FH5",
            DisplayName = "Forza Horizon 5",
            SignatureFiles = ["forzahorizon5.exe", "Media"],
            DetectionAliases = ["fh5", "horizon 5"],
            RegistryPaths =
            [
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft.624F8B84B80_8wekyb3d8bbwe",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft.624F8B84B80_8wekyb3d8bbwe"
            ],
            SteamAppId = "1551360",
            SteamFolderNames = ["ForzaHorizon5", "Forza Horizon 5"],
            CommonPaths =
            [
                @"C:\Program Files\WindowsApps\Microsoft.624F8B84B80_8wekyb3d8bbwe",
                @"D:\Program Files\WindowsApps\Microsoft.624F8B84B80_8wekyb3d8bbwe",
                @"E:\Program Files\WindowsApps\Microsoft.624F8B84B80_8wekyb3d8bbwe",
                @"C:\XboxGames\Forza Horizon 5",
                @"D:\XboxGames\Forza Horizon 5",
                @"C:\Program Files (x86)\Steam\steamapps\common\ForzaHorizon5",
                @"D:\Program Files (x86)\Steam\steamapps\common\ForzaHorizon5"
            ]
        },
        new()
        {
            GameId = "FH6",
            DisplayName = "Forza Horizon 6",
            SignatureFiles = ["Media"],
            DetectionAliases = ["fh6", "horizon 6"],
            SupportsAutoDetect = false,
        },
        new()
        {
            GameId = "FM5",
            DisplayName = "Forza Motorsport 5",
            SignatureFiles = ["Media"],
            DetectionAliases = ["fm5", "motorsport 5"],
            SupportsAutoDetect = false,
        },
        new()
        {
            GameId = "FM6",
            DisplayName = "Forza Motorsport 6 (Xbox One)",
            SignatureFiles = ["Media"],
            DetectionAliases = ["fm6", "motorsport 6"],
            SupportsAutoDetect = false,
        },
        new()
        {
            GameId = "FM7",
            DisplayName = "Forza Motorsport 7",
            SignatureFiles = ["forzamotorsport7.exe", "Media"],
            DetectionAliases = ["fm7", "motorsport 7"],
            RegistryPaths =
            [
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft.ApolloBaseGame",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft.ApolloBaseGame"
            ],
            CommonPaths =
            [
                @"C:\Program Files\WindowsApps\Microsoft.ApolloBaseGame",
                @"D:\Program Files\WindowsApps\Microsoft.ApolloBaseGame",
                @"E:\Program Files\WindowsApps\Microsoft.ApolloBaseGame",
                @"C:\XboxGames\Forza Motorsport 7",
                @"D:\XboxGames\Forza Motorsport 7"
            ]
        },
        new()
        {
            GameId = "FM2023",
            DisplayName = "Forza Motorsport (2023)",
            SignatureFiles = ["forza_gaming.desktop.x64_release_final.exe", "Media"],
            DetectionAliases = ["fm2023", "motorsport (2023)", "motorsport 2023", "forza motorsport"],
            RegistryPaths =
            [
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft.ForzaMotorsport_8wekyb3d8bbwe",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft.ForzaMotorsport_8wekyb3d8bbwe"
            ],
            SteamAppId = "2440510",
            SteamFolderNames = ["ForzaMotorsport", "Forza Motorsport"],
            CommonPaths =
            [
                @"C:\Program Files\WindowsApps\Microsoft.ForzaMotorsport_8wekyb3d8bbwe",
                @"D:\Program Files\WindowsApps\Microsoft.ForzaMotorsport_8wekyb3d8bbwe",
                @"E:\Program Files\WindowsApps\Microsoft.ForzaMotorsport_8wekyb3d8bbwe",
                @"C:\XboxGames\Forza Motorsport",
                @"D:\XboxGames\Forza Motorsport",
                @"C:\Program Files (x86)\Steam\steamapps\common\ForzaMotorsport",
                @"D:\Program Files (x86)\Steam\steamapps\common\ForzaMotorsport"
            ]
        },
    ];

    private static readonly Dictionary<string, ForzaGameDefinition> GamesById =
        AllGames.ToDictionary(game => game.GameId, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<ForzaGameDefinition> SetupGames => AllGames;

    public static IReadOnlyList<ForzaGameDefinition> MaterialLibraryGames => AllGames;

    public static bool TryGetGame(string gameId, out ForzaGameDefinition? game)
    {
        bool found = GamesById.TryGetValue(gameId ?? string.Empty, out var definition);
        game = definition;
        return found;
    }

    public static string GetDisplayName(string gameId)
    {
        return TryGetGame(gameId, out var game)
            ? game!.DisplayName
            : gameId ?? string.Empty;
    }

    public static IEnumerable<string> GetMatchingGameIds(string detectedGame)
    {
        if (string.IsNullOrWhiteSpace(detectedGame))
            return [];

        string text = detectedGame.ToLowerInvariant();

        return AllGames
            .Select(game => new
            {
                game.GameId,
                MatchIndex = GetMatchIndex(text, game)
            })
            .Where(item => item.MatchIndex >= 0)
            .OrderBy(item => item.MatchIndex)
            .Select(item => item.GameId);
    }

    public static string GetMaterialLibraryFileName(string gameId)
    {
        return $"Materials_{gameId?.Trim()}.json";
    }

    public static string GetPreferredMaterialLibraryGameId()
    {
        string materialsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Materials");
        foreach (var game in MaterialLibraryGames)
        {
            string path = Path.Combine(materialsDirectory, GetMaterialLibraryFileName(game.GameId));
            if (File.Exists(path))
                return game.GameId;
        }

        return "FH5";
    }

    private static int GetMatchIndex(string detectedGame, ForzaGameDefinition game)
    {
        int bestIndex = int.MaxValue;

        foreach (string alias in game.DetectionAliases.Prepend(game.GameId).Prepend(game.DisplayName))
        {
            int index = detectedGame.IndexOf(alias.ToLowerInvariant(), StringComparison.Ordinal);
            if (index >= 0)
                bestIndex = Math.Min(bestIndex, index);
        }

        return bestIndex == int.MaxValue ? -1 : bestIndex;
    }
}