using ForzaTechStudio.Models;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ForzaTechStudio.Services
{
    public static class IKAnchorBonesParser
    {
        // Helpers

        private static float ReadFloat(XElement? el, string name, float fallback = 0f)
        {
            if (el == null) return fallback;
            var val = el.Attribute(name)?.Value ?? el.Element(name)?.Value?.Trim();
            if (val != null && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                return v;
            return fallback;
        }

        // Parse

        public static IKAnchorBonesData Parse(string xmlText)
        {
            var doc  = XDocument.Parse(xmlText);
            var root = doc.Root;
            var data = new IKAnchorBonesData { SourceDoc = doc };
            if (root == null) return data;

            if (int.TryParse(root.Attribute("Version")?.Value, out int rv))
                data.Version = rv;

            // HandGripAmount / ReclineAmount are present in the global library file
            // but absent in per-car override files – default to 0.8 / 0 if missing.
            data.HandGripAmount = ReadFloat(root, "HandGripAmount", 0.8f);
            data.ReclineAmount  = ReadFloat(root, "ReclineAmount",  0f);

            foreach (var boneEl in root.Elements("Bone"))
            {
                var bone = new IKAnchorBone
                {
                    Name    = boneEl.Attribute("Name")?.Value  ?? boneEl.Element("Name")?.Value?.Trim() ?? "",
                    OffsetX = ReadFloat(boneEl, "OffsetX"),
                    OffsetY = ReadFloat(boneEl, "OffsetY"),
                    OffsetZ = ReadFloat(boneEl, "OffsetZ"),
                };
                if (int.TryParse(boneEl.Attribute("Version")?.Value, out int bv))
                    bone.BoneVersion = bv;
                data.Bones.Add(bone);
            }

            return data;
        }

        // Serialize

        public static string Serialize(IKAnchorBonesData data)
        {
            var sb       = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent             = true,
                IndentChars        = "  ",
                OmitXmlDeclaration = true,
                NewLineChars       = "\n",
                Encoding           = new UTF8Encoding(false)
            };

            static string F(double v) => v.ToString("F6", CultureInfo.InvariantCulture);

            using var w = XmlWriter.Create(sb, settings);
            w.WriteStartElement("IKAnchorBones");
            w.WriteAttributeString("Version", data.Version.ToString());

            foreach (var bone in data.Bones)
            {
                w.WriteStartElement("Bone");
                w.WriteAttributeString("Version", bone.BoneVersion.ToString());
                w.WriteAttributeString("Name",    bone.Name);
                w.WriteAttributeString("OffsetX", F(bone.OffsetX));
                w.WriteAttributeString("OffsetY", F(bone.OffsetY));
                w.WriteAttributeString("OffsetZ", F(bone.OffsetZ));
                w.WriteEndElement();
            }

            w.WriteEndElement(); // IKAnchorBones
            w.Flush();
            return sb.ToString();
        }
    }
}
