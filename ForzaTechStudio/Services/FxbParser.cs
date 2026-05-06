using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ForzaTechStudio.ViewModels;

namespace ForzaTechStudio.Services
{
    // Stream-based parser for FxStudio Particle Bank (.fxb) files.
    public sealed class FxbParser : IDisposable
    {
        private readonly Stream _stream;
        private readonly BinaryReader _reader;
        private bool _disposed;

        public FxbParser(Stream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        }

        // Public entry-point

        public ViewModels.FxbBankNode Parse()
        {
            if (_stream.Length < FxbBankHeaderSize)
                throw new InvalidDataException($"File too small ({_stream.Length} bytes); minimum is {FxbBankHeaderSize}.");

            _stream.Seek(0, SeekOrigin.Begin);
            var hdr = ReadBankHeader();
            ValidateHeader(hdr, _stream.Length);

            var bank = new ViewModels.FxbBankNode
            {
                BankName      = GetString(hdr.m_BankName, hdr),
                Version       = hdr.m_Version,
                BankSize      = hdr.m_BankSize,
                NumberEffects = hdr.m_NumberEffects,
            };

            ReadEffects(bank, hdr);
            return bank;
        }

        // Header

        private const int FxbBankHeaderSize = 96; // 0x60

        private ViewModels.FxbBankHeader ReadBankHeader()
        {
            Seek(0);
            return new ViewModels.FxbBankHeader(
                m_MagicId:                    ReadU32(),
                m_Version:                    ReadU32(),
                m_PlatformId:                 ReadU32(),
                m_BankSize:                   ReadU32(),
                m_BankName:                   ReadU32(),
                m_StringTableOffset:          ReadU32(),
                m_Vector3TableOffset:         ReadU32(),
                m_Vector4TableOffset:         ReadU32(),
                m_FloatRangeTableOffset:      ReadU32(),
                m_IntegerRangeTableOffset:    ReadU32(),
                m_FixedFunctionOffset:        ReadU32(),
                m_ColorARGBChannelDataOffset: ReadU32(),
                m_FloatChannelDataOffset:     ReadU32(),
                m_ChannelTableOffset:         ReadU32(),
                m_LODTableOffset:             ReadU32(),
                m_NumberComponentDefs:        ReadU32(),
                m_ComponetDefOffset:          ReadU32(),
                m_NumInputs:                  ReadU32(),
                m_InputDefinitionOffset:      ReadU32(),
                m_LODCategories:              ReadU32(),
                m_EffectNameTable:            ReadU32(),
                m_EffectIdTable:              ReadU32(),
                m_NumberEffects:              ReadU32(),
                m_EffectOffset:               ReadU32()
            );
        }

        private static void ValidateHeader(ViewModels.FxbBankHeader hdr, long streamLen)
        {
            if (hdr.m_MagicId != ViewModels.FxbBankHeader.MagicValue)
                throw new InvalidDataException(
                    $"Invalid magic 0x{hdr.m_MagicId:X8}; expected 0x{ViewModels.FxbBankHeader.MagicValue:X8} ('FxBk').");

            if (hdr.m_Version != ViewModels.FxbBankHeader.VersionValue)
                throw new InvalidDataException(
                    $"Unsupported version {hdr.m_Version}; expected {ViewModels.FxbBankHeader.VersionValue}.");

            if (hdr.m_BankSize > streamLen)
                throw new InvalidDataException(
                    $"BankSize {hdr.m_BankSize} exceeds stream length {streamLen}.");


            // m_EffectIdTable  == m_EffectNameTable + m_NumberEffects * 8
            // m_EffectOffset   == m_EffectIdTable   + m_NumberEffects * 8
            long expectedIdTable  = (long)hdr.m_EffectNameTable + (long)hdr.m_NumberEffects * 8;
            long expectedEffStart = expectedIdTable              + (long)hdr.m_NumberEffects * 8;

            if (hdr.m_EffectIdTable != expectedIdTable)
                throw new InvalidDataException(
                    $"EffectIdTable offset 0x{hdr.m_EffectIdTable:X} does not satisfy integrity constraint " +
                    $"(expected 0x{expectedIdTable:X}).");

            if (hdr.m_EffectOffset != expectedEffStart)
                throw new InvalidDataException(
                    $"EffectOffset 0x{hdr.m_EffectOffset:X} does not satisfy integrity constraint " +
                    $"(expected 0x{expectedEffStart:X}).");
        }

        // Effects

