using System;
using System.Collections.Generic;
using System.Linq;

namespace ForzaTechStudio.Services;

// Categorizes modelbin files by expected VLay pattern to prevent incorrect element additions during conversion.
public enum VLayPatternCategory
{
    // Full layout: all tangents and texcoords (NORMAL+TANGENTs+TEXCOORDs). Examples: body, bumper, hood, door, trunk.
    Full,

    // Secondary detail: TANGENT1 but no TANGENT0. Used by calipers, rotors, drums, and wheel meshes.
    DetailSecondary,

    // Minimal: NORMAL + one or two TEXCOORDs only, no tangents. Glass parts, simplified LODs, side markers.
    Minimal,

    // Interior reduced: partial tangent/texcoord set. Interior/console parts, some drum parts.
    InteriorReduced,

    // Empty VLay (0 elements, stride=0) � position-only layout.
    // Present in ALL games for the same modelbins. Should never have elements added.
    PositionOnly,

    // Unknown � use default source-aware conversion logic without filename hinting.
    Unknown
}

// Advises VLay conversion on per-element additions/removals based on modelbin filename and target game.
public static class VLayPatternAdvisor
{
    // Determines the expected VLay pattern category for a modelbin based on its filename.
    // This uses naming conventions observed across all 7 analyzed Forza titles.
    public static VLayPatternCategory CategorizeModelbin(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return VLayPatternCategory.Unknown;

        // Normalize: strip path, lowercase for matching
        string name = System.IO.Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();

        // Calipers, rotors, drums, and wheels consistently use DetailSecondary (TANGENT1 only, no TANGENT0).
        if (IsCaliper(name) || IsRotor(name) || IsDrum(name))
            return VLayPatternCategory.DetailSecondary;

        // Wheels use DetailSecondary to avoid incorrectly adding TANGENT0 during conversion.
        if (IsWheel(name))
            return VLayPatternCategory.DetailSecondary;

        // Full layout parts: body, bumper, fender, hood, trunk, door, headlight, engine, interior, etc.
        if (IsFullLayoutPart(name))
            return VLayPatternCategory.Full;

        // Default: unknown, let the source-aware converter decide
        return VLayPatternCategory.Unknown;
    }

    // Returns the set of elements to suppress from being added, based on source layout, filename, and target game. FH5 never suppresses TANGENT2/TEXCOORD0.
    public static HashSet<(string name, short idx)> GetSuppressedAdditions(
        string fileName,
        HashSet<(string name, short idx)> sourceSemantics,
        int sourceSlot1ElementCount,
        int sourceSlot1Stride,
        ForzaGameTarget target)
    {
        var suppressed = new HashSet<(string name, short idx)>();

        // If the source has 0 slot1 elements (position-only), never add anything
        if (sourceSlot1ElementCount == 0)
            return suppressed; // Empty � all additions will be naturally suppressed since no tangent/texcoord exists

        var category = CategorizeModelbin(fileName);

        // Suppress all extras for very minimal layouts (<=2 elements, stride <=12).
        if (sourceSlot1ElementCount <= 2 && sourceSlot1Stride <= 12)
        {
            suppressed.Add(("COLOR", 0));
            suppressed.Add(("TANGENT", 0));
            suppressed.Add(("TANGENT", 1));
            suppressed.Add(("TANGENT", 2));
            suppressed.Add(("TEXCOORD", 4));
        }
        // Source with 3 elements and stride ?16: still a reduced part
        // FM6+: 3 elem stride=12 (NORMAL+TEXCOORD0+TEXCOORD2, NORMAL+TEXCOORD2+TEXCOORD3)
        // FH2: 3 elem stride=16 (NORMAL+TEXCOORD0+TEXCOORD2)
        else if (sourceSlot1ElementCount <= 3 && sourceSlot1Stride <= 16 && !sourceSemantics.Any(s => s.name == "TANGENT"))
        {
            suppressed.Add(("COLOR", 0));
            suppressed.Add(("TANGENT", 2));
            suppressed.Add(("TEXCOORD", 4));
        }

        // === Source-aware: if source has no TEXCOORD0 ===
        // Parts with partial texcoord sets (e.g. glass parts with TEXCOORD1-3 but no TEXCOORD0)
        // should NOT get TEXCOORD4 � FH5 doesn't add TEXCOORD4 to these layouts either
        if (!sourceSemantics.Contains(("TEXCOORD", 0)) && sourceSemantics.Contains(("TEXCOORD", 1)))
        {
            suppressed.Add(("TEXCOORD", 4));
        }

        // === Source-aware: if source has only NORMAL (1 elem) ===
        // FH2: 1 elem stride=8 (NORMAL only), FH3: 1 elem stride=4 (NORMAL only)
        // FM2023: 1 elem stride=4 (NORMAL only)
        // These should never get anything added
        if (sourceSlot1ElementCount == 1)
        {
            suppressed.Add(("COLOR", 0));
            suppressed.Add(("TANGENT", 0));
            suppressed.Add(("TANGENT", 1));
            suppressed.Add(("TANGENT", 2));
            suppressed.Add(("TEXCOORD", 0));
            suppressed.Add(("TEXCOORD", 1));
            suppressed.Add(("TEXCOORD", 2));
            suppressed.Add(("TEXCOORD", 3));
            suppressed.Add(("TEXCOORD", 4));
        }

        // === Category-specific suppressions ===
        switch (category)
        {
            case VLayPatternCategory.DetailSecondary:
                // Brake/wheel parts never have TANGENT0; suppress it during conversion.
                suppressed.Add(("TANGENT", 0));
                break;

            case VLayPatternCategory.Minimal:
                // Minimal parts: suppress everything extra
                suppressed.Add(("COLOR", 0));
                suppressed.Add(("TANGENT", 0));
                suppressed.Add(("TANGENT", 1));
                suppressed.Add(("TANGENT", 2));
                suppressed.Add(("TEXCOORD", 4));
                break;

            case VLayPatternCategory.InteriorReduced:
                // Interior reduced: suppress TEXCOORD4
                suppressed.Add(("TEXCOORD", 4));
                break;
        }

        // Suppress COLOR0 for FM2023 targets (FM2023 full layouts typically omit it).
        if (target == ForzaGameTarget.FM2023 && sourceSlot1ElementCount < 10)
        {
            suppressed.Add(("COLOR", 0));
        }

        // FH5 always requires TANGENT2 and TEXCOORD0; restore them if suppressed above.
        if (target == ForzaGameTarget.FH5)
        {
            suppressed.Remove(("TANGENT", 2));
            suppressed.Remove(("TEXCOORD", 0));
        }

        return suppressed;
    }

