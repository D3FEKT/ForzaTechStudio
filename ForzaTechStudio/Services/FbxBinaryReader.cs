using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;

namespace ForzaTechStudio.Services
{
    // Represents a parsed FBX node with properties and children.
    public sealed class FbxNode
    {
        public byte[] Name { get; set; } = Array.Empty<byte>();
        public List<object> Properties { get; set; } = new();
        public List<byte> PropertyTypes { get; set; } = new();
        public List<FbxNode> Children { get; set; } = new();

        public string NameStr => Encoding.UTF8.GetString(Name);

        public FbxNode? FindChild(byte[] name)
        {
            for (int i = 0; i < Children.Count; i++)
                if (Children[i].Name.AsSpan().SequenceEqual(name))
                    return Children[i];
            return null;
        }

        public FbxNode? FindChild(string name)
            => FindChild(Encoding.UTF8.GetBytes(name));

        public IEnumerable<FbxNode> FindChildren(byte[] name)
        {
            for (int i = 0; i < Children.Count; i++)
                if (Children[i].Name.AsSpan().SequenceEqual(name))
                    yield return Children[i];
        }

        public IEnumerable<FbxNode> FindChildren(string name)
            => FindChildren(Encoding.UTF8.GetBytes(name));

        public object? GetProperty(int index)
            => index >= 0 && index < Properties.Count ? Properties[index] : null;
    }

    // Reads FBX binary (7.1–7.5) into a tree of FbxNode.
    public static class FbxBinaryReader
    {
        private static readonly byte[] _magic = Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0\x1A\0");

        // Property type markers, must match FBX binary spec exactly
        // Z=INT8  Y=INT16  B=BOOL  C=CHAR  I=INT32  F=FLOAT32  D=FLOAT64  L=INT64  R=BYTES  S=STRING
        private const byte PT_Int8   = (byte)'Z';
        private const byte PT_Int16  = (byte)'Y';
        private const byte PT_Bool   = (byte)'B';
        private const byte PT_Char   = (byte)'C';
        private const byte PT_Int32  = (byte)'I';
        private const byte PT_Float  = (byte)'F';
        private const byte PT_Double = (byte)'D';
        private const byte PT_Int64  = (byte)'L';
        private const byte PT_String = (byte)'S';
        private const byte PT_Raw    = (byte)'R';
        private const byte PT_FloatArr  = (byte)'f';
        private const byte PT_DoubleArr = (byte)'d';
        private const byte PT_Int32Arr  = (byte)'i';
        private const byte PT_Int64Arr  = (byte)'l';
        private const byte PT_BoolArr   = (byte)'b';
        private const byte PT_ByteArr   = (byte)'c';

        private static bool IsArrayType(byte t) => t is PT_FloatArr or PT_DoubleArr or PT_Int32Arr
            or PT_Int64Arr or PT_BoolArr or PT_ByteArr;

        public static FbxNode Parse(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs, Encoding.UTF8);

            // Magic
            byte[] magicRead = br.ReadBytes(_magic.Length);
            if (!magicRead.AsSpan().SequenceEqual(_magic))
                throw new InvalidDataException("Not a valid FBX binary file (bad magic).");

            uint version = br.ReadUInt32();

            // Versions >= 7500 use 64-bit offsets / 25-byte sentinel
            bool is64 = version >= 7500;
            int sentinelLen = is64 ? 25 : 13;

            var root = new FbxNode { Name = Array.Empty<byte>() };

            while (fs.Position < fs.Length)
            {
                var child = ReadNode(br, is64, sentinelLen);
                if (child == null) break;
                root.Children.Add(child);
            }

            return root;
        }

        private static FbxNode? ReadNode(BinaryReader br, bool is64, int sentinelLen)
        {
            long startPos = br.BaseStream.Position;
            if (startPos + sentinelLen > br.BaseStream.Length) return null;

            long endOffset;
            long propCount;

            if (is64)
            {
                endOffset = br.ReadInt64();
                propCount = br.ReadInt64();
                br.ReadInt64(); 
            }
            else
            {
                endOffset = br.ReadUInt32();
                propCount = br.ReadUInt32();
                br.ReadUInt32(); 
            }

            byte nameLen = br.ReadByte();
            byte[] name = br.ReadBytes(nameLen);

            if (endOffset == 0)
                return null;

            if (endOffset < br.BaseStream.Position || endOffset > br.BaseStream.Length)
                throw new InvalidDataException($"Invalid FBX node end offset {endOffset} at 0x{startPos:X}.");

            var node = new FbxNode { Name = name };

            for (long i = 0; i < propCount; i++)
            {
                byte type = br.ReadByte();
                node.PropertyTypes.Add(type);
                try
                {
                    node.Properties.Add(ReadProperty(br, type));
                }
                catch (Exception ex) when (ex is NotSupportedException or EndOfStreamException or InvalidDataException)
                {
                    throw new InvalidDataException($"Failed to read FBX property {i} on node '{node.NameStr}' at 0x{startPos:X} (type 0x{type:X2}).", ex);
                }
            }

            long pos = br.BaseStream.Position;
            if (pos > endOffset)
                throw new InvalidDataException($"FBX node '{node.NameStr}' overran its scope at 0x{startPos:X}.");

            if (pos < endOffset)
            {
                long childLimit = endOffset - sentinelLen;
                if (childLimit < pos)
                    throw new InvalidDataException($"FBX node '{node.NameStr}' has an invalid nested sentinel boundary at 0x{startPos:X}.");

                while (br.BaseStream.Position < childLimit)
                {
                    var child = ReadNode(br, is64, sentinelLen);
                    if (child == null) break;
                    node.Children.Add(child);
                }

                byte[] sentinel = br.ReadBytes(sentinelLen);
                if (sentinel.Length != sentinelLen || !IsAllZero(sentinel))
                    throw new InvalidDataException($"FBX node '{node.NameStr}' has an invalid nested block sentinel at 0x{startPos:X}.");
            }

            if (br.BaseStream.Position != endOffset)
                throw new InvalidDataException($"FBX node '{node.NameStr}' ended at 0x{br.BaseStream.Position:X}, expected 0x{endOffset:X}.");

            return node;
        }

