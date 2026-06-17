using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace ForzaTechStudio.Services
{
    public sealed class ClipdSkeldParseResult
    {
        public GrannyFileData FileData { get; init; } = new();
        public List<AclCompressedAnimation> AclAnimations { get; } = new();
        public List<string> Notes { get; } = new();
        public List<string> Warnings { get; } = new();
    }

    public sealed class ClipdSkeldParserService
    {
        private const uint BsiMagic = 0xB1A414CC;
        private const uint GrubMagic = 0x47727562;

        //BSI type hashes 
        private const ulong AclAnimationDataHash = 0xCBE977A85A54EB5E;
        private const ulong AclAnimationHash = 0xEAA09FC7E4E9009E;
        private const ulong ClipDictionaryHash = 0xB581FE38EDC12453;
        private const ulong SkeletonDictionaryHash = 0xF2FDC126D251B7E2;
        private const ulong SkeletonHash = 0xB107888537A7DF23;

        private readonly Dictionary<uint, string> _fnv32BoneNames = new();
        private readonly Dictionary<ulong, string> _fnv64BoneNames = new();


        private IReadOnlyDictionary<ulong, string> GetMergedBoneNames()
        {
            var merged = new Dictionary<ulong, string>(_fnv64BoneNames);
            foreach (var kvp in AnimationNameTable.HashToBoneName)
            {
                if (!merged.ContainsKey(kvp.Key))
                    merged[kvp.Key] = kvp.Value;
            }
            return merged;
        }

        public ClipdSkeldParserService()
        {
            LoadBuiltInBoneNames();
            LoadExternalBoneLookup();
        }

        public ClipdSkeldParseResult Parse(string filePath)
        {
            return Parse(File.ReadAllBytes(filePath), Path.GetFileName(filePath));
        }

        public ClipdSkeldParseResult Parse(byte[] bytes, string sourceName)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            var result = new ClipdSkeldParseResult
            {
                FileData = new GrannyFileData
                {
                    IsValid = true,
                    IsGsf = false,
                    SourceFileName = sourceName,
                    StatusMessage = "CLIPD/SKELD BSI parser"
                }
            };

            try
            {
                if (bytes.Length < 4)
                    throw new InvalidDataException("File is too small for CLIPD/SKELD magic.");

                uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4));
                if (magic == BsiMagic)
                    ParseBsi(bytes, sourceName, result);
                else if (magic == GrubMagic)
                    ParseGrub(bytes, sourceName, result);
                else
                    throw new InvalidDataException($"Unsupported CLIPD/SKELD magic 0x{magic:X8}.");

                result.FileData.StatusMessage = BuildStatus(result);
            }
            catch (Exception ex)
            {
                result.FileData.IsValid = false;
                result.FileData.StatusMessage = ex.Message;
            }

            return result;
        }

        private void ParseBsi(byte[] bytes, string sourceName, ClipdSkeldParseResult result)
        {
            var reader = new SpanReader(bytes);
            uint magic = reader.ReadUInt32();
            byte version = reader.ReadByte();
            uint schemaSize = reader.ReadUInt32();
            if (magic != BsiMagic || version != 1)
                throw new InvalidDataException("Invalid BSI CLIPD/SKELD frame header.");
            if (schemaSize > reader.Remaining)
                throw new InvalidDataException("BSI schema block extends past EOF.");

            int schemaStart = reader.Position;
            int schemaEnd = checked(schemaStart + (int)schemaSize);
            if (schemaSize > 0)
            {
                ValidateBsiSchemaBlock(bytes, schemaStart, schemaEnd, result);
                reader.Position = schemaEnd;
            }

            if (reader.Remaining < 16)
                throw new InvalidDataException("BSI frame is missing the main object and footer.");

            int mainObjectOffset = reader.Position;
            var root = ReadBsiObject(reader, bytes.Length);
            int footerOffset = reader.Position;
            if (reader.Remaining < 4)
                throw new InvalidDataException("BSI frame is missing the footer after the main object.");

            uint footerMagic = reader.ReadUInt32();
            if (footerMagic != BsiMagic)
                throw new InvalidDataException($"Invalid BSI footer magic 0x{footerMagic:X8} at 0x{footerOffset:X}.");

            int paddingSize = bytes.Length - reader.Position;
            if (paddingSize > 0)
                result.Notes.Add($"BSI trailing padding: 0x{paddingSize:X} bytes after footer.");

            result.Notes.Add($"BSI frame: schema 0x{schemaSize:X} bytes at 0x{schemaStart:X}, main object at 0x{mainObjectOffset:X}, footer at 0x{footerOffset:X}.");


            var aclObjects = ScanObjectsByHash(bytes, mainObjectOffset, footerOffset, AclAnimationDataHash);
            var skeletonObjects = ScanObjectsByHash(bytes, mainObjectOffset, footerOffset, SkeletonHash);
            result.Notes.Add($"BSI hash scan: {aclObjects.Count} ACLAnimationData @ [{string.Join(",", aclObjects.Select(o => $"0x{o.HeaderOffset:X}"))}], {skeletonObjects.Count} Skeleton.");

            // Optional naming via the ClipDictionary map keys.
            var orderedKeys = TryCollectRootMapKeys(root);

            IReadOnlyDictionary<ulong, string> boneNameLookup = GetMergedBoneNames();

            int skelIndex = 0;
            foreach (var skeletonObject in skeletonObjects)
            {
                string skelName = skelIndex < orderedKeys.Count
                    ? ResolveKeyName(orderedKeys[skelIndex], "skel")
                    : Path.GetFileNameWithoutExtension(sourceName);
                var skeleton = ConvertSkeletonObject(skeletonObject.Object, skelName);
                if (skeleton.Bones.Count > 0)
                    result.FileData.Skeletons.Add(skeleton);
                skelIndex++;
            }

            int clipIndex = 0;
            foreach (var aclObject in aclObjects)
            {
                var acl = ConvertAclObject(aclObject.Object);
                result.AclAnimations.Add(acl);

                string animName = clipIndex < orderedKeys.Count
                    ? ResolveKeyName(orderedKeys[clipIndex], "clip")
                    : $"{Path.GetFileNameWithoutExtension(sourceName)}_{clipIndex:D2}";

                int resolvedCount = acl.BoneHashes.Count(h => boneNameLookup.ContainsKey(h));
                var boneNames = acl.BoneHashes
                    .Select(h => boneNameLookup.TryGetValue(h, out var n) ? n : $"0x{h:X16}")
                    .ToList();
                string boneList = boneNames.Count > 0 ? string.Join(", ", boneNames) : "(none)";
                string aclNote = AclKeyframeDecompressionService.IsNativeAclAvailable
                    ? "native ACL loaded"
                    : "native ACL unavailable (identity fallback)";
                result.Notes.Add($"ACL clip '{animName}' @ 0x{aclObject.HeaderOffset:X}: {acl.BoneHashes.Length} bone tracks [{boneList}], {resolvedCount} resolved, {acl.NumSamples} samples, {acl.Duration:F3}s, transformBuf {acl.CompressedTransformData.Length}B, {aclNote}");

                var anim = AclKeyframeDecompressionService.ToGrannyAnimation(animName, acl, boneNameLookup);
                result.FileData.Animations.Add(anim);
                result.FileData.TrackGroups.AddRange(anim.TrackGroups);
                clipIndex++;
            }

            if (result.FileData.Skeletons.Count == 0 && sourceName.EndsWith(".skeld", StringComparison.OrdinalIgnoreCase))
                result.Warnings.Add("No BSI Skeleton object found by type-hash scan.");
            if (result.FileData.Animations.Count == 0 && sourceName.EndsWith(".clipd", StringComparison.OrdinalIgnoreCase))
            {
                AddPlaceholderClipdAnimation(sourceName, result);
                result.Warnings.Add("No BSI ACLAnimationData object found by type-hash scan.");
            }
        }

        private readonly record struct ScannedObject(int HeaderOffset, BsiObject Object);


        private static List<ScannedObject> ScanObjectsByHash(byte[] bytes, int regionStart, int regionEnd, ulong typeHash)
        {
            var found = new List<ScannedObject>();
            Span<byte> needle = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(needle, typeHash);

            // The hash lives at headerOffset+4, so the earliest header starts at regionStart.
            for (int i = regionStart + 4; i + 8 <= regionEnd; i++)
            {
                if (!bytes.AsSpan(i, 8).SequenceEqual(needle))
                    continue;

                int headerOffset = i - 4;
                if (headerOffset < regionStart)
                    continue;

                uint dataSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(headerOffset, 4));
                long bodyEnd = (long)headerOffset + 12 + dataSize;
                if (dataSize == 0 || bodyEnd > regionEnd)
                    continue;

                try
                {
                    var reader = new SpanReader(bytes) { Position = headerOffset };
                    var obj = ReadBsiObject(reader, (int)bodyEnd, parseMapEntries: false);
                    // Require the parse to land exactly on the declared body end.
                    if (reader.Position == bodyEnd && obj.TypeHash == typeHash)
                        found.Add(new ScannedObject(headerOffset, obj));
                }
                catch
                {
                    // Not a real object at this offset (schema table hit or coincidence)
                }
            }

            return found;
        }


        private static List<ulong> TryCollectRootMapKeys(BsiObject root)
        {
            foreach (var obj in EnumerateObjects(root))
            {
                if (obj.IsMap && obj.MapEntries.Count > 0)
                    return obj.MapEntries.Select(e => e.Key).ToList();
            }
            return new List<ulong>();
        }

        private string ResolveKeyName(ulong keyHash, string prefix)
        {
            // Try FNV64 bone name lookup first (skeleton bone hashes)
            if (_fnv64BoneNames.TryGetValue(keyHash, out string? mapped))
                return mapped;

            // Clip/state dictionary keys are FNV1a-32 stored as u64, check lower 32 bits
            if (keyHash <= uint.MaxValue &&
                AnimationNameTable.HashToName.TryGetValue((uint)keyHash, out string? clipName))
                return clipName;

            return $"{prefix}_0x{keyHash:X16}";
        }

        private static void ValidateBsiSchemaBlock(byte[] bytes, int schemaStart, int schemaEnd, ClipdSkeldParseResult result)
        {
            if (schemaEnd - schemaStart < 12)
            {
                result.Warnings.Add($"BSI schema block at 0x{schemaStart:X} is too small for an object header.");
                return;
            }

            uint schemaBodySize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(schemaStart, 4));
            int expectedSchemaEnd = schemaStart + 12 + checked((int)schemaBodySize);
            if (expectedSchemaEnd == schemaEnd)
            {
                ulong schemaTypeHash = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(schemaStart + 4, 8));
                result.Notes.Add($"BSI schema object: body 0x{schemaBodySize:X} bytes, type 0x{schemaTypeHash:X16}.");
                return;
            }

            result.Warnings.Add($"BSI schema block size mismatch: header object ends at 0x{expectedSchemaEnd:X}, declared schema ends at 0x{schemaEnd:X}.");
        }

        private void ParseGrub(byte[] bytes, string sourceName, ClipdSkeldParseResult result)
        {
            foreach (var blob in ReadGrubBlobs(bytes))
            {
                string tag = FourCc(blob.Tag);
                if (tag == "Skel")
                {
                    var payload = SliceBlob(bytes, blob);
                    var skeleton = ParseFlatSkeletonStream(payload, sourceName);
                    if (skeleton.Bones.Count > 0)
                        result.FileData.Skeletons.Add(skeleton);
                }
                else if (tag == "Anim")
                {
                    TryParseRawAclPayload(SliceBlob(bytes, blob), Path.GetFileNameWithoutExtension(sourceName), result);
                }
                else if (tag == "ClsL")
                {
                    TryParseNestedGrubAnimations(SliceBlob(bytes, blob), sourceName, result);
                }
            }
        }

        private void TryParseNestedGrubAnimations(byte[] payload, string sourceName, ClipdSkeldParseResult result)
        {
            if (payload.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4)) != GrubMagic)
                return;

            foreach (var blob in ReadGrubBlobs(payload))
            {
                var child = SliceBlob(payload, blob);
                if (child.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(child.AsSpan(0, 4)) != GrubMagic)
                    continue;

                foreach (var nestedBlob in ReadGrubBlobs(child))
                {
                    if (FourCc(nestedBlob.Tag) == "Anim")
                        TryParseRawAclPayload(SliceBlob(child, nestedBlob), Path.GetFileNameWithoutExtension(sourceName), result);
                }
            }
        }

        private void TryParseRawAclPayload(byte[] payload, string name, ClipdSkeldParseResult result)
        {
            try
            {
                var reader = new SpanReader(payload);
                var aclObject = ReadBsiObject(reader, payload.Length);
                if (!IsAclAnimationDataObject(aclObject)) return;
                var acl = ConvertAclObject(aclObject);
                result.AclAnimations.Add(acl);
                var anim = AclKeyframeDecompressionService.ToGrannyAnimation(name, acl, GetMergedBoneNames());
                result.FileData.Animations.Add(anim);
                result.FileData.TrackGroups.AddRange(anim.TrackGroups);
            }
            catch
            {
                result.Warnings.Add("GRUB Anim blob was not a standalone BSI ACLAnimationData payload.");
            }
        }

        private static IEnumerable<GrubBlob> ReadGrubBlobs(byte[] bytes)
        {
            var reader = new SpanReader(bytes);
            if (reader.ReadUInt32() != GrubMagic)
                yield break;

            byte major = reader.ReadByte();
            byte minor = reader.ReadByte();
            uint blobCount;
            int headerSize;

            if (major == 1 && minor == 1)
            {
                reader.Skip(2);
                headerSize = (int)reader.ReadUInt32();
                reader.Skip(4);
                blobCount = reader.ReadUInt32();
            }
            else
            {
                blobCount = reader.ReadUInt16();
                reader.Skip(8);
                headerSize = 16;
            }

            reader.Position = headerSize;
            for (int i = 0; i < blobCount && reader.Remaining >= 24; i++)
            {
                yield return new GrubBlob(
                    reader.ReadUInt32(),
                    reader.ReadByte(),
                    reader.ReadByte(),
                    reader.ReadUInt16(),
                    reader.ReadUInt32(),
                    reader.ReadUInt32(),
                    reader.ReadUInt32(),
                    reader.ReadUInt32());
            }
        }

        private static BsiObject ReadBsiObject(SpanReader reader, int maxEnd, bool parseMapEntries = true)
        {
            if (reader.Position + 12 > maxEnd)
                throw new InvalidDataException("Truncated BSI object header.");

            uint dataSize = reader.ReadUInt32();
            ulong typeHash = reader.ReadUInt64();
            int bodyStart = reader.Position;
            int bodyEnd = checked(bodyStart + (int)dataSize);
            if (bodyEnd > maxEnd || bodyEnd > reader.Length)
                throw new InvalidDataException("BSI object body extends past expected boundary.");

            var obj = new BsiObject(typeHash, dataSize);
            while (reader.Position < bodyEnd)
            {
                int fieldStart = reader.Position;
                uint fieldSize = reader.ReadUInt32();
                int payloadStart = reader.Position;
                int payloadEnd = checked(payloadStart + (int)fieldSize);
                if (payloadEnd > bodyEnd)
                    throw new InvalidDataException("BSI field extends past object body.");

                byte[] payload = reader.ReadBytes((int)fieldSize);
                var field = new BsiField(fieldStart, payloadStart, payload);

                // Detect nested PropertyBag
                if (fieldSize >= 12)
                {
                    uint nestedSize = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4));
                    if ((long)nestedSize + 12 == fieldSize && nestedSize > 0)
                    {
                        var nestedReader = new SpanReader(payload);
                        try { field.NestedObject = ReadBsiObject(nestedReader, payload.Length, parseMapEntries); }
                        catch { }
                    }
                }

                obj.Fields.Add(field);
            }

            // After all fields are read, try to interpret this object as a map.
            if (parseMapEntries)
                TryParseAsMap(obj);

            return obj;
        }

        private static IEnumerable<BsiObject> EnumerateObjects(BsiObject root)
        {
            yield return root;


            foreach (var child in root.Children)
            {
                foreach (var descendant in EnumerateObjects(child))
                    yield return descendant;
            }

            foreach (var field in root.Fields)
            {
                if (field.NestedObject != null)
                {
                    foreach (var descendant in EnumerateObjects(field.NestedObject))
                        yield return descendant;
                }
            }
        }

        // Map / unordered_map entry parsing 

        private static void TryParseAsMap(BsiObject obj)
        {
            // A BSI map (unordered_map) has:
            //   Field 0: metadata (4 or 8 bytes depending on serialization)
            //   Field 1: packed entries blob  {key, value_PropertyBag}*
            if (obj.Fields.Count < 2) return;

            byte[] entriesPayload = obj.Fields[1].Payload;

            // Try Field 0 as a 4-byte entry count (common case)
            uint entryCount = 0;
            if (obj.Fields[0].Payload.Length == 4)
            {
                entryCount = BinaryPrimitives.ReadUInt32LittleEndian(obj.Fields[0].Payload);
            }
            // Try Field 0 as 8 bytes (hash-table metadata, count in Field 1 header)
            else if (obj.Fields[0].Payload.Length == 8 && entriesPayload.Length >= 8)
            {
                // Field 1 starts with [u32 count][u32 key_stride]
                entryCount = BinaryPrimitives.ReadUInt32LittleEndian(entriesPayload.AsSpan(0, 4));
                uint keyStride = BinaryPrimitives.ReadUInt32LittleEndian(entriesPayload.AsSpan(4, 4));
                if (keyStride == 4 || keyStride == 8)
                {
                    // Strip the 8-byte header before parsing entries
                    entriesPayload = entriesPayload.AsSpan(8).ToArray();
                }
                else
                {
                    return; // Unknown format
                }
            }
            else
            {
                return; // Unknown Field 0 size
            }

            if (entryCount == 0 || entryCount > 500_000) return;

            // Try 8-byte keys first (UInt64 Identifier, common for FNV-64 hashes)
            if (TryParseMapEntries(entriesPayload, entryCount, 8, out var entries))
            {
                obj.IsMap = true;
                obj.MapEntryCount = (int)entryCount;
                foreach (var (key, childObj) in entries)
                {
                    obj.MapEntries.Add((key, childObj));
                    obj.Children.Add(childObj);
                }
                return;
            }

            // Fallback: try 4-byte keys (U32_Obfuscated Identifier)
            if (TryParseMapEntries(entriesPayload, entryCount, 4, out entries))
            {
                obj.IsMap = true;
                obj.MapEntryCount = (int)entryCount;
                foreach (var (key, childObj) in entries)
                {
                    obj.MapEntries.Add((key, childObj));
                    obj.Children.Add(childObj);
                }
            }
        }

        private static bool TryParseMapEntries(byte[] payload, uint expectedCount, int keySize,
            out List<(ulong Key, BsiObject Value)> entries)
        {
            entries = new();
            int offset = 0;

            for (int i = 0; i < expectedCount; i++)
            {
                if (offset + keySize + 12 > payload.Length)
                {
                    entries.Clear();
                    return false;
                }

                // Read key
                ulong key = keySize == 8
                    ? BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(offset, 8))
                    : BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset, 4));
                offset += keySize;

                // Read value PropertyBag header (12 bytes: dataSize + typeHash)
                uint valueDataSize = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset, 4));
                if (valueDataSize == 0 || valueDataSize > 50_000_000) { entries.Clear(); return false; }

                int valueTotalSize = 12 + (int)valueDataSize;
                if (offset + valueTotalSize > payload.Length) { entries.Clear(); return false; }

                // Parse the value as a BsiObject
                try
                {
                    var valueReader = new SpanReader(payload) { Position = offset };
                    var valueObj = ReadBsiObject(valueReader, offset + valueTotalSize, parseMapEntries: true);
                    entries.Add((key, valueObj));
                }
                catch
                {
                    entries.Clear();
                    return false;
                }

                offset += valueTotalSize;
            }

            // Must have consumed exactly the entries payload
            return offset == payload.Length;
        }

        private static bool IsAclAnimationDataObject(BsiObject obj)
        {


            int byteVectorCount = 0;
            int uint32VectorCount = 0;
            bool hasFloat32Field = false;

            foreach (var field in obj.Fields)
            {
                if (LooksLikeVectorBytes(field.Payload))
                    byteVectorCount++;
                else if (LooksLikeVectorUInt32(field.Payload))
                    uint32VectorCount++;
                else if (field.Payload.Length == 4)
                {
                    // Could be uint32 or float32
                    hasFloat32Field = true;
                }
            }

            // Must have ACL compressed data and bone hashes at minimum
            return byteVectorCount >= 2 && uint32VectorCount >= 1 && hasFloat32Field;
        }

        private static bool IsSkeletonObject(BsiObject obj)
        {
            return obj.Fields.Count >= 3
                && LooksLikeVectorUInt64OrUInt32(obj.Fields[0].Payload)
                && LooksLikeVectorInt16(obj.Fields[1].Payload)
                && LooksLikeVectorMatricesOrTransforms(obj.Fields[2].Payload);
        }

        private static AclCompressedAnimation ConvertAclObject(BsiObject obj)
        {

            byte[] FieldBytes(int i) => i < obj.Fields.Count ? ReadByteVector(obj.Fields[i].Payload) : Array.Empty<byte>();
            ulong[] FieldUInt64Identifiers(int i) => i < obj.Fields.Count ? ReadIdentifierVector(obj.Fields[i].Payload) : Array.Empty<ulong>();
            uint FieldU32Scalar(int i) => i < obj.Fields.Count && obj.Fields[i].Payload.Length >= 4
                ? BinaryPrimitives.ReadUInt32LittleEndian(obj.Fields[i].Payload.AsSpan(0, 4)) : 0u;
            float FieldF32Scalar(int i) => i < obj.Fields.Count && obj.Fields[i].Payload.Length >= 4
                ? BitConverter.ToSingle(obj.Fields[i].Payload, 0) : 0f;
            byte FieldU8(int i) => i < obj.Fields.Count && obj.Fields[i].Payload.Length >= 1 ? obj.Fields[i].Payload[0] : (byte)0;

            return new AclCompressedAnimation
            {
                CompressedTransformData = FieldBytes(0),
                CompressedRootTransformData = FieldBytes(1),
                CompressedFloatChannelData = FieldBytes(2),
                BoneHashes = FieldUInt64Identifiers(6),
                NumSamples = FieldU32Scalar(9),
                Duration = FieldF32Scalar(10),
                AnimType = FieldU8(11),
                VersionNumber = FieldU8(12)
            };
        }

        private GrannySkeleton ConvertSkeletonObject(BsiObject obj, string sourceName)
        {
            ulong[] boneIds = ReadIdentifierVector(obj.Fields[0].Payload);
            short[] parents = ReadInt16Vector(obj.Fields[1].Payload);
            Matrix4x4[] bindPose = ReadTransformVector(obj.Fields[2].Payload);
            int boneCount = Math.Min(boneIds.Length, Math.Min(parents.Length, bindPose.Length));

            var skeleton = new GrannySkeleton
            {
                Name = Path.GetFileNameWithoutExtension(sourceName),
                LODType = 0
            };

            var world = new Matrix4x4[boneCount];
            for (int i = 0; i < boneCount; i++)
            {
                string name = _fnv64BoneNames.TryGetValue(boneIds[i], out string? mapped)
                    ? mapped
                    : AnimationNameTable.ResolveBone(boneIds[i])
                      ?? $"bone_{boneIds[i]:X16}";
                Matrix4x4 local = bindPose[i];
                world[i] = parents[i] >= 0 && parents[i] < i ? local * world[parents[i]] : local;
                Matrix4x4.Invert(world[i], out var inverseWorld);

                skeleton.Bones.Add(new GrannyBone
                {
                    Name = name,
                    ParentIndex = parents[i],
                    LocalTransform = MatrixToTransform(local),
                    WorldTransform = world[i],
                    InverseWorld4x4 = inverseWorld,
                    BoneHash = boneIds[i]
                });
            }

            return skeleton;
        }

        private static void AddPlaceholderClipdAnimation(string sourceName, ClipdSkeldParseResult result)
        {
            const float duration = 1f;
            string baseName = Path.GetFileNameWithoutExtension(sourceName);
            var animation = new GrannyAnimation
            {
                Name = string.IsNullOrWhiteSpace(baseName) ? "clipd_placeholder" : baseName,
                Duration = duration,
                TimeStep = duration,
                Oversampling = 1f,
                DefaultLoopCount = 0,
                Flags = 0
            };

            var trackGroup = new GrannyTrackGroup
            {
                Name = animation.Name,
                Flags = 0,
                LoopTranslation = Vector3.Zero
            };

            var track = new GrannyTransformTrack
            {
                Name = "root",
                Flags = 0,
                PositionCurve = BuildPlaceholderCurve(3, new[] { 0f, 0f, 0f, 0f, 0f, 0f }),
                OrientationCurve = BuildPlaceholderCurve(4, new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f }),
                ScaleShearCurve = BuildPlaceholderCurve(3, new[] { 1f, 1f, 1f, 1f, 1f, 1f })
            };
            track.Keyframes.Add(new TransformKeyframe { Time = 0f, Position = Vector3.Zero, Orientation = Quaternion.Identity, Scale = Vector3.One });
            track.Keyframes.Add(new TransformKeyframe { Time = duration, Position = Vector3.Zero, Orientation = Quaternion.Identity, Scale = Vector3.One });

            trackGroup.TransformTracks.Add(track);
            animation.TrackGroups.Add(trackGroup);
            result.FileData.Animations.Add(animation);
            result.FileData.TrackGroups.Add(trackGroup);
            result.Warnings.Add("CLIPD loaded as a placeholder animation until ACLAnimationData is resolved from the BSI payload.");
        }

        private static GrannyCurveInfo BuildPlaceholderCurve(int dimension, float[] controls)
        {
            return new GrannyCurveInfo
            {
                FormatName = "CLIPD.Placeholder",
                IsIdentity = true,
                IsConstant = true,
                Knots = new[] { 0f, 1f },
                Controls = controls,
                Dimension = dimension,
                Degree = 1
            };
        }

        private GrannySkeleton ParseFlatSkeletonStream(byte[] payload, string sourceName)
        {
            var reader = new SpanReader(payload);
            ushort boneCount = reader.ReadUInt16();
            if (boneCount > 4096)
                throw new InvalidDataException("Flat skeleton stream has an unreasonable bone count.");

            var skeleton = new GrannySkeleton { Name = Path.GetFileNameWithoutExtension(sourceName) };
            var world = new Matrix4x4[boneCount];
            for (int i = 0; i < boneCount; i++)
            {
                string name = reader.ReadLengthPrefixedString();
                short parent = reader.ReadInt16();
                reader.Skip(4);
                Matrix4x4 local = reader.ReadMatrix4x4();
                world[i] = parent >= 0 && parent < i ? local * world[parent] : local;
                Matrix4x4.Invert(world[i], out var inverseWorld);
                skeleton.Bones.Add(new GrannyBone
                {
                    Name = name,
                    ParentIndex = parent,
                    LocalTransform = MatrixToTransform(local),
                    WorldTransform = world[i],
                    InverseWorld4x4 = inverseWorld
                });
            }

            return skeleton;
        }

        private static GrannyTransform MatrixToTransform(Matrix4x4 matrix)
        {
            Vector3 translation = new(matrix.M41, matrix.M42, matrix.M43);
            Vector3 scale = new(
                new Vector3(matrix.M11, matrix.M12, matrix.M13).Length(),
                new Vector3(matrix.M21, matrix.M22, matrix.M23).Length(),
                new Vector3(matrix.M31, matrix.M32, matrix.M33).Length());

            var rotationMatrix = matrix;
            if (scale.X > 1e-6f) { rotationMatrix.M11 /= scale.X; rotationMatrix.M12 /= scale.X; rotationMatrix.M13 /= scale.X; }
            if (scale.Y > 1e-6f) { rotationMatrix.M21 /= scale.Y; rotationMatrix.M22 /= scale.Y; rotationMatrix.M23 /= scale.Y; }
            if (scale.Z > 1e-6f) { rotationMatrix.M31 /= scale.Z; rotationMatrix.M32 /= scale.Z; rotationMatrix.M33 /= scale.Z; }
            rotationMatrix.M41 = rotationMatrix.M42 = rotationMatrix.M43 = 0f;
            rotationMatrix.M44 = 1f;

            return new GrannyTransform
            {
                Flags = 0x7,
                Position = translation,
                Orientation = Quaternion.CreateFromRotationMatrix(rotationMatrix),
                ScaleShear0 = new Vector3(matrix.M11, matrix.M12, matrix.M13),
                ScaleShear1 = new Vector3(matrix.M21, matrix.M22, matrix.M23),
                ScaleShear2 = new Vector3(matrix.M31, matrix.M32, matrix.M33)
            };
        }

        private static bool LooksLikeVectorBytes(byte[] payload) => TryReadVectorCountAny(payload, 1, out _, out _);
        private static bool LooksLikeVectorUInt32(byte[] payload) => TryReadVectorCountAny(payload, 4, out _, out _);
        private static bool LooksLikeVectorInt16(byte[] payload) => TryReadVectorCountAny(payload, 2, out _, out _);
        private static bool LooksLikeVectorUInt64OrUInt32(byte[] payload) => LooksLikeVectorUInt32(payload) || TryReadVectorCountAny(payload, 8, out _, out _);
        private static bool LooksLikeVectorMatricesOrTransforms(byte[] payload) => TryReadVectorCountAny(payload, 64, out _, out _) || TryReadVectorCountAny(payload, 40, out _, out _);


        private static bool TryReadVectorCount(byte[] payload, int stride, out int count, out int headerSize)
        {
            count = 0;
            headerSize = 0;
            if (payload.Length < 4) return false;

            uint rawCount = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4));
            if (rawCount == 0 || rawCount > 1_000_000) return false;

            if (payload.Length >= 8)
            {
                uint capacity = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(4, 4));
                long expectedA = 8L + rawCount * stride;
                if (rawCount <= capacity && expectedA == payload.Length)
                {
                    count = (int)rawCount;
                    headerSize = 8;
                    return true;
                }
            }

            long expectedB = 4L + rawCount * stride;
            if (expectedB == payload.Length)
            {
                count = (int)rawCount;
                headerSize = 4;
                return true;
            }


            return false;
        }


        private static bool TryReadVectorCountAny(byte[] payload, int stride, out int count, out int headerSize)
            => TryReadVectorCount(payload, stride, out count, out headerSize);

        private static byte[] ReadByteVector(byte[] payload)
        {
            if (!TryReadVectorCount(payload, 1, out int count, out int headerSize)) return Array.Empty<byte>();
            var result = new byte[count];
            Buffer.BlockCopy(payload, headerSize, result, 0, count);
            return result;
        }

        private static uint[] ReadUInt32Vector(byte[] payload)
        {
            if (!TryReadVectorCount(payload, 4, out int count, out int headerSize)) return Array.Empty<uint>();
            var result = new uint[count];
            for (int i = 0; i < count; i++)
                result[i] = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(headerSize + i * 4, 4));
            return result;
        }

        private static ulong[] ReadIdentifierVector(byte[] payload)
        {
            if (TryReadVectorCount(payload, 8, out int count64, out int headerSize))
            {
                var result = new ulong[count64];
                for (int i = 0; i < count64; i++)
                    result[i] = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(headerSize + i * 8, 8));
                return result;
            }

            var ids32 = ReadUInt32Vector(payload);
            return ids32.Select(id => (ulong)id).ToArray();
        }

        private static short[] ReadInt16Vector(byte[] payload)
        {
            if (!TryReadVectorCount(payload, 2, out int count, out int headerSize)) return Array.Empty<short>();
            var result = new short[count];
            for (int i = 0; i < count; i++)
                result[i] = BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(headerSize + i * 2, 2));
            return result;
        }

        private static Matrix4x4[] ReadTransformVector(byte[] payload)
        {
            if (TryReadVectorCount(payload, 64, out int matrixCount, out int headerSize))
            {
                var result = new Matrix4x4[matrixCount];
                var reader = new SpanReader(payload) { Position = headerSize };
                for (int i = 0; i < matrixCount; i++)
                    result[i] = reader.ReadMatrix4x4();
                return result;
            }

            if (TryReadVectorCount(payload, 40, out int trsCount, out headerSize))
            {
                var transforms = new Matrix4x4[trsCount];
                var trsReader = new SpanReader(payload) { Position = headerSize };
                for (int i = 0; i < trsCount; i++)
                {
                    Vector3 translation = trsReader.ReadVector3();
                    Quaternion rotation = trsReader.ReadQuaternion();
                    Vector3 scale = trsReader.ReadVector3();
                    transforms[i] = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
                }
                return transforms;
            }

            return Array.Empty<Matrix4x4>();
        }

        private static byte[] SliceBlob(byte[] bytes, GrubBlob blob)
        {
            if (blob.CompressedSize != blob.UncompressedSize)
                throw new NotSupportedException("Compressed GRUB animation blobs require decompression outside the BSI-only backend path.");
            if (blob.DataOffset + blob.UncompressedSize > bytes.Length)
                throw new InvalidDataException("GRUB blob extends past EOF.");
            var payload = new byte[blob.UncompressedSize];
            Buffer.BlockCopy(bytes, (int)blob.DataOffset, payload, 0, payload.Length);
            return payload;
        }

        private static string FourCc(uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            return Encoding.ASCII.GetString(bytes);
        }

        private static string BuildStatus(ClipdSkeldParseResult result)
        {
            var parts = new List<string> { "CLIPD/SKELD" };
            if (result.FileData.Skeletons.Count > 0) parts.Add($"{result.FileData.Skeletons.Count} skel");
            if (result.FileData.Animations.Count > 0) parts.Add($"{result.FileData.Animations.Count} anim");
            if (!AclKeyframeDecompressionService.IsNativeAclAvailable && result.AclAnimations.Count > 0)
                parts.Add("ACL native shim missing: identity preview tracks only");
            parts.AddRange(result.Warnings);
            return string.Join("; ", parts);
        }

        private void LoadBuiltInBoneNames()
        {
            AddBone(0x08BC1ACB, 0x426B43668313948B, "<root>");
            AddBone(0x20FD0E45, 0xA354FD1FF0C467C5, "root");
        }

        private void LoadExternalBoneLookup()
        {
            try
            {
                // Try to load bone hash lookup CSV from Materials directory
                string csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Materials", "bone_hash_lookup.csv");
                if (!File.Exists(csvPath))
                {
                    // Also try relative to the application base for development
                    csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "bone_hash_lookup.csv");
                    if (!File.Exists(csvPath))
                        return;
                }

                foreach (string line in File.ReadLines(csvPath))
                {
                    // Skip comments and empty lines
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                        continue;

                    // Format: 0xHHHHHHHH,0xHHHHHHHHHHHHHHHH,bone_name
                    string[] parts = trimmed.Split(',');
                    if (parts.Length < 3)
                        continue;

                    string fnv32Str = parts[0].Trim();
                    string fnv64Str = parts[1].Trim();
                    string boneName = parts[2].Trim();

                    if (fnv32Str.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                        fnv64Str.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                        uint.TryParse(fnv32Str[2..], System.Globalization.NumberStyles.HexNumber, null, out uint fnv32) &&
                        ulong.TryParse(fnv64Str[2..], System.Globalization.NumberStyles.HexNumber, null, out ulong fnv64))
                    {
                        AddBone(fnv32, fnv64, boneName);
                    }
                }
            }
            catch
            {
                // Silently ignore errors loading external bone data
            }
        }



        private void AddBone(uint fnv32, ulong fnv64, string name)
        {
            _fnv32BoneNames[fnv32] = name;
            _fnv64BoneNames[fnv64] = name;
        }

        private sealed record BsiObject(ulong TypeHash, uint DataSize)
        {
            public List<BsiField> Fields { get; } = new();
            public List<BsiObject> Children { get; } = new();
            public bool IsMap { get; set; }
            public int MapEntryCount { get; set; }
            public List<(ulong Key, BsiObject Value)> MapEntries { get; } = new();
        }

        private sealed record BsiField(int FieldOffset, int PayloadOffset, byte[] Payload)
        {
            public BsiObject? NestedObject { get; set; }
        }

        private readonly record struct GrubBlob(uint Tag, byte Major, byte Minor, ushort MetadataCount, uint MetadataOffset, uint DataOffset, uint CompressedSize, uint UncompressedSize);

        private sealed class SpanReader
        {
            private readonly byte[] _data;

            public SpanReader(byte[] data) => _data = data;
            public int Position { get; set; }
            public int Length => _data.Length;
            public int Remaining => _data.Length - Position;

            public byte ReadByte()
            {
                Ensure(1);
                return _data[Position++];
            }

            public ushort ReadUInt16()
            {
                Ensure(2);
                ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(Position, 2));
                Position += 2;
                return value;
            }

            public short ReadInt16()
            {
                Ensure(2);
                short value = BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(Position, 2));
                Position += 2;
                return value;
            }

            public uint ReadUInt32()
            {
                Ensure(4);
                uint value = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(Position, 4));
                Position += 4;
                return value;
            }

            public ulong ReadUInt64()
            {
                Ensure(8);
                ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(Position, 8));
                Position += 8;
                return value;
            }

            public float ReadSingle()
            {
                Ensure(4);
                float value = BitConverter.ToSingle(_data, Position);
                Position += 4;
                return value;
            }

            public Vector3 ReadVector3() => new(ReadSingle(), ReadSingle(), ReadSingle());

            public Quaternion ReadQuaternion()
            {
                var rotation = new Quaternion(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
                return rotation.LengthSquared() > 1e-12f ? Quaternion.Normalize(rotation) : Quaternion.Identity;
            }

            public Matrix4x4 ReadMatrix4x4()
            {
                return new Matrix4x4(
                    ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
                    ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
                    ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
                    ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
            }

            public string ReadLengthPrefixedString()
            {
                uint length = ReadUInt32();
                if (length > Remaining) throw new InvalidDataException("String length extends past EOF.");
                string value = Encoding.UTF8.GetString(_data, Position, (int)length).TrimEnd('\0');
                Position += (int)length;
                return value;
            }

            public byte[] ReadBytes(int count)
            {
                Ensure(count);
                var result = new byte[count];
                Buffer.BlockCopy(_data, Position, result, 0, count);
                Position += count;
                return result;
            }

            public void Skip(int count)
            {
                Ensure(count);
                Position += count;
            }

            private void Ensure(int count)
            {
                if (count < 0 || Position + count > _data.Length)
                    throw new EndOfStreamException("Unexpected end of CLIPD/SKELD stream.");
            }
        }
    }
}
