using System;
using System.Collections.Generic;

namespace ForzaTechStudio.ViewModels
{

    internal static class FxbPropertySemantics
    {
        // Exact-match descriptions (key -> description, case-insensitive)
        private static readonly Dictionary<string, string> _exact = new(StringComparer.OrdinalIgnoreCase)
        {
            // Timing / lifecycle
            ["duration"]             = "Total lifetime of the emitter (seconds)",
            ["lifetime"]             = "Per-particle lifetime (seconds)",
            ["starttime"]            = "Delay before the emitter activates (seconds)",
            ["startdelay"]           = "Delay before first emission (seconds)",
            ["loopdelay"]            = "Pause between loop iterations (seconds)",
            ["loopcount"]            = "Number of times to repeat; -1 = infinite",
            ["playcount"]            = "Play count; -1 = infinite",

            // Emission
            ["emissionrate"]         = "Particles emitted per second",
            ["emission_rate"]        = "Particles emitted per second",
            ["burstcount"]           = "Number of particles released in a single burst",
            ["maxparticles"]         = "Maximum live particle count at any time",
            ["max_particles"]        = "Maximum live particle count at any time",
            ["spawnrate"]            = "Spawn rate (particles/sec)",

            // Size / scale
            ["size"]                 = "Uniform particle size / scale",
            ["startsize"]            = "Initial size at spawn",
            ["endsize"]              = "Final size at death",
            ["scale"]                = "Uniform scale multiplier",
            ["width"]                = "Particle billboard width",
            ["height"]               = "Particle billboard height",
            ["sizex"]                = "Size along X axis",
            ["sizey"]                = "Size along Y axis",

            // Velocity / movement
            ["velocity"]             = "Initial velocity magnitude (units/sec)",
            ["speed"]                = "Particle speed (units/sec)",
            ["startspeed"]           = "Speed at spawn",
            ["endspeed"]             = "Speed at death",
            ["velocityx"]            = "Velocity component along X",
            ["velocityy"]            = "Velocity component along Y",
            ["velocityz"]            = "Velocity component along Z",
            ["acceleration"]         = "Linear acceleration applied each frame",
            ["gravity"]              = "Gravity scale factor (1.0 = normal gravity)",
            ["drag"]                 = "Air-drag coefficient (slows velocity)",
            ["turbulence"]           = "Turbulence / noise strength",

            // Rotation
            ["rotation"]             = "Initial rotation (radians)",
            ["startrotation"]        = "Initial billboard rotation (radians)",
            ["endrotation"]          = "Final billboard rotation (radians)",
            ["rotationrate"]         = "Rotation speed (radians/sec)",
            ["angularvelocity"]      = "Angular velocity (radians/sec)",
            ["spin"]                 = "Spin rate (radians/sec)",

            // Color / alpha
            ["color"]                = "Particle tint color (ARGB keyframe)",
            ["colour"]               = "Particle tint color (ARGB keyframe)",
            ["startcolor"]           = "Color at spawn",
            ["endcolor"]             = "Color at death",
            ["alpha"]                = "Opacity (0 = transparent, 1 = opaque)",
            ["startalpha"]           = "Opacity at spawn",
            ["endalpha"]             = "Opacity at death",
            ["alphastart"]           = "Opacity at spawn",
            ["alphaend"]             = "Opacity at death",
            ["opacity"]              = "Overall opacity multiplier",
            ["brightness"]           = "Brightness/intensity multiplier",
            ["emissive"]             = "Emissive light contribution scale",

            // Position / offset
            ["position"]             = "Spawn position offset from emitter origin",
            ["offset"]               = "Positional offset (Vector3)",
            ["offsetx"]              = "X-axis positional offset",
            ["offsety"]              = "Y-axis positional offset",
            ["offsetz"]              = "Z-axis positional offset",
            ["emittercenter"]        = "Center of the emission volume",
            ["emitterextents"]       = "Half-extents of the emission bounding box",
            ["emitterradius"]        = "Radius of a spherical / disc emitter",
            ["innerradius"]          = "Inner radius (hollow sphere / torus emitter)",
            ["outerradius"]          = "Outer radius of the emission volume",

            // Texture / UV
            ["uvscale"]              = "Texture UV tiling scale",
            ["uvoffset"]             = "Texture UV offset",
            ["uvscrollu"]            = "UV scroll speed along U axis",
            ["uvscrollv"]            = "UV scroll speed along V axis",
            ["framesperrow"]         = "Number of sprite-sheet columns",
            ["numframes"]            = "Total sprite-sheet frame count",
            ["framerate"]            = "Animation playback speed (frames/sec)",
            ["startframe"]           = "First frame index in sprite-sheet",

            // Physics / collision
            ["bounciness"]           = "Coefficient of restitution on collision",
            ["restitution"]          = "Bounce restitution (0 = no bounce, 1 = perfect)",
            ["friction"]             = "Surface friction applied on collision",
            ["mass"]                 = "Particle mass (affects physics forces)",
            ["buoyancy"]             = "Buoyancy force in fluid simulation",

            // LOD
            ["loddistance"]          = "Distance at which this LOD level activates",
            ["lodscale"]             = "Emission-rate scale factor for this LOD",
            ["lodbias"]              = "LOD transition bias",

            // Misc
            ["sortkey"]              = "Render sort priority key",
            ["shadowstrength"]       = "Shadow contribution strength (0-1)",
            ["distancefade"]         = "Distance at which particles begin to fade",
            ["nearclipdistance"]     = "Near-clip fade start distance",
            ["farclipdistance"]      = "Far-clip fade start distance",
            ["windinfluence"]        = "Scale of global wind force on particles",
            ["windscale"]            = "Wind speed multiplier",
            ["depthfade"]            = "Softparticle depth-fade distance",
            ["depthbias"]            = "Depth-buffer bias for rendering",
            ["tilecount"]            = "World-tile repetition count",
            ["trailwidth"]           = "Width of ribbon/trail particle strand",
            ["trailsegments"]        = "Number of segments in a ribbon trail",
            ["ribbonwidth"]          = "Ribbon width at the current segment",
            ["stretchamount"]        = "Velocity-stretch scale for stretched billboards",
        };

