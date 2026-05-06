using ForzaTools.Bundles;
using ForzaTools.CarScene;
using ForzaTools.Shared;

namespace ForzaTechStudio.Services;

// Facade that delegates to ModelbinConversionService and CarbinConversionService.
// Preserves backward compatibility with callers that reference this type.
public class ModelCarbinConversionService
{
    private readonly ModelbinConversionService _modelbinService;
    private readonly CarbinConversionService _carbinService;

    public ModelCarbinConversionService(ModelbinConversionService modelbinService, CarbinConversionService carbinService)
    {
        _modelbinService = modelbinService;
        _carbinService = carbinService;
    }

    // Converts a modelbin bundle's blobs in-place to the specified target game format.
    public void ConvertModelbinBundle(Bundle bundle, ForzaGameTarget target, ConversionResult result, ConversionOptions? options = null)
    {
        _modelbinService.ConvertModelbinBundle(bundle, target, result, options);
    }

    // Converts a carbin file to the specified target game format.
    public ConversionResult ConvertCarbin(string inputPath, string outputPath, ForzaGameTarget target, ConversionOptions? options = null)
    {
        return _carbinService.ConvertCarbin(inputPath, outputPath, target, options);
    }

    // Gets modelbin target versions for a given game. Delegates to ModelbinConversionService.
    public static (
        (byte maj, byte min) bundle,
        (byte maj, byte min) modl,
        (byte maj, byte min) mesh,
        (byte maj, byte min) vlay
    ) GetModelbinTargetVersions(ForzaGameTarget target)
    {
        return ModelbinConversionService.GetModelbinTargetVersions(target);
    }

    // Gets carbin target versions for a given game. Delegates to CarbinConversionService.
    public static (
        ushort sceneVer,
        ushort modelVer,
        GameSeries series,
        ushort partVer,
        ushort upgPartVer,
        ushort upgradeVer
    ) GetCarbinTargetVersions(ForzaGameTarget target)
    {
        return CarbinConversionService.GetCarbinTargetVersions(target);
    }
}
