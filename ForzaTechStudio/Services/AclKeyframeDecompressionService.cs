using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace ForzaTechStudio.Services
{
    public sealed class AclCompressedAnimation
    {
        public byte[] CompressedTransformData { get; init; } = Array.Empty<byte>();
        public byte[] CompressedRootTransformData { get; init; } = Array.Empty<byte>();
        public byte[] CompressedFloatChannelData { get; init; } = Array.Empty<byte>();
        public ulong[] BoneHashes { get; init; } = Array.Empty<ulong>();
        public uint NumSamples { get; init; }
        public float Duration { get; init; }
        public byte AnimType { get; init; }
        public byte VersionNumber { get; init; }
    }

    public readonly record struct AclTransformSample(Vector3 Translation, Quaternion Rotation, Vector3 Scale);

    public sealed class AclDecompressedTrack
    {
        public ulong BoneHash { get; init; }
        public string Name { get; init; } = string.Empty;
        public AclTransformSample[] Samples { get; init; } = Array.Empty<AclTransformSample>();
    }

    public sealed class AclDecompressedClip
    {
        public float Duration { get; init; }
        public uint NumSamples { get; init; }
        public List<AclDecompressedTrack> Tracks { get; } = new();

        public bool WasNativeDecompressed { get; set; }
    }

    public static class AclKeyframeDecompressionService
    {
        private const string NativeLibraryName = "forzatech_acl";

        private static bool? _nativeAvailable;
        private static string _nativeStatus = "Not checked";


        public static bool IsNativeAclAvailable
        {
            get
            {
                if (_nativeAvailable.HasValue)
                    return _nativeAvailable.Value;
                try
                {
                    if (!NativeLibrary.TryLoad(NativeLibraryName, out nint handle))
                    {
                        _nativeStatus = "DLL not found (forzatech_acl.dll missing from output directory)";
                        _nativeAvailable = false;
                        return false;
                    }
                    NativeLibrary.Free(handle);
                    _nativeStatus = "Loaded successfully";
                    _nativeAvailable = true;
                    return true;
                }
                catch (Exception ex)
                {
                    _nativeStatus = $"Failed to load: {ex.Message}";
                    _nativeAvailable = false;
                    return false;
                }
            }
        }


        public static string NativeStatus => _nativeStatus;

        public static AclDecompressedClip Decompress(AclCompressedAnimation animation, IReadOnlyDictionary<ulong, string> boneNames)
        {
            ArgumentNullException.ThrowIfNull(animation);

            if (animation.NumSamples == 0 || animation.BoneHashes.Length == 0)
            {
                return new AclDecompressedClip
                {
                    Duration = Math.Max(animation.Duration, 0f),
                    NumSamples = animation.NumSamples,
                    WasNativeDecompressed = false
                };
            }

            if (TryNativeDecompress(animation, boneNames, out var nativeClip))
                return nativeClip;

            // Native decompression not available or failed, fall back to identity clip
            var identity = BuildIdentityClip(animation, boneNames);
            identity.WasNativeDecompressed = false;
            return identity;
        }

        public static GrannyAnimation ToGrannyAnimation(string name, AclCompressedAnimation animation, IReadOnlyDictionary<ulong, string> boneNames)
        {
            var decompressed = Decompress(animation, boneNames);
            var granny = new GrannyAnimation
            {
                Name = string.IsNullOrWhiteSpace(name) ? "clipd_acl_clip" : name,
                Duration = decompressed.Duration,
                TimeStep = decompressed.NumSamples > 1 && decompressed.Duration > 0f
                    ? decompressed.Duration / (decompressed.NumSamples - 1)
                    : 0f,
                Oversampling = 1f,
                DefaultLoopCount = 0,
                Flags = 0,
                IsNativeDecompressed = decompressed.WasNativeDecompressed
            };

            var group = new GrannyTrackGroup
            {
                Name = granny.Name,
                Flags = 0,
                LoopTranslation = Vector3.Zero
            };

            foreach (var track in decompressed.Tracks)
            {
                var transformTrack = new GrannyTransformTrack
                {
                    Name = track.Name,
                    BoneHash = track.BoneHash,
                    Flags = 0,
                    PositionCurve = BuildCurve(track.Samples, decompressed.Duration, 3, sample => new[]
                    {
                        sample.Translation.X, sample.Translation.Y, sample.Translation.Z
                    }, Vector3.Zero),
                    OrientationCurve = BuildCurve(track.Samples, decompressed.Duration, 4, sample => new[]
                    {
                        sample.Rotation.X, sample.Rotation.Y, sample.Rotation.Z, sample.Rotation.W
                    }, Quaternion.Identity),
                    ScaleShearCurve = BuildCurve(track.Samples, decompressed.Duration, 3, sample => new[]
                    {
                        sample.Scale.X, sample.Scale.Y, sample.Scale.Z
                    }, Vector3.One)
                };

                int sampleCount = track.Samples.Length;
                for (int i = 0; i < sampleCount; i++)
                {
                    transformTrack.Keyframes.Add(new TransformKeyframe
                    {
                        Time = SampleTime(i, sampleCount, decompressed.Duration),
                        Position = track.Samples[i].Translation,
                        Orientation = track.Samples[i].Rotation,
                        Scale = track.Samples[i].Scale
                    });
                }

                group.TransformTracks.Add(transformTrack);
            }

            granny.TrackGroups.Add(group);
            return granny;
        }

        private static GrannyCurveInfo BuildCurve<TIdentity>(AclTransformSample[] samples, float duration, int dimension, Func<AclTransformSample, float[]> selector, TIdentity identity)
        {
            if (samples.Length == 0)
                return new GrannyCurveInfo { Dimension = dimension, IsIdentity = true, FormatName = "ACL.Empty" };

            var controls = new float[samples.Length * dimension];
            bool isIdentity = true;
            for (int i = 0; i < samples.Length; i++)
            {
                var values = selector(samples[i]);
                for (int d = 0; d < dimension; d++)
                    controls[i * dimension + d] = d < values.Length ? values[d] : 0f;
                isIdentity &= IsIdentitySample(values, identity);
            }

            var knots = new float[samples.Length];
            for (int i = 0; i < knots.Length; i++)
                knots[i] = SampleTime(i, knots.Length, duration);

            return new GrannyCurveInfo
            {
                FormatName = "ACL.NativeSamples",
                IsIdentity = isIdentity,
                IsConstant = samples.Length == 1,
                Knots = knots,
                Controls = controls,
                Dimension = dimension,
                Degree = 1
            };
        }

        private static bool IsIdentitySample<TIdentity>(float[] values, TIdentity identity)
        {
            const float epsilon = 1e-5f;
            if (identity is Vector3 vector)
            {
                return values.Length >= 3
                    && Math.Abs(values[0] - vector.X) < epsilon
                    && Math.Abs(values[1] - vector.Y) < epsilon
                    && Math.Abs(values[2] - vector.Z) < epsilon;
            }

            if (identity is Quaternion quaternion)
            {
                return values.Length >= 4
                    && Math.Abs(values[0] - quaternion.X) < epsilon
                    && Math.Abs(values[1] - quaternion.Y) < epsilon
                    && Math.Abs(values[2] - quaternion.Z) < epsilon
                    && Math.Abs(values[3] - quaternion.W) < epsilon;
            }

            return false;
        }

        private static AclDecompressedClip BuildIdentityClip(AclCompressedAnimation animation, IReadOnlyDictionary<ulong, string> boneNames)
        {
            var clip = new AclDecompressedClip
            {
                Duration = Math.Max(animation.Duration, 0f),
                NumSamples = Math.Max(animation.NumSamples, 1)
            };

            int sampleCount = (int)Math.Clamp(clip.NumSamples, 1, 4096);
            foreach (ulong boneHash in animation.BoneHashes)
            {
                string name = boneNames.TryGetValue(boneHash, out string? mappedName)
                    ? mappedName
                    : $"0x{boneHash:X16}";

                var samples = new AclTransformSample[sampleCount];
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = new AclTransformSample(Vector3.Zero, Quaternion.Identity, Vector3.One);

                clip.Tracks.Add(new AclDecompressedTrack
                {
                    BoneHash = boneHash,
                    Name = name,
                    Samples = samples
                });
            }

            return clip;
        }

        private static bool TryNativeDecompress(AclCompressedAnimation animation, IReadOnlyDictionary<ulong, string> boneNames, out AclDecompressedClip clip)
        {
            clip = new AclDecompressedClip
            {
                Duration = Math.Max(animation.Duration, 0f),
                NumSamples = Math.Max(animation.NumSamples, 1),
                WasNativeDecompressed = false
            };

            try
            {
                int boneCount = animation.BoneHashes.Length;
                int sampleCount = (int)Math.Clamp(clip.NumSamples, 1, 4096);
                int transformFloatCount = checked(boneCount * sampleCount * 10);
                float[] transforms = new float[transformFloatCount];

                // P/Invoke expects uint32 bone hashes (lower 32 bits of each Identifier<uint64>)
                uint[] boneHashes32 = Array.ConvertAll(animation.BoneHashes, h => (uint)h);
                int result = acl_decompress_pose_samples(
                    animation.CompressedTransformData,
                    animation.CompressedTransformData.Length,
                    boneHashes32,
                    boneCount,
                    sampleCount,
                    animation.Duration,
                    transforms,
                    transforms.Length);

                if (result <= 0)
                    return false;

                for (int boneIndex = 0; boneIndex < boneCount; boneIndex++)
                {
                    ulong boneHash = animation.BoneHashes[boneIndex];
                    string name = boneNames.TryGetValue(boneHash, out string? mappedName)
                        ? mappedName
                        : $"bone_{boneHash:X16}";
                    var samples = new AclTransformSample[sampleCount];
                    for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                    {
                        int offset = (sampleIndex * boneCount + boneIndex) * 10;
                        var rotation = new Quaternion(transforms[offset + 3], transforms[offset + 4], transforms[offset + 5], transforms[offset + 6]);
                        samples[sampleIndex] = new AclTransformSample(
                            new Vector3(transforms[offset + 0], transforms[offset + 1], transforms[offset + 2]),
                            rotation.LengthSquared() > 1e-12f ? Quaternion.Normalize(rotation) : Quaternion.Identity,
                            new Vector3(transforms[offset + 7], transforms[offset + 8], transforms[offset + 9]));
                    }

                    clip.Tracks.Add(new AclDecompressedTrack
                    {
                        BoneHash = boneHash,
                        Name = name,
                        Samples = samples
                    });
                }

                clip.WasNativeDecompressed = true;
                return boneCount > 0;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (BadImageFormatException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static float SampleTime(int index, int sampleCount, float duration)
        {
            if (sampleCount <= 1 || duration <= 0f) return 0f;
            return duration * index / (sampleCount - 1);
        }

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int acl_decompress_pose_samples(
            byte[] compressedTransformData,
            int compressedTransformDataSize,
            uint[] boneHashes,
            int boneCount,
            int sampleCount,
            float duration,
            [Out] float[] outTransforms,
            int outTransformFloatCount);
    }
}
