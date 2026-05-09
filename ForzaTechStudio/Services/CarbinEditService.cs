using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ForzaTools.CarScene;

namespace ForzaTechStudio.Services;

// Applies post conversion edits to a carbin: ordinal/ID updates and media name + path rewrites
public class CarbinEditService
{
    public class EditOptions
    {
        public bool ApplyOrdinal { get; set; }
        public uint OriginalOrdinal { get; set; }
        public uint NewOrdinal { get; set; }

        public bool ApplyMediaName { get; set; }
        public string? OriginalMediaName { get; set; }
        public string? NewMediaName { get; set; }
    }

    public class EditResult
    {
        public int IdsUpdated;
        public int PathsUpdated;
        public List<string> Log { get; } = new();
    }

    // Reads a carbin's ordinal and media name without retaining the parsed scene.
    public static (uint Ordinal, string MediaName) ReadHeader(Stream stream)
    {
        var carbin = new CarbinFile();
        carbin.Load(stream);
        return (carbin.Scene.Ordinal, carbin.Scene.MediaName ?? string.Empty);
    }

    public EditResult ApplyToFile(string carbinPath, EditOptions options)
    {
        var carbin = new CarbinFile();
        using (var input = File.OpenRead(carbinPath))
            carbin.Load(input);

        var result = Apply(carbin.Scene, options);

        using var output = File.Create(carbinPath);
        carbin.Save(output);
        return result;
    }

    public EditResult Apply(Scene scene, EditOptions options)
    {
        var result = new EditResult();
        if (scene == null) return result;

        if (options.ApplyOrdinal && options.NewOrdinal != options.OriginalOrdinal)
        {
            scene.Ordinal = options.NewOrdinal;
            result.Log.Add($"Ordinal: {options.OriginalOrdinal} -> {options.NewOrdinal}");

            foreach (var part in scene.UpgradableParts)
            {
                foreach (var upg in part.Upgrades)
                {
                    if (TryRekey(upg.Id, options, out int newId))
                    {
                        upg.Id = newId;
                        result.IdsUpdated++;
                    }
                    if (upg.CarBodyId != -1 && TryRekey(upg.CarBodyId, options, out int newBody))
                    {
                        upg.CarBodyId = newBody;
                        result.IdsUpdated++;
                    }
                }
                foreach (var shared in part.SharedModels)
                {
                    for (int i = 0; i < shared.UpgradeIds.Count; i++)
                    {
                        if (TryRekey(shared.UpgradeIds[i], options, out int newRef))
                        {
                            shared.UpgradeIds[i] = newRef;
                            result.IdsUpdated++;
                        }
                    }
                }
            }

            result.Log.Add($"IDs updated: {result.IdsUpdated}");
        }

        if (options.ApplyMediaName &&
            !string.IsNullOrEmpty(options.OriginalMediaName) &&
            !string.IsNullOrEmpty(options.NewMediaName) &&
            !string.Equals(options.OriginalMediaName, options.NewMediaName, StringComparison.Ordinal))
        {
            string oldName = options.OriginalMediaName!;
            string newName = options.NewMediaName!;

            scene.MediaName = ReplaceCi(scene.MediaName, oldName, newName, ref result.PathsUpdated);
            scene.SkeletonPath = ReplaceCi(scene.SkeletonPath, oldName, newName, ref result.PathsUpdated);

            foreach (var entry in scene.NonUpgradableParts)
                RewriteModels(entry.Part.Models, oldName, newName, result);

            foreach (var part in scene.UpgradableParts)
            {
                foreach (var upg in part.Upgrades)
                    RewriteModels(upg.Models, oldName, newName, result);
                foreach (var shared in part.SharedModels)
                    RewriteModel(shared.Model, oldName, newName, result);
            }

            result.Log.Add($"Media name: {oldName} -> {newName} ({result.PathsUpdated} path(s) updated)");
        }

        return result;
    }

    private static bool TryRekey(int id, EditOptions options, out int newId)
    {
        newId = id;
        if (id < 0) return false;
        uint prefix = (uint)(id / 1000);
        if (prefix != options.OriginalOrdinal) return false;
        int suffix = id % 1000;
        newId = (int)(options.NewOrdinal * 1000 + (uint)suffix);
        return newId != id;
    }

    private static void RewriteModels(IEnumerable<CarRenderModel> models, string oldName, string newName, EditResult result)
    {
        foreach (var model in models)
            RewriteModel(model, oldName, newName, result);
    }

    private static void RewriteModel(CarRenderModel model, string oldName, string newName, EditResult result)
    {
        if (model == null) return;

        model.Path = ReplaceCi(model.Path, oldName, newName, ref result.PathsUpdated);
        model.AOSwatchPath = ReplaceCi(model.AOSwatchPath, oldName, newName, ref result.PathsUpdated);
        model.MotorsportUnkV18 = ReplaceCi(model.MotorsportUnkV18, oldName, newName, ref result.PathsUpdated);
        model.MotorsportUnkV19 = ReplaceCi(model.MotorsportUnkV19, oldName, newName, ref result.PathsUpdated);

        foreach (var ao in model.AOMapInfos)
            ao.Path = ReplaceCi(ao.Path, oldName, newName, ref result.PathsUpdated);
    }

    private static string? ReplaceCi(string? source, string oldValue, string newValue, ref int counter)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(oldValue)) return source;

        int index = source.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return source;

        var sb = new StringBuilder(source.Length);
        int cursor = 0;
        while (index >= 0)
        {
            sb.Append(source, cursor, index - cursor);
            sb.Append(newValue);
            cursor = index + oldValue.Length;
            counter++;
            index = source.IndexOf(oldValue, cursor, StringComparison.OrdinalIgnoreCase);
        }
        sb.Append(source, cursor, source.Length - cursor);
        return sb.ToString();
    }
}
