using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ForzaTechStudio.Models
{
    // Observable model types


    public partial class ShakeBonesTransform : ObservableObject
    {
        [ObservableProperty] private string _name = "translateX";
        [ObservableProperty] private double _minVal;
        [ObservableProperty] private double _maxVal;

        public ShakeBonesTransform() { }

        public ShakeBonesTransform(string name, double minVal, double maxVal)
        {
            _name = name;
            _minVal = minVal;
            _maxVal = maxVal;
        }
    }

    public partial class ShakeBonesbone : ObservableObject
    {
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private int _translationNoiseIndex;
        [ObservableProperty] private int _rotationNoiseIndex;
        [ObservableProperty] private double _translationNoiseLagS;
        [ObservableProperty] private double _rotationNoiseLagS;

        public ObservableCollection<ShakeBonesTransform> Transforms { get; } = new();

        public ShakeBonesbone() { }

        public ShakeBonesbone(
            string name,
            int translationNoiseIndex,
            int rotationNoiseIndex,
            double translationNoiseLagS,
            double rotationNoiseLagS)
        {
            _name = name;
            _translationNoiseIndex = translationNoiseIndex;
            _rotationNoiseIndex = rotationNoiseIndex;
            _translationNoiseLagS = translationNoiseLagS;
            _rotationNoiseLagS = rotationNoiseLagS;
        }
    }

    public partial class ShakeBonesCamera : ObservableObject
    {
        [ObservableProperty] private string _name = "";

        public ObservableCollection<ShakeBonesbone> Bones { get; } = new();

        public ShakeBonesCamera() { }
        public ShakeBonesCamera(string name) => _name = name;
    }

    // Parser / Writer

    public static class ShakeBonesParser
    {

        // Returns the detected version (0 = original, 2 = Version="2") 
        public static ObservableCollection<ShakeBonesCamera> Parse(string xmlText, out int version)
        {
            var cameras = new ObservableCollection<ShakeBonesCamera>();

            var doc = XDocument.Parse(xmlText);
            var root = doc.Root;
            if (root == null) { version = 0; return cameras; }

            // Detect Version attribute on <ShakeBoneSettings>
            version = 0;
            var verAttr = root.Attribute("Version");
            if (verAttr != null && int.TryParse(verAttr.Value, out int v))
                version = v;

            foreach (var camEl in root.Elements("Camera"))
            {
                var cam = new ShakeBonesCamera(camEl.Attribute("name")?.Value ?? "");

                foreach (var boneEl in camEl.Elements("Bone"))
                {
                    double.TryParse(boneEl.Attribute("TranslationNoiseLagS")?.Value ?? "0",
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double tLag);

                    double.TryParse(boneEl.Attribute("RotationNoiseLagS")?.Value ?? "0",
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double rLag);

                    int.TryParse(boneEl.Attribute("TranslationNoiseIndex")?.Value ?? "0", out int tIdx);
                    int.TryParse(boneEl.Attribute("RotationNoiseIndex")?.Value ?? "0", out int rIdx);

                    var bone = new ShakeBonesbone(
                        boneEl.Attribute("name")?.Value ?? "",
                        tIdx, rIdx, tLag, rLag);

                    foreach (var xfEl in boneEl.Elements("Transform"))
                    {
                        double.TryParse(xfEl.Attribute("minVal")?.Value ?? "0",
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double minV);

                        double.TryParse(xfEl.Attribute("maxVal")?.Value ?? "0",
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double maxV);

                        bone.Transforms.Add(new ShakeBonesTransform(
                            xfEl.Attribute("name")?.Value ?? "translateX",
                            minV, maxV));
                    }

                    cam.Bones.Add(bone);
                }

                cameras.Add(cam);
            }

            return cameras;
        }

        public static string Serialize(System.Collections.Generic.IEnumerable<ShakeBonesCamera> cameras,
                                           bool useVersion2 = false)
        {
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "\t",
                Encoding = new UTF8Encoding(false),
                OmitXmlDeclaration = true,
                NewLineChars = "\n"
            };

            using var writer = XmlWriter.Create(sb, settings);

            writer.WriteStartElement("ShakeBoneSettings");
            if (useVersion2)
                writer.WriteAttributeString("Version", "2");

            foreach (var cam in cameras)
            {
                writer.WriteStartElement("Camera");
                writer.WriteAttributeString("name", cam.Name);

                foreach (var bone in cam.Bones)
                {
                    writer.WriteStartElement("Bone");
                    writer.WriteAttributeString("name", bone.Name);
                    writer.WriteAttributeString("TranslationNoiseIndex", bone.TranslationNoiseIndex.ToString());
                    writer.WriteAttributeString("RotationNoiseIndex", bone.RotationNoiseIndex.ToString());
                    writer.WriteAttributeString("TranslationNoiseLagS",
                        bone.TranslationNoiseLagS.ToString("G9", System.Globalization.CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("RotationNoiseLagS",
                        bone.RotationNoiseLagS.ToString("G9", System.Globalization.CultureInfo.InvariantCulture));

                    foreach (var xf in bone.Transforms)
                    {
                        writer.WriteStartElement("Transform");
                        writer.WriteAttributeString("name", xf.Name);
                        writer.WriteAttributeString("minVal",
                            xf.MinVal.ToString("G9", System.Globalization.CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("maxVal",
                            xf.MaxVal.ToString("G9", System.Globalization.CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement(); // Bone
                }

                writer.WriteEndElement(); // Camera
            }

            writer.WriteEndElement(); // ShakeBoneSettings
            writer.Flush();
            return sb.ToString();
        }
    }
}
