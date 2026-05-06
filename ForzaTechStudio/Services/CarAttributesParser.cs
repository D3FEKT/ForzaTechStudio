using ForzaTechStudio.Models;
using System;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ForzaTechStudio.Services
{
    public static class CarAttributesParser
    {
        // Helpers

        private static float ReadFloat(XElement? parent, string name, float fallback = 0f)
        {
            if (parent == null) return fallback;
            var val = parent.Attribute(name)?.Value ?? parent.Element(name)?.Value?.Trim();
            if (val != null && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                return v;
            return fallback;
        }

        // Parse

        public static CarAttributesData Parse(string xmlText)
        {
            var data = new CarAttributesData();
            var doc  = XDocument.Parse(xmlText);
            var root = doc.Root;
            if (root == null) return data;

            if (int.TryParse(root.Attribute("Version")?.Value, out int rv))
                data.RootVersion = rv;

            var wsrEl = root.Element("WindshieldReflectionSettings");
            if (wsrEl != null)
            {
                var wsr = data.WindshieldReflection;

                int wsrVer = 1;
                if (int.TryParse(wsrEl.Attribute("Version")?.Value, out int wv)) wsrVer = wv;
                wsr.Version = wsrVer;

                wsr.EyeOffset      = ReadFloat(wsrEl, "EyeOffset");
                wsr.AngleOffset    = ReadFloat(wsrEl, "AngleOffset");
                wsr.ShearAmount    = ReadFloat(wsrEl, "ShearAmount");
                wsr.TextureOffsetX = ReadFloat(wsrEl, "TextureOffsetX");
                wsr.TextureOffsetY = ReadFloat(wsrEl, "TextureOffsetY");
                wsr.TextureScaleX  = ReadFloat(wsrEl, "TextureScaleX", 1f);
                wsr.TextureScaleY  = ReadFloat(wsrEl, "TextureScaleY", 1f);
                if (wsrVer >= 2)
                {
                    wsr.FadeStart  = ReadFloat(wsrEl, "FadeStart");
                    wsr.FadeEnd    = ReadFloat(wsrEl, "FadeEnd",   1f);
                    wsr.MaxAmount  = ReadFloat(wsrEl, "MaxAmount", 1f);
                }
            }

            return data;
        }


        // Serialize

        public static string Serialize(CarAttributesData data)
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

            using var w = XmlWriter.Create(sb, settings);

            w.WriteStartElement("CarAttributes");
            if (data.RootVersion > 0)
                w.WriteAttributeString("Version", data.RootVersion.ToString());

            var wsr = data.WindshieldReflection;
            w.WriteStartElement("WindshieldReflectionSettings");
            if (wsr.Version >= 2)
                w.WriteAttributeString("Version", wsr.Version.ToString());

            static string F(double v) => v.ToString("G9", CultureInfo.InvariantCulture);
            w.WriteElementString("EyeOffset",      F(wsr.EyeOffset));
            w.WriteElementString("AngleOffset",    F(wsr.AngleOffset));
            w.WriteElementString("ShearAmount",    F(wsr.ShearAmount));
            w.WriteElementString("TextureOffsetX", F(wsr.TextureOffsetX));
            w.WriteElementString("TextureOffsetY", F(wsr.TextureOffsetY));
            w.WriteElementString("TextureScaleX",  F(wsr.TextureScaleX));
            w.WriteElementString("TextureScaleY",  F(wsr.TextureScaleY));
            if (wsr.Version >= 2)
            {
                w.WriteElementString("FadeStart", F(wsr.FadeStart));
                w.WriteElementString("FadeEnd",   F(wsr.FadeEnd));
                w.WriteElementString("MaxAmount", F(wsr.MaxAmount));
            }

            w.WriteEndElement(); // WindshieldReflectionSettings
            w.WriteEndElement(); // CarAttributes
            w.Flush();
            return sb.ToString();
        }
    }
}
