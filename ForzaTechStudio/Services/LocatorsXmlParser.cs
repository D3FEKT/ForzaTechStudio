using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ForzaTechStudio.Services
{
    public class LocatorEntry
    {
        public int Version { get; set; }
        public string Guid { get; set; }
        public string SrcGuid { get; set; }
        public string Name { get; set; }
        public Matrix4x4 SceneTransform { get; set; }
        public string AttachToBoneName { get; set; }
        public int AttachSnap { get; set; }
        public int IsUpgradable { get; set; }
        public string PartType { get; set; }
        public int UpgradeLevel { get; set; }
        public int PartId { get; set; }
        public int ParentUpgradeId { get; set; }

        // Raw XML element for round-trip preservation of unknown attributes
        internal XElement RawElement { get; set; }
    }

    public class LocatorsData
    {
        public string FilePath { get; set; }
        public XDocument SourceDocument { get; set; }
        public List<LocatorEntry> Locators { get; set; } = new();
    }

    // Parses and serializes Forza locators.xml files.
    public class LocatorsXmlParser
    {
        public LocatorsData Parse(string filePath)
        {
            var data = new LocatorsData { FilePath = filePath };

            // Forza locators.xml files contain malformed attribute values like
            // BoneName="<root>" where '<' / '>' are not escaped.  We must
            // sanitize the raw text before handing it to XDocument.
            string rawXml = File.ReadAllText(filePath);
            string sanitized = SanitizeAttributeValues(rawXml);

            var doc = XDocument.Parse(sanitized);
            data.SourceDocument = doc;

            var root = doc.Root;
            if (root == null) return data;

            // The root can be <Locators>, <LocatorsXml>, or any wrapper � search all
            // descendant <Locator> elements regardless of nesting depth or root name.
            foreach (var el in root.Descendants("Locator"))
            {
                // Skip nested <Locator> elements that are children of another <Locator>
                if (el.Parent?.Name.LocalName == "Locator") continue;

                var entry = ParseLocatorElement(el);
                if (entry != null)
                    data.Locators.Add(entry);
            }

            return data;
        }

        public LocatorsData ParseFromString(string xml, string filePath = null)
        {
            var data = new LocatorsData { FilePath = filePath };

            string sanitized = SanitizeAttributeValues(xml);
            var doc = XDocument.Parse(sanitized);
            data.SourceDocument = doc;

            var root = doc.Root;
            if (root == null) return data;

            foreach (var el in root.Descendants("Locator"))
            {
                if (el.Parent?.Name.LocalName == "Locator") continue;

                var entry = ParseLocatorElement(el);
                if (entry != null)
                    data.Locators.Add(entry);
            }

            return data;
        }

        // Escapes bare angle-bracket characters inside XML attribute values.
        private static string SanitizeAttributeValues(string xml)
        {
            // Match  ="..."  attribute values.
            return Regex.Replace(xml, """=(")(.*?)(")""", m =>
            {
                string content = m.Groups[2].Value;
                if (content.Contains('<') || content.Contains('>'))
                {
                    content = content.Replace("<", "&lt;")
                                     .Replace(">", "&gt;");
                }
                return $"=\"{content}\"";
            });
        }

        private static LocatorEntry ParseLocatorElement(XElement el)
        {
            if (el == null) return null;

            var entry = new LocatorEntry();
            entry.RawElement = el;

            // Version attribute on <Locator>
            entry.Version = int.TryParse(el.Attribute("Version")?.Value, out int ver) ? ver : 3;

            // GUID
            entry.Guid = el.Element("GUID")?.Attribute("value")?.Value ?? "";
            entry.SrcGuid = el.Element("SrcGUID")?.Attribute("value")?.Value ?? "";

            // <Name value="carLocator_Exhaust_001"/> � attribute form (primary)
            // Fall back to element text content if the attribute is absent
            var nameEl = el.Element("Name");
            entry.Name = nameEl?.Attribute("value")?.Value
                      ?? nameEl?.Value?.Trim()
                      ?? "";

            // SceneTransform � 16 floats stored as value._11 � value._44
            var stEl = el.Element("SceneTransform");
            if (stEl != null)
            {
                float Get(string attr) =>
                    float.TryParse(stEl.Attribute(attr)?.Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float v) ? v : 0f;

                entry.SceneTransform = new Matrix4x4(
                    Get("value._11"), Get("value._12"), Get("value._13"), Get("value._14"),
                    Get("value._21"), Get("value._22"), Get("value._23"), Get("value._24"),
                    Get("value._31"), Get("value._32"), Get("value._33"), Get("value._34"),
                    Get("value._41"), Get("value._42"), Get("value._43"), Get("value._44")
                );
            }

            // AttachToBone
            var atkEl = el.Element("AttachToBone");
            entry.AttachToBoneName = atkEl?.Attribute("BoneName")?.Value ?? "";
            entry.AttachSnap = int.TryParse(atkEl?.Attribute("Snap")?.Value, out int snap) ? snap : 0;

            // UpgradeInfo
            var upEl = el.Element("UpgradeInfo");
            entry.IsUpgradable = int.TryParse(upEl?.Attribute("IsUpgradable")?.Value, out int isUp) ? isUp : 0;
            entry.PartType = upEl?.Attribute("PartType")?.Value ?? "";
            entry.UpgradeLevel = int.TryParse(upEl?.Attribute("UpgradeLevel")?.Value, out int ul) ? ul : 0;
            entry.PartId = int.TryParse(upEl?.Attribute("PartId")?.Value, out int pid) ? pid : -1;
            entry.ParentUpgradeId = int.TryParse(upEl?.Attribute("ParentUpgradeId")?.Value, out int puid) ? puid : -1;

            return entry;
        }

        // Writes changes back to the source XML and saves. Restores bare angle-bracket
        // convention in attribute values for game-engine compatibility.
        public void Save(LocatorsData data, string filePath = null)
        {
            string path = filePath ?? data.FilePath;
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("No file path specified for saving.");

            ApplyChangesToDocument(data);

            // XDocument.Save would write &lt; / &gt; which is correct XML but
            // Forza expects the raw '<' / '>' characters.  Write through a
            // string and restore them.
            string xmlText = data.SourceDocument.ToString();
            xmlText = DesanitizeAttributeValues(xmlText);
            File.WriteAllText(path, xmlText);
        }

        // Returns the serialised XML as a string.
        public string Serialize(LocatorsData data)
        {
            ApplyChangesToDocument(data);
            string xmlText = data.SourceDocument.ToString();
            return DesanitizeAttributeValues(xmlText);
        }

        // Restores &amp;lt; / &amp;gt; inside attribute values back to bare &lt; / &gt;
        // so the output matches the Forza-expected format.
        private static string DesanitizeAttributeValues(string xml)
        {
            return Regex.Replace(xml, """=("")(.*?)(")""", m =>
            {
                string content = m.Groups[2].Value;
                if (content.Contains("&lt;") || content.Contains("&gt;"))
                {
                    content = content.Replace("&lt;", "<")
                                     .Replace("&gt;", ">")
                                     .Replace("&amp;", "&");
                }
                return $"=\"{content}\"";
            });
        }

        private static void ApplyChangesToDocument(LocatorsData data)
        {
            foreach (var entry in data.Locators)
            {
                if (entry.RawElement == null) continue;

                var el = entry.RawElement;

                // Update Name
                var nameEl = el.Element("Name");
                if (nameEl != null)
                    nameEl.SetAttributeValue("value", entry.Name);

                // Update SceneTransform
                var stEl = el.Element("SceneTransform");
                if (stEl == null)
                {
                    stEl = new XElement("SceneTransform");
                    el.Add(stEl);
                }

                var m = entry.SceneTransform;
                string F(float v) => v.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);

                stEl.SetAttributeValue("value._11", F(m.M11)); stEl.SetAttributeValue("value._12", F(m.M12));
                stEl.SetAttributeValue("value._13", F(m.M13)); stEl.SetAttributeValue("value._14", F(m.M14));
                stEl.SetAttributeValue("value._21", F(m.M21)); stEl.SetAttributeValue("value._22", F(m.M22));
                stEl.SetAttributeValue("value._23", F(m.M23)); stEl.SetAttributeValue("value._24", F(m.M24));
                stEl.SetAttributeValue("value._31", F(m.M31)); stEl.SetAttributeValue("value._32", F(m.M32));
                stEl.SetAttributeValue("value._33", F(m.M33)); stEl.SetAttributeValue("value._34", F(m.M34));
                stEl.SetAttributeValue("value._41", F(m.M41)); stEl.SetAttributeValue("value._42", F(m.M42));
                stEl.SetAttributeValue("value._43", F(m.M43)); stEl.SetAttributeValue("value._44", F(m.M44));
            }
        }
    }
}