        private static bool IsAllZero(byte[] data)
        {
            for (int i = 0; i < data.Length; i++)
                if (data[i] != 0)
                    return false;
            return true;
        }

        private static object ReadProperty(BinaryReader br, byte type)
        {
            switch (type)
            {
                case PT_Int8:   return br.ReadSByte();
                case PT_Int16:  return br.ReadInt16();
                case PT_Bool:   return br.ReadByte() != 0;
                case PT_Char:   return (char)br.ReadByte();
                case PT_Int32:  return br.ReadInt32();
                case PT_Float:  return br.ReadSingle();
                case PT_Double: return br.ReadDouble();
                case PT_Int64:  return br.ReadInt64();
                case PT_String: { int len = br.ReadInt32(); return Encoding.UTF8.GetString(br.ReadBytes(len)); }
                case PT_Raw:    { int len = br.ReadInt32(); return br.ReadBytes(len); }
                case PT_FloatArr:  return ReadArray<float>(br);
                case PT_DoubleArr: return ReadArray<double>(br);
                case PT_Int32Arr:  return ReadArray<int>(br);
                case PT_Int64Arr:  return ReadArray<long>(br);
                case PT_BoolArr:   return ReadBoolArray(br);
                case PT_ByteArr:   return ReadByteArray(br);
                default: throw new NotSupportedException($"Unknown FBX property type: {(char)type} (0x{type:X2})");
            }
        }

        private static T[] ReadArray<T>(BinaryReader br) where T : unmanaged
        {
            uint length   = br.ReadUInt32();
            uint encoding = br.ReadUInt32();
            uint compLen  = br.ReadUInt32();

            byte[] raw = br.ReadBytes((int)compLen);
            if (raw.Length != compLen)
                throw new EndOfStreamException("Unexpected end of FBX array data.");

            if (encoding == 1)
                raw = DecompressZlib(raw, length * (uint)Unsafe.SizeOf<T>());

            int expectedBytes = checked((int)(length * (uint)Unsafe.SizeOf<T>()));
            if (raw.Length < expectedBytes)
                throw new InvalidDataException($"FBX array decoded to {raw.Length} bytes, expected {expectedBytes}.");

            T[] arr = new T[length];
            Buffer.BlockCopy(raw, 0, arr, 0, expectedBytes);
            return arr;
        }

        private static bool[] ReadBoolArray(BinaryReader br)
        {
            uint length   = br.ReadUInt32();
            uint encoding = br.ReadUInt32();
            uint compLen  = br.ReadUInt32();
            byte[] raw = br.ReadBytes((int)compLen);
            if (raw.Length != compLen)
                throw new EndOfStreamException("Unexpected end of FBX bool array data.");

            if (encoding == 1)
                raw = DecompressZlib(raw, length);

            if (raw.Length < length)
                throw new InvalidDataException($"FBX bool array decoded to {raw.Length} bytes, expected {length}.");

            bool[] arr = new bool[length];
            for (int i = 0; i < length && i < raw.Length; i++)
                arr[i] = raw[i] != 0;
            return arr;
        }

        private static byte[] ReadByteArray(BinaryReader br)
        {
            uint length   = br.ReadUInt32();
            uint encoding = br.ReadUInt32();
            uint compLen  = br.ReadUInt32();
            byte[] raw = br.ReadBytes((int)compLen);
            if (raw.Length != compLen)
                throw new EndOfStreamException("Unexpected end of FBX byte array data.");

            if (encoding == 1)
                raw = DecompressZlib(raw, length);

            if (raw.Length < length)
                throw new InvalidDataException($"FBX byte array decoded to {raw.Length} bytes, expected {length}.");

            return raw;
        }

        private static byte[] DecompressZlib(byte[] data, uint expectedLength)
        {
            // ZLibStream handles all valid zlib headers (0x78 0x01/0x5E/0x9C/0xDA etc.)
            // Falls back to raw deflate if data lacks a zlib header
            try
            {
                using var src = new MemoryStream(data);
                using var zlib = new ZLibStream(src, CompressionMode.Decompress);
                using var dst = new MemoryStream((int)expectedLength);
                zlib.CopyTo(dst);
                return dst.ToArray();
            }
            catch
            {
                // Raw deflate fallback (no zlib header)
                using var src = new MemoryStream(data);
                using var deflate = new DeflateStream(src, CompressionMode.Decompress);
                using var dst = new MemoryStream((int)expectedLength);
                deflate.CopyTo(dst);
                return dst.ToArray();
            }
        }
    }
}
