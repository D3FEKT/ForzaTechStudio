using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForzaTechStudio.Services
{
    public class VLayElementData
    {
        [JsonPropertyName("semanticNameIndex")] public int SemanticNameIndex { get; set; }
        [JsonPropertyName("semanticIndex")]     public int SemanticIndex     { get; set; }
        [JsonPropertyName("inputSlot")]         public int InputSlot         { get; set; }
        [JsonPropertyName("inputSlotClass")]    public int InputSlotClass    { get; set; }
        [JsonPropertyName("format")]            public int Format            { get; set; }
        [JsonPropertyName("alignedByteOffset")] public int AlignedByteOffset { get; set; }
        [JsonPropertyName("instanceDataStepRate")] public int InstanceDataStepRate { get; set; }
    }

    public class VLayLibraryEntry
    {
        [JsonPropertyName("semanticNames")]  public List<string>       SemanticNames  { get; set; } = new();
        [JsonPropertyName("elements")]       public List<VLayElementData> Elements    { get; set; } = new();
        [JsonPropertyName("packedFormats")]  public List<int>          PackedFormats  { get; set; } = new();
        [JsonPropertyName("flags")]          public uint               Flags          { get; set; }
        [JsonPropertyName("description")]    public string             Description    { get; set; } = "";
        [JsonPropertyName("savedAt")]        public DateTime           SavedAt        { get; set; }
    }

    [JsonSerializable(typeof(Dictionary<string, VLayLibraryEntry>))]
    public partial class VLayJsonContext : JsonSerializerContext
    {
    }

    public static class VLayLibrary
    {
        private static Dictionary<string, VLayLibraryEntry> _cache;
        private static readonly string _jsonPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Materials", "vlaylibrary.json");

        private static void EnsureLoaded()
        {
            if (_cache != null) return;
            try
            {
                if (File.Exists(_jsonPath))
                {
                    string json = File.ReadAllText(_jsonPath);
                    _cache = JsonSerializer.Deserialize(json, VLayJsonContext.Default.DictionaryStringVLayLibraryEntry)
                             ?? new Dictionary<string, VLayLibraryEntry>();
                }
                else
                {
                    _cache = new Dictionary<string, VLayLibraryEntry>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"VLayLibrary Load Error: {ex.Message}");
                _cache = new Dictionary<string, VLayLibraryEntry>();
            }
        }

        public static Dictionary<string, VLayLibraryEntry> GetAllEntries()
        {
            EnsureLoaded();
            return _cache;
        }

        public static void AddOrUpdate(string name, VLayLibraryEntry entry)
        {
            EnsureLoaded();
            _cache[name] = entry;
            Save();
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_jsonPath)!);
                string json = JsonSerializer.Serialize(
                    _cache,
                    VLayJsonContext.Default.DictionaryStringVLayLibraryEntry);
                File.WriteAllText(_jsonPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"VLayLibrary Save Error: {ex.Message}");
            }
        }
    }
}