        // Substring hints for partial name matches (checked in order)
        private static readonly (string Substring, string Hint)[] _partial =
        [
            ("color",       "Particle color channel (ARGB)"),
            ("colour",      "Particle color channel (ARGB)"),
            ("alpha",       "Opacity / transparency value"),
            ("size",        "Particle size or scale"),
            ("velocity",    "Velocity component (units/sec)"),
            ("speed",       "Speed value (units/sec)"),
            ("lifetime",    "Particle lifetime (seconds)"),
            ("duration",    "Duration (seconds)"),
            ("rotation",    "Rotation angle or rate"),
            ("spawn",       "Spawn / emission parameter"),
            ("emit",        "Emission parameter"),
            ("radius",      "Radius of emission volume"),
            ("offset",      "Positional offset"),
            ("scale",       "Scale multiplier"),
            ("gravity",     "Gravity influence"),
            ("wind",        "Wind influence"),
            ("fade",        "Fade/transition distance"),
            ("lod",         "Level-of-detail parameter"),
            ("uv",          "Texture UV parameter"),
            ("frame",       "Sprite-sheet animation parameter"),
            ("trail",       "Trail / ribbon parameter"),
            ("ribbon",      "Ribbon strand parameter"),
            ("stretch",     "Velocity-stretch parameter"),
            ("sort",        "Render sort parameter"),
            ("shadow",      "Shadow parameter"),
        ];


        public static string Describe(string name, FxbPropertyType type)
        {
            if (!string.IsNullOrEmpty(name))
            {
                if (_exact.TryGetValue(name, out var exact))
                    return exact;

                var lower = name.ToLowerInvariant();
                foreach (var (sub, hint) in _partial)
                {
                    if (lower.Contains(sub))
                        return hint;
                }
            }

            // Generic fallback per type
            return type switch
            {
                FxbPropertyType.FloatKeyFrame      => "Float value animated over time (keyframe curve)",
                FxbPropertyType.ColorARGBKeyFrame  => "ARGB color animated over time (keyframe curve)",
                FxbPropertyType.FloatRange         => "Float value sampled from a min-max range",
                FxbPropertyType.IntegerRange       => "Integer value sampled from a min-max range",
                FxbPropertyType.Integer            => "Inline integer constant",
                FxbPropertyType.Float              => "Inline float constant",
                FxbPropertyType.String             => "String reference (name or identifier)",
                FxbPropertyType.Vector3            => "3-component float vector (X, Y, Z)",
                FxbPropertyType.Vector3Range       => "Two Vector3 values defining a range",
                FxbPropertyType.Vector4            => "4-component float vector (X, Y, Z, W)",
                FxbPropertyType.FloatArray         => "Array of float values (channel table)",
                FxbPropertyType.IntegerArray       => "Array of integer values (channel table)",
                FxbPropertyType.FixedFunction      => "Fixed-function curve (quadratic / sinusoidal)",
                FxbPropertyType.StringArray        => "Array of string references",
                _                                  => $"Property of type {type}",
            };
        }
    }
}
