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

            if (int.TryParse(root.Attribute("ShiftingFamily")?.Value, out int sf))
                data.ShiftingFamily = sf;

            data.SteeringWheelMaxDegrees = ReadFloat(root, "SteeringWheelMaxDegrees");
            data.HandGripAmount          = ReadFloat(root, "HandGripAmount", 0.8f);
            data.ReclineAmount           = ReadFloat(root, "ReclineAmount",  0f);

            foreach (var boneEl in root.Elements("Bone"))
            {
                var bone = new IKAnchorBone
                {
                    Name    = boneEl.Attribute("Name")?.Value  ?? boneEl.Element("Name")?.Value?.Trim() ?? "",
                    OffsetX = ReadFloat(boneEl, "OffsetX"),
                    OffsetY = ReadFloat(boneEl, "OffsetY"),
                    OffsetZ = ReadFloat(boneEl, "OffsetZ"),
                    RotX    = ReadFloat(boneEl, "RotX"),
                    RotY    = ReadFloat(boneEl, "RotY"),
                    RotZ    = ReadFloat(boneEl, "RotZ"),
                    ScaleX  = ReadFloat(boneEl, "ScaleX"),
                    ScaleY  = ReadFloat(boneEl, "ScaleY"),
                    ScaleZ  = ReadFloat(boneEl, "ScaleZ"),
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
            static void WriteF(XmlWriter w, string name, double v)
            {
                if (v != 0.0) w.WriteAttributeString(name, v.ToString("F6", CultureInfo.InvariantCulture));
            }

            using var w = XmlWriter.Create(sb, settings);
            w.WriteStartElement("IKAnchorBones");
            w.WriteAttributeString("Version", data.Version.ToString());
            if (data.ShiftingFamily > 0)
                w.WriteAttributeString("ShiftingFamily", data.ShiftingFamily.ToString());
            w.WriteAttributeString("SteeringWheelMaxDegrees", F(data.SteeringWheelMaxDegrees));
            w.WriteAttributeString("HandGripAmount",           F(data.HandGripAmount));
            w.WriteAttributeString("ReclineAmount",            F(data.ReclineAmount));

            foreach (var bone in data.Bones)
            {
                w.WriteStartElement("Bone");
                w.WriteAttributeString("Version", bone.BoneVersion.ToString());
                w.WriteAttributeString("Name",    bone.Name);
                WriteF(w, "OffsetX", bone.OffsetX);
                WriteF(w, "OffsetY", bone.OffsetY);
                WriteF(w, "OffsetZ", bone.OffsetZ);
                WriteF(w, "RotX",    bone.RotX);
                WriteF(w, "RotY",    bone.RotY);
                WriteF(w, "RotZ",    bone.RotZ);
                WriteF(w, "ScaleX",  bone.ScaleX);
                WriteF(w, "ScaleY",  bone.ScaleY);
                WriteF(w, "ScaleZ",  bone.ScaleZ);
                w.WriteEndElement();
            }

            w.WriteStartElement("Mojo");
            w.WriteAttributeString("Version", data.Version.ToString());
            w.WriteStartElement("EditLinkages");
            w.WriteAttributeString("Version", data.Version.ToString());
            w.WriteEndElement(); // EditLinkages
            w.WriteEndElement(); // Mojo

            w.WriteEndElement(); // IKAnchorBones
            w.Flush();
            return sb.ToString();
        }
    }
}