        private void ReadEffects(ViewModels.FxbBankNode bank, ViewModels.FxbBankHeader hdr)
        {
            if (hdr.m_NumberEffects == 0) return;

            // Read EffectNameTableEntry[] (sorted by name for binary search in engine)
            Seek(hdr.m_EffectNameTable);
            var nameTable = new ViewModels.FxbEffectNameTableEntry[hdr.m_NumberEffects];
            for (int i = 0; i < nameTable.Length; i++)
            {
                nameTable[i] = new ViewModels.FxbEffectNameTableEntry(ReadU32(), ReadU32());
            }

            // Parse each unique effect header (follow name table to preserve display order)
            var visitedOffsets = new HashSet<uint>();
            foreach (var entry in nameTable)
            {
                if (!visitedOffsets.Add(entry.m_EffectOffset)) continue;

                long hdrOffset = entry.m_EffectOffset;
                Seek(hdrOffset);
                var eh = ReadEffectHeader();

                var effectNode = new ViewModels.FxbEffectNode
                {
                    Name                = GetString(eh.m_NameId, hdr),
                    NId                 = eh.m_nId,
                    FDuration           = eh.m_fDuration,
                    LodCategory         = eh.m_nLODCategory,
                    EffectHeaderOffset  = hdrOffset,
                    DurationFieldOffset = hdrOffset + 8, // m_fDuration at +0x08
                };

                ReadPhases(effectNode, eh, hdr);
                ReadComponents(effectNode, eh, hdr);
                bank.Effects.Add(effectNode);
            }
        }

        private ViewModels.FxbEffectHeader ReadEffectHeader() =>
            new(ReadU32(), ReadU32(), ReadF32(), ReadU32(),
                ReadU32(), ReadU32(), ReadU32(),
                ReadU32(), ReadU32(), ReadU32(), ReadU32());

        // Phases

        private void ReadPhases(ViewModels.FxbEffectNode effectNode,
                                 ViewModels.FxbEffectHeader eh,
                                 ViewModels.FxbBankHeader hdr)
        {
            if (eh.m_nNumPhases == 0) return;

            Seek(eh.m_nPhaseOffset);
            for (uint i = 0; i < eh.m_nNumPhases; i++)
            {
                long phaseBase = _stream.Position;
                var nameId    = ReadU32();
                var duration  = ReadF32();
                var playCount = ReadI32();

                effectNode.Phases.Add(new ViewModels.FxbPhaseNode
                {
                    Name                 = GetString(nameId, hdr),
                    FDuration            = duration,
                    NPlayCount           = playCount,
                    DurationFieldOffset  = phaseBase + 4,
                    PlayCountFieldOffset = phaseBase + 8,
                });
            }
        }

        // Components  (variable-stride: 40 + 8*nProp + 8*nInput + 12*nDynamic)

        private void ReadComponents(ViewModels.FxbEffectNode effectNode,
                                     ViewModels.FxbEffectHeader eh,
                                     ViewModels.FxbBankHeader hdr)
        {
            if (eh.m_nNumComponents == 0) return;

            long pos = eh.m_nComponentOffset;
            for (uint c = 0; c < eh.m_nNumComponents; c++)
            {
                Seek(pos);
                long compBase = pos;

                var defOffset   = ReadU32();
                var startTime   = ReadF32();
                var endTime     = ReadF32();
                var trackGroup  = ReadU32();
                var nPropValues = ReadU32();
                var propOffset  = ReadU32();
                var nInputs     = ReadU32();
                var inputOffset = ReadU32();
                var nDynamic    = ReadU32();
                var dynOffset   = ReadU32();

                uint stride = 40u + 8u * nPropValues + 8u * nInputs + 12u * nDynamic;


                long effectivePropOffset = (propOffset != 0)
                    ? (long)propOffset
                    : compBase + 40;

                // Resolve type name from ComponentDefinitionHeader
                string typeName = ResolveComponentTypeName(defOffset, hdr);

                var compNode = new ViewModels.FxbComponentNode
                {
                    TypeName             = typeName,
                    FStartTime           = startTime,
                    FEndTime             = endTime,
                    TrackGroup           = trackGroup,
                    NumPropertyValues    = nPropValues,
                    NumInputValues       = nInputs,
                    NumDynamicValues     = nDynamic,
                    StartTimeFieldOffset = compBase + 4,
                    EndTimeFieldOffset   = compBase + 8,
                };

                ReadPropertyValues(compNode, nPropValues, effectivePropOffset, defOffset, hdr);

                effectNode.Components.Add(compNode);
                pos += stride;
            }
        }

        private string ResolveComponentTypeName(uint defOffset, ViewModels.FxbBankHeader hdr)
        {
            if (defOffset == 0) return "";
            try
            {
                Seek(defOffset);
                uint typeNameId = ReadU32();
                return GetString(typeNameId, hdr);
            }
            catch
            {
                return $"[def@0x{defOffset:X}]";
            }
        }