    // Gets a human-readable description of what VLay pattern a modelbin should expect
    // for the given target game. Useful for logging during conversion.
    public static string GetPatternDescription(string fileName, int sourceElementCount, int sourceStride, ForzaGameTarget target)
    {
        if (sourceElementCount == 0)
            return "Position-only (0 elem, stride=0) � no slot1 conversion needed";

        var category = CategorizeModelbin(fileName);
        string catName = category switch
        {
            VLayPatternCategory.Full => "Full layout",
            VLayPatternCategory.DetailSecondary => "Detail/secondary (no TANGENT0)",
            VLayPatternCategory.Minimal => "Minimal (NORMAL + TEXCOORD only)",
            VLayPatternCategory.InteriorReduced => "Interior reduced",
            VLayPatternCategory.PositionOnly => "Position-only",
            _ => "Unknown (using source-aware defaults)"
        };

        return $"{catName} � source: {sourceElementCount} elem, stride={sourceStride}";
    }

    #region Filename Pattern Matching

    private static bool IsCaliper(string name)
    {
        return name.Contains("caliper");
    }

    private static bool IsRotor(string name)
    {
        return name.Contains("rotor") && !name.Contains("rotorcraft");
    }

    private static bool IsDrum(string name)
    {
        return name.StartsWith("drum") || name.Contains("_drum") ||
               // Match patterns like "CHE_drumLR_008", "FOR_drumRR_001", "DOD_drumLR_008"
               (name.Contains("drum") && (name.Contains("lf") || name.Contains("lr") ||
                                           name.Contains("rf") || name.Contains("rr")));
    }

    private static bool IsWheel(string name)
    {
        // Wheel modelbins consistently use DetailSecondary (TANGENT1, no TANGENT0).
        return name.Contains("wheel") && !name.Contains("steering");
    }

    private static bool IsFullLayoutPart(string name)
    {
        // Full-layout keyword list derived from cross-game VLay analysis (body, bumper, door, hood, engine, interior, etc.).
        string[] fullLayoutKeywords =
        [
            "body", "bumper", "fender", "hood", "trunk", "door", "wing", "skirt",
            "grille", "exhaust", "headlight", "taillight", "foglight",
            "glass", "engine", "enginebay", "interior", "dash", "seat",
            "pedal", "steering", "shifter", "console", "speaker",
            "floor", "roof", "pillar", "visor", "mirror",
            "undercarriage", "suspension", "controlarm",
            "rollcage", "antenna", "wiper", "washer",
            "badge", "emblem", "livery", "custom", "diamond", "honeycomb", "trim",
            "sidemarker", "gascap", "vent", "splitter",
            "lens", "reflector", "lightbulb", "bulbs",
            "spare", "misc", "nav", "gauge", "needle",
            "seatbelt", "rearview", "parcelshelf", "overhead",
            "storage", "sunglass", "doorjamb", "doorsill",
            "hoodliner", "trunkliner", "trunkbay",
            "bumperframe", "doorcard", "doorhandle",
            "gaugepod", "steeringcolumn"
        ];

        foreach (var keyword in fullLayoutKeywords)
        {
            if (name.Contains(keyword))
                return true;
        }

        return false;
    }

    #endregion
}
