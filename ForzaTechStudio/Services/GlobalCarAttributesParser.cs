using ForzaTechStudio.Models;
using System.Xml.Linq;

namespace ForzaTechStudio.Services
{
    // Parses and serializes GlobalCarAttributes.xml using a generic section model,
    // allowing full round-trip of all attributes including undocumented groups.
    public static class GlobalCarAttributesParser
    {
        // Parse

        public static GlobalCarAttributesData Parse(string xmlText)
        {
            var doc  = XDocument.Parse(xmlText);
            var root = doc.Root;
            var data = new GlobalCarAttributesData { SourceDoc = doc };
            if (root == null) return data;

            if (int.TryParse(root.Attribute("Version")?.Value, out int rv))
                data.RootVersion = rv;

            foreach (var el in root.Elements())
            {
                var section = new GlobalCarAttributeSection
                {
                    DisplayName     = el.Name.LocalName,
                    SectionVersion  = el.Attribute("Version")?.Value ?? "",
                    HasChildElements = el.HasElements,
                    RawElement      = el,
                };

                foreach (var attr in el.Attributes())
                {
                    if (attr.Name.LocalName == "Version") continue; // exposed as SectionVersion
                    section.Attributes.Add(new SectionAttribute { Key = attr.Name.LocalName, Value = attr.Value });
                }

                data.Sections.Add(section);
            }

            return data;
        }

        // Serialize

        public static string Serialize(GlobalCarAttributesData data)
        {
            if (data.SourceDoc?.Root == null) return "";

            // Update root Version
            data.SourceDoc.Root.SetAttributeValue("Version", data.RootVersion.ToString());

            // Write each section's edited attributes back into its XElement
            foreach (var section in data.Sections)
            {
                if (section.RawElement == null) continue;

                // Update Version attribute if present
                if (!string.IsNullOrEmpty(section.SectionVersion))
                    section.RawElement.SetAttributeValue("Version", section.SectionVersion);

                foreach (var attr in section.Attributes)
                    section.RawElement.SetAttributeValue(attr.Key, attr.Value);
            }

            return data.SourceDoc.ToString(SaveOptions.None);
        }
    }
}