        private void ReadPropertyValues(ViewModels.FxbComponentNode compNode,
                                         uint nPropValues,
                                         long propArrayOffset,
                                         uint compDefOffset,
                                         ViewModels.FxbBankHeader hdr)
        {
            if (nPropValues == 0) return;

            Seek(propArrayOffset);
            for (uint i = 0; i < nPropValues; i++)
            {
                // ComponentProperty entry: {m_PropertyIndex u32, m_Data u32}
                long entryBase     = _stream.Position;        
                uint propDefOffset = ReadU32();          
                uint dataOffset    = ReadU32();               

                string          propName = "";
                FxbPropertyType typeId   = FxbPropertyType.Integer;


                if (propDefOffset >= FxbBankHeaderSize && propDefOffset + 16 <= hdr.m_BankSize)
                {
                    long savedPos = _stream.Position;
                    try
                    {
                        Seek(propDefOffset);
                        uint nameId  = ReadU32();  
                        uint typeVal = ReadU32(); 
                        propName = GetString(nameId, hdr);
                        typeId   = (FxbPropertyType)(typeVal & 0x7FFFFFFFu);
                    }
                    catch { }
                    Seek(savedPos);
                }

                var propNode = new ViewModels.FxbPropertyValueNode
                {
                    Name       = propName,
                    TypeId     = typeId,
                    DataOffset = dataOffset,
                };

                ResolvePropertyData(propNode, dataOffset, typeId, hdr, entryBase);
                compNode.PropertyValues.Add(propNode);
            }
        }

        private void ResolvePropertyData(ViewModels.FxbPropertyValueNode node,
                                          uint dataOffset,
                                          FxbPropertyType typeId,
                                          ViewModels.FxbBankHeader hdr,
                                          long entryBase = 0)
        {
            if (dataOffset == 0 && typeId != FxbPropertyType.Integer && typeId != FxbPropertyType.Float) return;

            try
            {
                switch (typeId)
                {
                    case FxbPropertyType.FloatKeyFrame:
                        ReadFloatKeyframeData(node, dataOffset);
                        break;

                    case FxbPropertyType.ColorARGBKeyFrame:
                        ReadColorKeyframeData(node, dataOffset);
                        break;

                    case FxbPropertyType.FloatRange:
                        ReadFloatRange(node, dataOffset);
                        break;

                    case FxbPropertyType.IntegerRange:
                        ReadIntegerRange(node, dataOffset);
                        break;

                    case FxbPropertyType.Integer:

                        node.IntValue            = (int)dataOffset;
                        node.IntValueFieldOffset = entryBase + 4;
                        node.Summary             = $"{(int)dataOffset}";
                        break;

                    case FxbPropertyType.Float:
                        // Data stores an IEEE 754 float reinterpreted as uint32.
                        float fVal = BitConverter.Int32BitsToSingle((int)dataOffset);
                        node.FloatValue           = fVal;
                        node.FloatValueFieldOffset = entryBase + 4;
                        node.Summary              = $"{fVal:G6}";
                        break;

                    case FxbPropertyType.String:
                        // Data is file offset to null-terminated string.
                        string sVal = GetString(dataOffset, hdr);
                        if (string.IsNullOrEmpty(sVal) && dataOffset > 0)
                            sVal = ReadNullTerminatedString(dataOffset);
                        node.StringValue = sVal;
                        node.Summary     = string.IsNullOrEmpty(sVal) ? $"@0x{dataOffset:X}" : sVal;
                        break;

                    case FxbPropertyType.Vector3:
                    case FxbPropertyType.Vector3Range:
                        ReadVector3(node, dataOffset);
                        break;

                    case FxbPropertyType.Vector4:
                        ReadVector4(node, dataOffset);
                        break;

                    default:
                        node.Summary = $"[{typeId}] @0x{dataOffset:X}";
                        break;
                }
            }
            catch
            {
                node.Summary = $"[read error @0x{dataOffset:X}]";
            }
        }

        // Float keyframe

