using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FileFormats
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BXMLFileHeader   // 0x0D = 13 bytes
    {
        public uint  Magic;             // 0x00  must == 0x4C4D5842 ('BXML')
        public byte  Version;           // 0x04  max supported = 2
        public int   StringCount;       // 0x05  number of string-table entries
        public uint  StringTableSize;   // 0x09  byte length of string table block
    }

    // Node model 

    [Flags]
    public enum BXMLNodeFlags : byte
    {
        IsNode        = 0x01,
        HasAttributes = 0x02,
        HasChildNodes = 0x04,
    }

    public sealed class BXMLNode
    {
        public BXMLNodeFlags           Flags      { get; set; }
        public string                  Name       { get; set; } = "";
        public List<BXMLAttribute>     Attributes { get; } = new List<BXMLAttribute>();
        public List<BXMLNode>          Children   { get; } = new List<BXMLNode>();
    }

    public readonly struct BXMLAttribute
    {
        public readonly string Name;
        public readonly string Value;
        public BXMLAttribute(string name, string value) { Name = name; Value = value; }
    }

    // Top-level file object

    public sealed class BXMLFile
    {
        // All unique strings from the string table, in file order.
        public List<string> StringTable { get; } = new List<string>();

        // The single root element node.
        public BXMLNode Root { get; set; } = new BXMLNode();
    }

    // Parser 

    public sealed class BXMLParser : IDisposable
    {
        private const uint  MAGIC           = 0x4C4D5842u; // 'BXML'
        private const byte  MAX_VERSION     = 2;

        private readonly BinaryReader _r;
        private int _stringCount; // cached from header, governs index width

        public BXMLParser(Stream stream, bool leaveOpen = false)
        {
            _r = new BinaryReader(stream, Encoding.UTF8, leaveOpen);
        }

        // Public factory 

        public static BXMLFile FromFile(string path)
        {
            using var fs     = File.OpenRead(path);
            using var parser = new BXMLParser(fs);
            return parser.Parse();
        }

        public static BXMLFile FromStream(Stream stream, bool leaveOpen = false)
        {
            using var parser = new BXMLParser(stream, leaveOpen);
            return parser.Parse();
        }

        // Core parse

        public BXMLFile Parse()
        {
            var file = new BXMLFile();

            // Header (13 bytes @ 0x00) 
            uint magic = _r.ReadUInt32();                       // 0x00  4 bytes
            if (magic != MAGIC)
                throw new InvalidDataException(
                    $"Not a BXML file — magic 0x{magic:X8} != 0x{MAGIC:X8}.");

            byte version = _r.ReadByte();                       // 0x04  1 byte
            if (version > MAX_VERSION)
                throw new InvalidDataException(
                    $"BXML version {version} exceeds max supported version {MAX_VERSION}.");

            int  stringCount     = _r.ReadInt32();              // 0x05  4 bytes
            uint stringTableSize = _r.ReadUInt32();             // 0x09  4 bytes

            _stringCount = stringCount;

            // String table (offset 0x0D) 
            // Each entry: int16 CharCount + UTF-8 bytes (no null terminator)
            for (int i = 0; i < stringCount; i++)
            {
                short charCount = _r.ReadInt16();               // length prefix
                byte[] bytes    = _r.ReadBytes(charCount);      // UTF-8 payload
                file.StringTable.Add(Encoding.UTF8.GetString(bytes));
            }

            // RootCount (1 byte immediately after string table) 
            // Always 0x01 in observed files; semantics unconfirmed 
            byte rootCount = _r.ReadByte();
            if (rootCount == 0)
                throw new InvalidDataException("BXML RootCount is 0 — no root node.");

            // Root node (recursive BXMLNode) 
            file.Root = ReadNode(file.StringTable);

            return file;
        }

        // Node reader (recursive)

        private BXMLNode ReadNode(List<string> strings)
        {
            var node = new BXMLNode();

            node.Flags = (BXMLNodeFlags)_r.ReadByte();              // Flags byte
            node.Name  = ReadStringIndex(strings);                   // NameIndex

            if ((node.Flags & BXMLNodeFlags.HasAttributes) != 0)
            {
                byte attrCount = _r.ReadByte();                      // AttrCount
                for (int i = 0; i < attrCount; i++)
                {
                    string attrName  = ReadStringIndex(strings);     // attr NameIndex
                    string attrValue = ReadStringIndex(strings);     // attr ValueIndex
                    node.Attributes.Add(new BXMLAttribute(attrName, attrValue));
                }
            }

            if ((node.Flags & BXMLNodeFlags.HasChildNodes) != 0)
            {
                ushort childCount = _r.ReadUInt16();                 // ChildCount (ushort)
                for (int i = 0; i < childCount; i++)
                    node.Children.Add(ReadNode(strings));
            }

            return node;
        }

        // String index reader 
        // Width is determined by _stringCount, set once from the file header.

        private string ReadStringIndex(List<string> strings)
        {
            int idx;
            if (_stringCount > ushort.MaxValue)
                idx = _r.ReadInt32();
            else if (_stringCount > byte.MaxValue)
                idx = _r.ReadUInt16();
            else
                idx = _r.ReadByte();

            if ((uint)idx >= (uint)strings.Count)
                throw new InvalidDataException(
                    $"String index {idx} out of range (table has {strings.Count} entries).");

            return strings[idx];
        }

        public void Dispose() => _r.Dispose();
    }

    // Serialiser

    public sealed class BXMLWriter : IDisposable
    {
        private const uint MAGIC         = 0x4C4D5842u; // 'BXML'
        private const byte VERSION       = 2;

        private readonly BinaryWriter _w;

        public BXMLWriter(Stream stream, bool leaveOpen = false)
        {
            _w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen);
        }

        // Public factory 

        public static void ToFile(BXMLFile file, string path)
        {
            using var fs = File.Create(path);
            using var bw = new BXMLWriter(fs);
            bw.Write(file);
        }

        public static void ToStream(BXMLFile file, Stream stream, bool leaveOpen = false)
        {
            using var bw = new BXMLWriter(stream, leaveOpen);
            bw.Write(file);
        }

        // Core serialise

        public void Write(BXMLFile file)
        {
            // Build the string table from the tree (deterministic sorted order,
            // matching the AlphaNumStringComparer used by the original game tools).
            var strings = BuildStringTable(file.Root);

            // Measure the string table size
            // Each entry: 2 bytes (int16 char count) + UTF-8 byte count.
            uint tableSize = 0;
            foreach (string s in strings)
                tableSize += (uint)(2 + Encoding.UTF8.GetByteCount(s));

            // Header (13 bytes)
            _w.Write(MAGIC);                    // 0x00  uint32
            _w.Write(VERSION);                  // 0x04  byte
            _w.Write(strings.Count);            // 0x05  int32
            _w.Write(tableSize);                // 0x09  uint32

            // String table
            foreach (string s in strings)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(s);
                _w.Write((short)bytes.Length); 
                _w.Write(bytes);
            }

            // RootCount
            _w.Write((byte)1);

            // Root node
            WriteNode(file.Root, strings);
        }



        private void WriteNode(BXMLNode node, List<string> strings)
        {
            _w.Write((byte)node.Flags);                         // Flags
            WriteStringIndex(node.Name, strings);               // NameIndex

            if ((node.Flags & BXMLNodeFlags.HasAttributes) != 0)
            {
                _w.Write((byte)node.Attributes.Count);          // AttrCount
                foreach (var attr in node.Attributes)
                {
                    WriteStringIndex(attr.Name,  strings);      // attr NameIndex
                    WriteStringIndex(attr.Value, strings);       // attr ValueIndex
                }
            }

            if ((node.Flags & BXMLNodeFlags.HasChildNodes) != 0)
            {
                _w.Write((ushort)node.Children.Count);          // ChildCount (ushort)
                foreach (var child in node.Children)
                    WriteNode(child, strings);
            }
        }


        private void WriteStringIndex(string s, List<string> strings)
        {
            int idx = strings.IndexOf(s);
            if (idx < 0)
                throw new InvalidOperationException(
                    $"String '{s}' not found in string table — call BuildStringTable first.");

            if (strings.Count > ushort.MaxValue)
                _w.Write(idx);
            else if (strings.Count > byte.MaxValue)
                _w.Write((ushort)idx);
            else
                _w.Write((byte)idx);
        }

        // String table builder 
        // Collects all unique strings (element names, attr names, attr values)
        // and sorts them with the same alphanumeric comparer the game tools use.

        private static List<string> BuildStringTable(BXMLNode root)
        {
            var set  = new HashSet<string>(StringComparer.Ordinal);
            CollectStrings(root, set);
            var list = new List<string>(set);
            list.Sort(AlphaNumComparer.Instance);
            return list;
        }

        private static void CollectStrings(BXMLNode node, HashSet<string> set)
        {
            set.Add(node.Name);
            foreach (var attr in node.Attributes)
            {
                set.Add(attr.Name);
                set.Add(attr.Value);
            }
            foreach (var child in node.Children)
                CollectStrings(child, set);
        }

        public void Dispose() => _w.Dispose();
    }


    public static class BXMLConverter
    {
        // BXML > XML

        public static void BxmlToXml(string bxmlPath, string xmlPath)
        {
            var file = BXMLParser.FromFile(bxmlPath);
            var settings = new XmlWriterSettings
            {
                Indent             = true,
                IndentChars        = "  ",
                Encoding           = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                OmitXmlDeclaration = false,
            };
            using var writer = XmlWriter.Create(xmlPath, settings);
            WriteXmlNode(writer, file.Root);
            writer.Flush();
        }


        // Converts a parsed BXMLFile to an XDocument (in memory).
 
        public static XDocument ToXDocument(BXMLFile file)
        {
            return new XDocument(ToXElement(file.Root));
        }

        private static XElement ToXElement(BXMLNode node)
        {
            var element = new XElement(node.Name);
            foreach (var attr in node.Attributes)
                element.Add(new XAttribute(attr.Name, attr.Value));
            foreach (var child in node.Children)
                element.Add(ToXElement(child));
            return element;
        }

        private static void WriteXmlNode(XmlWriter w, BXMLNode node)
        {
            w.WriteStartElement(node.Name);
            foreach (var attr in node.Attributes)
                w.WriteAttributeString(attr.Name, attr.Value);
            foreach (var child in node.Children)
                WriteXmlNode(w, child);
            w.WriteEndElement();
        }

        // XML > BXML

        public static void XmlToBxml(string xmlPath, string bxmlPath)
        {
            var doc = new XmlDocument();
            doc.Load(xmlPath);

            var file = FromXmlDocument(doc);
            BXMLWriter.ToFile(file, bxmlPath);
        }


        // Converts an in-memory XmlDocument to a BXMLFile.

        public static BXMLFile FromXmlDocument(XmlDocument doc)
        {
            var file = new BXMLFile();
            foreach (XmlNode n in doc.ChildNodes)
            {
                if (n is not XmlElement)
                    continue; // skip XmlDeclaration, comments, PIs
                file.Root = BuildBxmlNode(n);
                break;       // exactly one root element
            }
            if (file.Root == null)
                throw new InvalidOperationException("XML document contains no root element.");
            return file;
        }


        // Converts an XDocument to a BXMLFile.

        public static BXMLFile FromXDocument(XDocument doc)
        {
            if (doc.Root == null)
                throw new InvalidOperationException("XDocument has no root element.");
            var file = new BXMLFile();
            file.Root = BuildBxmlNodeFromXElement(doc.Root);
            return file;
        }

        private static BXMLNode BuildBxmlNode(XmlNode xmlNode)
        {
            var node  = new BXMLNode();
            node.Name  = xmlNode.Name;
            node.Flags = BXMLNodeFlags.IsNode;

            if (xmlNode.Attributes != null && xmlNode.Attributes.Count > 0)
            {
                node.Flags |= BXMLNodeFlags.HasAttributes;
                foreach (XmlAttribute attr in xmlNode.Attributes)
                    node.Attributes.Add(new BXMLAttribute(attr.Name, attr.Value));
            }

            foreach (XmlNode child in xmlNode.ChildNodes)
            {
                if (child is not XmlElement)
                    continue; // only element children are representable in BXML
                node.Flags |= BXMLNodeFlags.HasChildNodes;
                node.Children.Add(BuildBxmlNode(child));
            }

            return node;
        }

        private static BXMLNode BuildBxmlNodeFromXElement(XElement xel)
        {
            var node  = new BXMLNode();
            node.Name  = xel.Name.LocalName;
            node.Flags = BXMLNodeFlags.IsNode;

            foreach (var attr in xel.Attributes())
            {
                node.Flags |= BXMLNodeFlags.HasAttributes;
                node.Attributes.Add(new BXMLAttribute(attr.Name.LocalName, attr.Value));
            }

            foreach (var child in xel.Elements())
            {
                node.Flags |= BXMLNodeFlags.HasChildNodes;
                node.Children.Add(BuildBxmlNodeFromXElement(child));
            }

            return node;
        }
    }

    // Matches the sort order used by the original game tools to build the string
    // table.  Compares strings character-by-character

    internal sealed class AlphaNumComparer : IComparer<string>
    {
        public static readonly AlphaNumComparer Instance = new AlphaNumComparer();
        private AlphaNumComparer() { }

        public int Compare(string? x, string? y)
        {
            if (x is null && y is null) return 0;
            if (x is null) return -1;
            if (y is null) return  1;
            int len = Math.Min(x.Length, y.Length);
            for (int i = 0; i < len; i++)
            {
                if (x[i] < y[i]) return -1;
                if (x[i] > y[i]) return  1;
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}
