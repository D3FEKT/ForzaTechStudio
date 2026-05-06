using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression; // Required for ZipArchive
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;

namespace ForzaTechStudio.Services
{
    public class MaterialEntry
    {
        [JsonPropertyName("MaterialMetaData")]
        public string MaterialMetaData { get; set; }

        [JsonPropertyName("MaterialBlob")]
        public string MaterialBlob { get; set; }
    }

    [JsonSerializable(typeof(Dictionary<string, MaterialEntry>))]
    public partial class MaterialJsonContext : JsonSerializerContext
    {
    }

    public class MaterialExtractionService
    {
        // "Name" in reversed bytes
        private static readonly byte[] nameTag = new byte[] { 0x65, 0x6D, 0x61, 0x4E };
        // "Id  " in reversed bytes
        private static readonly byte[] idTag = new byte[] { 0x20, 0x20, 0x64, 0x49 };

        public async Task<int> ExtractMaterialsAsync(IEnumerable<string> filePaths, string? gameId = null)
        {
            var materials = new Dictionary<string, MaterialEntry>(
                MaterialLibrary.LoadEntries(gameId, out _, includeLegacyFallback: true),
                StringComparer.OrdinalIgnoreCase);

            await Task.Run(() =>
            {
                foreach (var path in filePaths)
                {
                    try
                    {
                        var extension = Path.GetExtension(path).ToLower();
                        if (extension == ".zip")
                        {
                            ProcessZip(path, materials);
                        }
                        else if (extension == ".modelbin" || extension == ".materialbin")
                        {
                            ProcessFile(path, materials);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to process file {path}: {ex.Message}");
                    }
                }

                MaterialLibrary.SaveEntries(materials, gameId);
            });

            return materials.Count;
        }

        public static List<string> GetMaterialNames(string filePath)
        {
            var results = new HashSet<string>();
            try
            {
                using var stream = File.OpenRead(filePath);
                var bundle = new Bundle();
                bundle.Load(stream);

                foreach (var blob in bundle.Blobs)
                {
                    if (blob is MaterialBlob materialBlob)
                    {
                        var name = GetMaterialName(materialBlob);
                        if (!string.IsNullOrEmpty(name))
                        {
                            results.Add(name);
                        }
                    }
                    else if (blob is MaterialResourceBlob materialResource)
                    {
                        if (!string.IsNullOrEmpty(materialResource.Path))
                        {
                            // Extract just the material name from the path
                            // e.g. "scene/library/materials/my_mat.materialbin" -> "my_mat"
                            var name = Path.GetFileNameWithoutExtension(materialResource.Path);
                            if (!string.IsNullOrEmpty(name))
                            {
                                results.Add(name);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get material names from {filePath}: {ex.Message}");
            }
            return results.ToList();
        }

        private void ProcessZip(string zipPath, Dictionary<string, MaterialEntry> materials)
        {
            try
            {
                // Create a temp directory for extraction
                string tempDir = Path.Combine(Path.GetTempPath(), "ForzaMaterials_" + Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);

                try
                {
                    // Use CustomZipFile to handle Forza compression methods (including Method 21)
                    using (var zip = new CustomZipFile(zipPath))
                    {
                        // Filter validation to extract only relevant files
                        zip.ExtractToDirectory(tempDir, fileName => 
                        {
                            var ext = Path.GetExtension(fileName).ToLower();
                            return ext == ".modelbin" || ext == ".materialbin";
                        });
                    }

                    // Process extracted files
                    string[] extractedFiles = Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories);
                    foreach (var file in extractedFiles)
                    {
                        // File extension check is redundant if filter worked, but safe
                        var ext = Path.GetExtension(file).ToLower();
                        if (ext == ".modelbin" || ext == ".materialbin")
                        {
                            ProcessFile(file, materials);
                        }
                    }
                }
                finally
                {
                    // Cleanup
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing zip {zipPath}: {ex.Message}");
            }
        }

        private void ProcessFile(string filePath, Dictionary<string, MaterialEntry> materials)
        {
            try
            {
                // FileStream is seekable, so we can pass it directly
                using var stream = File.OpenRead(filePath);
                ProcessBundleStream(stream, materials, filePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading file {filePath}: {ex.Message}");
            }
        }

        private void ProcessBundleStream(Stream stream, Dictionary<string, MaterialEntry> materials, string sourceName)
        {
            try
            {
                var bundle = new Bundle();
                bundle.Load(stream);

                foreach (var blob in bundle.Blobs)
                {
                    // FIX: Check for TAG_BLOB_MaterialInstance (MatI = 0x4D617449), not TAG_BLOB_Material
                    // MaterialBlob is the C# class, but the tag is MaterialInstance
                    if (blob is MaterialBlob materialBlob)
                    {
                        string materialName = GetMaterialName(materialBlob);

                        // Fallback if no name found
                        if (string.IsNullOrEmpty(materialName))
                        {
                            materialName = $"unnamed_material_{Guid.NewGuid().ToString().Substring(0, 8)}";
                        }

                        // 1. Generate Metadata Hex
                        string metadataHex = CreateFormattedMetadataHex(materialName);

                        // 2. Get Blob Data (Hex)
                        byte[] blobData = materialBlob.GetContents();
                        string blobHex = BitConverter.ToString(blobData).Replace("-", " ");

                        // Avoid duplicates
                        if (!materials.ContainsKey(materialName))
                        {
                            materials[materialName] = new MaterialEntry
                            {
                                MaterialMetaData = metadataHex,
                                MaterialBlob = blobHex
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing bundle in {sourceName}: {ex.Message}");
            }
        }

        internal static string GetMaterialName(MaterialBlob materialBlob)
        {
            // Try to find name in direct metadata
            var nameMeta = materialBlob.Metadatas.OfType<NameMetadata>().FirstOrDefault();
            if (nameMeta != null && !string.IsNullOrEmpty(nameMeta.Name))
            {
                return nameMeta.Name.TrimEnd('\0');
            }

            // Check nested bundles
            if (materialBlob.Bundle != null)
            {
                foreach (var nestedBlob in materialBlob.Bundle.Blobs)
                {
                    var nestedNameMeta = nestedBlob.Metadatas.OfType<NameMetadata>().FirstOrDefault();
                    if (nestedNameMeta != null && !string.IsNullOrEmpty(nestedNameMeta.Name))
                    {
                        return nestedNameMeta.Name.TrimEnd('\0');
                    }
                }
            }

            return string.Empty;
        }

        private string CreateFormattedMetadataHex(string materialName)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(materialName);
            byte stringLengthByte = (byte)(nameBytes.Length);
            // Program.cs adds 8 to the length for this offset byte
            byte sizeByteWithOffset = (byte)(nameBytes.Length + 8);

            byte firstByte, secondByte;
            CalculateSpecialSizeFormat(stringLengthByte, out firstByte, out secondByte);

            using (MemoryStream ms = new MemoryStream())
            {
                // Name Tag Block
                ms.Write(nameTag, 0, nameTag.Length);
                ms.WriteByte(firstByte);
                ms.WriteByte(secondByte);
                ms.WriteByte(0x10);
                ms.WriteByte(0x00);

                // ID Tag Block
                ms.Write(idTag, 0, idTag.Length);
                ms.WriteByte(0x40);
                ms.WriteByte(0x00);

                // Size/Data Block
                ms.WriteByte(sizeByteWithOffset);
                ms.WriteByte(0x00);

                ms.Write(nameBytes, 0, nameBytes.Length);

                // Padding
                ms.WriteByte(0x00);
                ms.WriteByte(0x00);
                ms.WriteByte(0x00);
                ms.WriteByte(0x00);

                return BitConverter.ToString(ms.ToArray()).Replace("-", " ");
            }
        }

        private void CalculateSpecialSizeFormat(byte size, out byte firstByte, out byte secondByte)
        {
            byte highNibble = (byte)((size & 0xF0) >> 4);
            byte lowNibble = (byte)(size & 0x0F);
            firstByte = (byte)(lowNibble << 4);
            secondByte = highNibble;
        }
    }
}