        private void ReadFloatKeyframeData(ViewModels.FxbPropertyValueNode node, uint offset)
        {
            Seek(offset);
            uint typeVal   = ReadU32();
            int  numFrames = ReadI32();

            if (numFrames <= 0 || numFrames > 4096) { node.Summary = "0 frames"; return; }

            var kfType = (FxbFloatKeyFrameType)(typeVal & 1u);
            if (kfType == FxbFloatKeyFrameType.Cubic)
            {
                for (int i = 0; i < numFrames; i++)
                {
                    long kfBase = _stream.Position;
                    node.CubicKeyframes.Add(new ViewModels.FxbCubicKeyframeRow
                    {
                        EndUnitTime = ReadF32(),
                        A           = ReadF32(),
                        B           = ReadF32(),
                        C           = ReadF32(),
                        D           = ReadF32(),
                        DataOffset  = kfBase,
                    });
                }
                node.Summary = $"Cubic x{numFrames}";
            }
            else
            {
                for (int i = 0; i < numFrames; i++)
                {
                    long kfBase = _stream.Position;
                    float time  = ReadF32();
                    float val   = ReadF32();
                    node.LinearKeyframes.Add(new ViewModels.FxbLinearKeyframeRow
                    {
                        UnitTime       = time,
                        Value          = val,
                        UnitTimeOffset = kfBase,
                        ValueOffset    = kfBase + 4,
                    });
                }
                node.Summary = $"Linear x{numFrames}";
            }
        }

        // Color keyframe

        private void ReadColorKeyframeData(ViewModels.FxbPropertyValueNode node, uint offset)
        {
            Seek(offset);
            int numFrames = ReadI32();
            if (numFrames <= 0 || numFrames > 4096) { node.Summary = "0 frames"; return; }

            for (int i = 0; i < numFrames; i++)
            {
                long kfBase = _stream.Position;
                float time  = ReadF32();
                uint  raw   = ReadU32();

                node.ColorKeyframes.Add(new ViewModels.FxbColorKeyframeRow
                {
                    UnitTime         = time,
                    Blue             = (byte)(raw & 0xFF),
                    Green            = (byte)((raw >> 8) & 0xFF),
                    Red              = (byte)((raw >> 16) & 0xFF),
                    Alpha            = (byte)((raw >> 24) & 0xFF),
                    UnitTimeOffset   = kfBase,
                    ColorValueOffset = kfBase + 4,
                });
            }
            node.Summary = $"ARGB x{numFrames}";
        }

        // Range / vector readers

        private void ReadFloatRange(ViewModels.FxbPropertyValueNode node, uint offset)
        {
            Seek(offset);
            float min = ReadF32();
            float max = ReadF32();
            node.FloatMin       = min;
            node.FloatMax       = max;
            node.FloatMinOffset = offset;
            node.FloatMaxOffset = offset + 4;
            node.Summary        = $"[{min:G5}, {max:G5}]";
        }

        private void ReadIntegerRange(ViewModels.FxbPropertyValueNode node, uint offset)
        {
            Seek(offset);
            int min = ReadI32();
            int max = ReadI32();
            node.IntMin       = min;
            node.IntMax       = max;
            node.IntMinOffset = offset;
            node.IntMaxOffset = offset + 4;
            node.Summary      = $"[{min}, {max}]";
        }

        private void ReadVector3(ViewModels.FxbPropertyValueNode node, uint offset)
        {
            Seek(offset);
            float x = ReadF32(), y = ReadF32(), z = ReadF32();
            node.VecX      = x;
            node.VecY      = y;
            node.VecZ      = z;
            node.VecOffset = offset;
            node.Summary   = $"({x:G4}, {y:G4}, {z:G4})";
        }

        private void ReadVector4(ViewModels.FxbPropertyValueNode node, uint offset)
        {
            Seek(offset);
            float x = ReadF32(), y = ReadF32(), z = ReadF32(), w = ReadF32();
            node.VecX      = x;
            node.VecY      = y;
            node.VecZ      = z;
            node.VecW      = w;
            node.VecOffset = offset;
            node.Summary   = $"({x:G4}, {y:G4}, {z:G4}, {w:G4})";
        }

        // String heap helper


        private string GetString(uint offset, ViewModels.FxbBankHeader hdr)
        {
            if (offset < FxbBankHeaderSize || offset >= hdr.m_ComponetDefOffset)
                return "";
            return ReadNullTerminatedString(offset);
        }

        private string ReadNullTerminatedString(uint offset)
        {
            try
            {
                Seek(offset);
                var sb = new StringBuilder(32);
                int b;
                while ((b = _reader.ReadByte()) != 0)
                    sb.Append((char)b);
                return sb.ToString();
            }
            catch
            {
                return "";
            }
        }

        // Primitive readers

        private void Seek(long offset) => _stream.Seek(offset, SeekOrigin.Begin);
        private uint  ReadU32() => _reader.ReadUInt32();
        private int   ReadI32() => _reader.ReadInt32();
        private float ReadF32() => _reader.ReadSingle();

        // IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _reader.Dispose();
            _disposed = true;
        }
    }
}
