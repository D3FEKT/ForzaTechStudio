using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace ForzaTechStudio.Models
{
    public partial class GameInstallation : ObservableObject
    {
        [ObservableProperty]
        private string _gameId = "";

        [ObservableProperty]
        private string _displayName = "";

        [ObservableProperty]
        private string _path = "";

        [ObservableProperty]
        private bool _isValid = false;

        [ObservableProperty]
        private string _statusMessage = "Not configured";

        [ObservableProperty]
        private string _detectedExecutable = "";

        [ObservableProperty]
        private DateTime? _lastValidated;

        [ObservableProperty]
        private bool _hasDatabase;

        [ObservableProperty]
        private int _dbEntryCount;

        [ObservableProperty]
        private DateTime? _dbLastBuilt;

        public string[] SignatureFiles { get; set; } = Array.Empty<string>();

        public string DbStatusText => HasDatabase
            ? $"Database: {DbEntryCount} entries (built {DbLastBuilt?.ToLocalTime():g})"
            : "No database";

        public GameInstallation()
        {
        }

        public GameInstallation(string gameId, string displayName, string[] signatureFiles)
        {
            GameId = gameId;
            DisplayName = displayName;
            SignatureFiles = signatureFiles;
        }

        public void UpdateStatus(bool isValid, string message, string? exeName = null)
        {
            IsValid = isValid;
            StatusMessage = message;
            _detectedExecutable = exeName ?? "";
            _lastValidated = DateTime.Now;
        }

        partial void OnHasDatabaseChanged(bool value) => OnPropertyChanged(nameof(DbStatusText));
        partial void OnDbEntryCountChanged(int value) => OnPropertyChanged(nameof(DbStatusText));
        partial void OnDbLastBuiltChanged(DateTime? value) => OnPropertyChanged(nameof(DbStatusText));
    }
}
