using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text;
using ForzaTools.Bundles.Blobs;
using ForzaTechStudio.Models;

namespace ForzaTechStudio.Services
{
    public enum FbxExportFormat { Ascii, Binary }

    // Writes FBX 7.4 ASCII or Binary from Modelbin export data.

    public static class FbxExportService
    {
        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        // Public entry point 

        public static void Export(IEnumerable<ModelBinExportData> models, string outputPath, FbxExportFormat format,
            Dictionary<string, string>? texMap = null, ExportOptions? options = null)
        {
            var list = models.ToList();
            var opt = options ?? new ExportOptions();
            if (format == FbxExportFormat.Binary)
                ExportBinary(list, outputPath, texMap, opt);
            else
                ExportAscii(list, outputPath, texMap, opt);
        }


        //  ASCII writer


        private static void ExportAscii(List<ModelBinExportData> models, string outputPath, Dictionary<string, string>? texMap, ExportOptions opt)
            => File.WriteAllText(outputPath, BuildFbxAscii(models, texMap, opt, Path.GetDirectoryName(outputPath) ?? string.Empty), new UTF8Encoding(false));

        private static string BuildFbxAscii(List<ModelBinExportData> models, Dictionary<string, string>? texMap, ExportOptions opt, string outputDir)
        {
            var sb = new StringBuilder(1 << 20);

            var allMeshes = FlattenMeshes(models);
            var (matList, matIndex) = CollectMaterials(allMeshes);
            // Build per-material texture path list (only mats that have a known DDS path)
            var matTexPaths = BuildMatTexPaths(matList, texMap);
            int texCount = matTexPaths.Count(p => p != null);

            var mbNodeId = MakeModelBinIds(models.Count);
            long MeshModelId(int fi)    => 200_000L + fi * 2;
            long MeshGeoId(int fi)      => 200_001L + fi * 2;
            long MatNodeId(int mi)      => 900_000L + mi;
            long TexNodeId(int mi)      => 950_000L + mi;

            var exportTransform = GetFbxExportTransform(opt);
            var bones = CollectBones(models, mbNodeId, opt);

            var now = DateTime.UtcNow;

            // Header 
            sb.AppendLine("; FBX 7.4.0 project file");
            sb.AppendLine("; Created by ForzaTechStudio");
            sb.AppendLine("; ---------------------------------------------------------------------------");
            sb.AppendLine();
            sb.AppendLine("FBXHeaderExtension:  {");
            sb.AppendLine("\tFBXHeaderVersion: 1003");
            sb.AppendLine("\tFBXVersion: 7400");
            sb.AppendLine("\tCreationTimeStamp:  {");
            sb.AppendLine("\t\tVersion: 1000");
            sb.AppendLine($"\t\tYear: {now.Year}");
            sb.AppendLine($"\t\tMonth: {now.Month}");
            sb.AppendLine($"\t\tDay: {now.Day}");
            sb.AppendLine($"\t\tHour: {now.Hour}");
            sb.AppendLine($"\t\tMinute: {now.Minute}");
            sb.AppendLine($"\t\tSecond: {now.Second}");
            sb.AppendLine($"\t\tMillisecond: {now.Millisecond}");
            sb.AppendLine("\t}");
            sb.AppendLine("\tCreator: \"ForzaTechStudio\"");
            sb.AppendLine("}");
            sb.AppendLine();

            // GlobalSettings 
            sb.AppendLine("GlobalSettings:  {");
            sb.AppendLine("\tVersion: 1000");
            sb.AppendLine("\tProperties70:  {");
            sb.AppendLine("\t\tP: \"UpAxis\", \"int\", \"Integer\", \"\",1");
            sb.AppendLine("\t\tP: \"UpAxisSign\", \"int\", \"Integer\", \"\",1");
            sb.AppendLine("\t\tP: \"FrontAxis\", \"int\", \"Integer\", \"\",2");
            sb.AppendLine("\t\tP: \"FrontAxisSign\", \"int\", \"Integer\", \"\",1");
            sb.AppendLine("\t\tP: \"CoordAxis\", \"int\", \"Integer\", \"\",0");
            sb.AppendLine("\t\tP: \"CoordAxisSign\", \"int\", \"Integer\", \"\",1");
            sb.AppendLine("\t\tP: \"UnitScaleFactor\", \"double\", \"Number\", \"\",100");
            sb.AppendLine("\t}");
            sb.AppendLine("}");
            sb.AppendLine();

            // Documents 
            sb.AppendLine("Documents:  {");
            sb.AppendLine("\tCount: 1");
            sb.AppendLine("\tDocument: 999999999, \"\", \"Scene\" {");
            sb.AppendLine("\t\tRootNode: 0");
            sb.AppendLine("\t}");
            sb.AppendLine("}");
            sb.AppendLine();

            // Definitions 
            int modelCount = allMeshes.Count + models.Count + bones.Count;
            sb.AppendLine("Definitions:  {");
            sb.AppendLine("\tVersion: 100");
            sb.AppendLine($"\tCount: {3 + modelCount + matList.Count + texCount + bones.Count}");
            sb.AppendLine("\tObjectType: \"GlobalSettings\" {"); sb.AppendLine("\t\tCount: 1"); sb.AppendLine("\t}");
            sb.AppendLine($"\tObjectType: \"Model\" {{"); sb.AppendLine($"\t\tCount: {modelCount}"); sb.AppendLine("\t}");
            sb.AppendLine($"\tObjectType: \"Geometry\" {{"); sb.AppendLine($"\t\tCount: {allMeshes.Count}"); sb.AppendLine("\t}");
            sb.AppendLine($"\tObjectType: \"Material\" {{"); sb.AppendLine($"\t\tCount: {matList.Count}"); sb.AppendLine("\t}");
            if (texCount > 0) { sb.AppendLine($"\tObjectType: \"Texture\" {{"); sb.AppendLine($"\t\tCount: {texCount}"); sb.AppendLine("\t}"); }
            if (bones.Count > 0) { sb.AppendLine($"\tObjectType: \"NodeAttribute\" {{"); sb.AppendLine($"\t\tCount: {bones.Count}"); sb.AppendLine("\t}"); }
            sb.AppendLine("}");
            sb.AppendLine();

            // Objects
            sb.AppendLine("Objects:  {");

            for (int mi = 0; mi < models.Count; mi++)
            {
                string nm = SanitiseName(models[mi].ModelBinName);
                sb.AppendLine($"\tModel: {mbNodeId[mi]}, \"Model::{nm}\", \"Null\" {{");
                sb.AppendLine("\t\tVersion: 232");
                sb.AppendLine("\t\tProperties70:  {");
                sb.AppendLine("\t\t\tP: \"RotationActive\", \"bool\", \"\", \"\",1");
                sb.AppendLine("\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
                sb.AppendLine("\t\t\tP: \"ScalingMax\", \"Vector3D\", \"Vector\", \"\",0,0,0");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t\tShading: T");
                sb.AppendLine("\t\tCulling: \"CullingOff\"");
                sb.AppendLine("\t}");
            }

            for (int fi = 0; fi < allMeshes.Count; fi++)
            {
                var (_, _, meshName, data) = allMeshes[fi];
                string sn = SanitiseName(meshName);
                AsciiWriteGeometry(sb, MeshGeoId(fi), sn, data, opt, exportTransform);
                AsciiWriteMeshModel(sb, MeshModelId(fi), sn);
            }

            for (int i = 0; i < matList.Count; i++)
            {
                sb.AppendLine($"\tMaterial: {MatNodeId(i)}, \"Material::{matList[i]}\", \"\" {{");
                sb.AppendLine("\t\tVersion: 102");
                sb.AppendLine("\t\tShadingModel: \"phong\"");
                sb.AppendLine("\t\tMultiLayer: 0");
                sb.AppendLine("\t\tProperties70:  {");
                sb.AppendLine("\t\t\tP: \"AmbientColor\", \"Color\", \"\", \"A\",0,0,0");
                sb.AppendLine("\t\t\tP: \"DiffuseColor\", \"Color\", \"\", \"A\",0.8,0.8,0.8");
                sb.AppendLine("\t\t\tP: \"SpecularColor\", \"Color\", \"\", \"A\",0,0,0");
                sb.AppendLine("\t\t\tP: \"Shininess\", \"double\", \"Number\", \"\",20");
                sb.AppendLine("\t\t\tP: \"Opacity\", \"double\", \"Number\", \"\",1");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t}");
            }

            for (int i = 0; i < matList.Count; i++)
            {
                string? texPath = matTexPaths[i];
                if (texPath == null) continue;
                string texName = matList[i];
                sb.AppendLine($"\tTexture: {TexNodeId(i)}, \"Texture::{texName}\", \"\" {{");
                sb.AppendLine("\t\tType: \"TextureVideoClip\"");
                sb.AppendLine("\t\tVersion: 202");
                sb.AppendLine($"\t\tTextureName: \"Texture::{texName}\"");
                sb.AppendLine("\t\tProperties70:  {");
                sb.AppendLine($"\t\t\tP: \"UseMaterial\", \"bool\", \"\", \"\",1");
                sb.AppendLine("\t\t}");
                var (absPath, relPath) = ResolveTexturePaths(texPath, outputDir);
                sb.AppendLine($"\t\tFileName: \"{absPath}\"");
                sb.AppendLine($"\t\tRelativeFilename: \"{relPath}\"");
                sb.AppendLine("\t}");
            }

            AsciiWriteBones(sb, bones, exportTransform);

            sb.AppendLine("}");
            sb.AppendLine();

            // Connections 
            sb.AppendLine("Connections:  {");
            for (int mi = 0; mi < models.Count; mi++)
                sb.AppendLine($"\tC: \"OO\",{mbNodeId[mi]},0");

            for (int fi = 0; fi < allMeshes.Count; fi++)
            {
                var (_, mbIdx, _, data) = allMeshes[fi];
                long geoId  = MeshGeoId(fi);
                long mdlId  = MeshModelId(fi);
                long parent = mbNodeId[mbIdx];
                string mat  = ObjExportService.ExtractMaterialBaseName(data.MaterialName ?? "default");
                long matId  = MatNodeId(matIndex[mat]);

                sb.AppendLine($"\tC: \"OO\",{geoId},{mdlId}");
                sb.AppendLine($"\tC: \"OO\",{mdlId},{parent}");
                sb.AppendLine($"\tC: \"OO\",{matId},{mdlId}");
            }

            for (int i = 0; i < matList.Count; i++)
            {
                if (matTexPaths[i] == null) continue;
                sb.AppendLine($"\tC: \"OP\",{TexNodeId(i)},{MatNodeId(i)},\"DiffuseColor\"");
            }

            foreach (var b in bones)
            {
                long parentId = b.IsRoot ? b.ParentModelBinId : bones[b.ParentBoneGlobal].Id;
                sb.AppendLine($"\tC: \"OO\",{b.Id},{parentId}");
                sb.AppendLine($"\tC: \"OO\",{b.AttrId},{b.Id}");
            }

            sb.AppendLine("}");
            sb.AppendLine();
            return sb.ToString();
        }

        // ASCII Geometry node 

        private static void AsciiWriteGeometry(StringBuilder sb, long id, string name, ForzaGeometryData data, ExportOptions opt, Matrix4x4 exportTransform)
        {
            var (worldVerts, indices) = ResolveGeometry(data, exportTransform);
            int vc = worldVerts.Length;
            var normals = data.Normals;
            var colors = data.Colors;
            bool hasN = normals != null && normals.Length == vc;
            var uvChannels = GetExportUvChannels(data, opt, vc);
            bool hasColors = opt.IncludeVertexColors && colors != null && colors.Length == vc;

            var rot    = data.GetRotationMatrix();
            bool hasRot  = rot != Matrix4x4.Identity;
            bool hasBone = data.BoneTransform != Matrix4x4.Identity;
            var polygonOrder = BuildFbxPolygonVertexOrder(indices);
            var poly = BuildFbxPolygonVertexIndices(polygonOrder);

            sb.AppendLine($"\tGeometry: {id}, \"Geometry::{name}\", \"Mesh\" {{");

            // Vertices
            sb.AppendLine($"\t\tVertices: *{vc * 3} {{");
            sb.Append("\t\t\ta: ");
            for (int i = 0; i < vc; i++)
            {
                if (i > 0) sb.Append(',');
                var v = worldVerts[i];
                sb.Append(v.X.ToString("F6", CI)); sb.Append(',');
                sb.Append(v.Y.ToString("F6", CI)); sb.Append(',');
                sb.Append(v.Z.ToString("F6", CI));
            }
            sb.AppendLine(); sb.AppendLine("\t\t}");

            // PolygonVertexIndex
            sb.AppendLine($"\t\tPolygonVertexIndex: *{poly.Length} {{");
            sb.Append("\t\t\ta: ");
            for (int i = 0; i < poly.Length; i++) { if (i > 0) sb.Append(','); sb.Append(poly[i]); }
            sb.AppendLine(); sb.AppendLine("\t\t}");

            // Normals
            if (hasN && normals != null)
            {
                sb.AppendLine("\t\tLayerElementNormal: 0 {");
                sb.AppendLine("\t\t\tVersion: 102"); sb.AppendLine("\t\t\tName: \"\"");
                sb.AppendLine("\t\t\tMappingInformationType: \"ByPolygonVertex\"");
                sb.AppendLine("\t\t\tReferenceInformationType: \"Direct\"");
                sb.AppendLine($"\t\t\tNormals: *{indices.Length * 3} {{");
                sb.Append("\t\t\t\ta: ");
                bool first = true;
                for (int i = 0; i < polygonOrder.Length; i++)
                {
                    if (!first) sb.Append(','); first = false;
                    var n = normals[polygonOrder[i]];
                    if (hasRot)  n = Vector3.Normalize(Vector3.TransformNormal(n, rot));
                    if (hasBone) n = Vector3.Normalize(Vector3.TransformNormal(n, data.BoneTransform));
                    n = Vector3.Normalize(Vector3.TransformNormal(n, exportTransform));
                    sb.Append(n.X.ToString("F6", CI)); sb.Append(',');
                    sb.Append(n.Y.ToString("F6", CI)); sb.Append(',');
                    sb.Append(n.Z.ToString("F6", CI));
                }
                sb.AppendLine(); sb.AppendLine("\t\t\t}"); sb.AppendLine("\t\t}");
            }

            // UVs
            for (int ui = 0; ui < uvChannels.Count; ui++)
            {
                var uvChannel = uvChannels[ui];
                sb.AppendLine($"\t\tLayerElementUV: {ui} {{");
                sb.AppendLine("\t\t\tVersion: 101"); sb.AppendLine($"\t\t\tName: \"{uvChannel.Name}\"");
                sb.AppendLine("\t\t\tMappingInformationType: \"ByPolygonVertex\"");
                sb.AppendLine("\t\t\tReferenceInformationType: \"IndexToDirect\"");
                sb.AppendLine($"\t\t\tUV: *{vc * 2} {{");
                sb.Append("\t\t\t\ta: ");
                for (int i = 0; i < vc; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(uvChannel.UVs[i].X.ToString("F6", CI)); sb.Append(',');
                    sb.Append(uvChannel.UVs[i].Y.ToString("F6", CI));
                }
                sb.AppendLine(); sb.AppendLine("\t\t\t}");
                sb.AppendLine($"\t\t\tUVIndex: *{polygonOrder.Length} {{");
                sb.Append("\t\t\t\ta: ");
                for (int i = 0; i < polygonOrder.Length; i++) { if (i > 0) sb.Append(','); sb.Append(polygonOrder[i]); }
                sb.AppendLine(); sb.AppendLine("\t\t\t}"); sb.AppendLine("\t\t}");
            }

            // Vertex colors
            if (hasColors && colors != null)
            {
                sb.AppendLine("\t\tLayerElementColor: 0 {");
                sb.AppendLine("\t\t\tVersion: 101"); sb.AppendLine("\t\t\tName: \"VertexColors\"");
                sb.AppendLine("\t\t\tMappingInformationType: \"ByPolygonVertex\"");
                sb.AppendLine("\t\t\tReferenceInformationType: \"IndexToDirect\"");
                sb.AppendLine($"\t\t\tColors: *{vc * 4} {{");
                sb.Append("\t\t\t\ta: ");
                for (int i = 0; i < vc; i++)
                {
                    if (i > 0) sb.Append(',');
                    var c = colors[i];
                    sb.Append(c.X.ToString("F6", CI)); sb.Append(',');
                    sb.Append(c.Y.ToString("F6", CI)); sb.Append(',');
                    sb.Append(c.Z.ToString("F6", CI)); sb.Append(',');
                    sb.Append(c.W.ToString("F6", CI));
                }
                sb.AppendLine(); sb.AppendLine("\t\t\t}");
                sb.AppendLine($"\t\t\tColorIndex: *{polygonOrder.Length} {{");
                sb.Append("\t\t\t\ta: ");
                for (int i = 0; i < polygonOrder.Length; i++) { if (i > 0) sb.Append(','); sb.Append(polygonOrder[i]); }
                sb.AppendLine(); sb.AppendLine("\t\t\t}"); sb.AppendLine("\t\t}");
            }

            // LayerElementMaterial
            sb.AppendLine("\t\tLayerElementMaterial: 0 {");
            sb.AppendLine("\t\t\tVersion: 101"); sb.AppendLine("\t\t\tName: \"\"");
            sb.AppendLine("\t\t\tMappingInformationType: \"AllSame\"");
            sb.AppendLine("\t\t\tReferenceInformationType: \"IndexToDirect\"");
            sb.AppendLine("\t\t\tMaterials: *1 {"); sb.AppendLine("\t\t\t\ta: 0"); sb.AppendLine("\t\t\t}");
            sb.AppendLine("\t\t}");

            // Layer
            sb.AppendLine("\t\tLayer: 0 {"); sb.AppendLine("\t\t\tVersion: 100");
            if (hasN) { sb.AppendLine("\t\t\tLayerElement:  {"); sb.AppendLine("\t\t\t\tType: \"LayerElementNormal\"");   sb.AppendLine("\t\t\t\tTypedIndex: 0"); sb.AppendLine("\t\t\t}"); }
            for (int ui = 0; ui < uvChannels.Count; ui++) { sb.AppendLine("\t\t\tLayerElement:  {"); sb.AppendLine("\t\t\t\tType: \"LayerElementUV\""); sb.AppendLine($"\t\t\t\tTypedIndex: {ui}"); sb.AppendLine("\t\t\t}"); }
            if (hasColors) { sb.AppendLine("\t\t\tLayerElement:  {"); sb.AppendLine("\t\t\t\tType: \"LayerElementColor\""); sb.AppendLine("\t\t\t\tTypedIndex: 0"); sb.AppendLine("\t\t\t}"); }
            sb.AppendLine("\t\t\tLayerElement:  {"); sb.AppendLine("\t\t\t\tType: \"LayerElementMaterial\""); sb.AppendLine("\t\t\t\tTypedIndex: 0"); sb.AppendLine("\t\t\t}");
            sb.AppendLine("\t\t}");
            sb.AppendLine("\t}");
        }

        private static void AsciiWriteMeshModel(StringBuilder sb, long id, string name)
        {
            sb.AppendLine($"\tModel: {id}, \"Model::{name}\", \"Mesh\" {{");
            sb.AppendLine("\t\tVersion: 232");
            sb.AppendLine("\t\tProperties70:  {");
            sb.AppendLine("\t\t\tP: \"RotationActive\", \"bool\", \"\", \"\",1");
            sb.AppendLine("\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
            sb.AppendLine("\t\t\tP: \"ScalingMax\", \"Vector3D\", \"Vector\", \"\",0,0,0");
            sb.AppendLine("\t\t\tP: \"DefaultAttributeIndex\", \"int\", \"Integer\", \"\",0");
            sb.AppendLine("\t\t}");
            sb.AppendLine("\t\tShading: T");
            sb.AppendLine("\t\tCulling: \"CullingOff\"");
            sb.AppendLine("\t}");
        }


        //  Binary writer  (FBX 7.4 binary)


        private static void ExportBinary(List<ModelBinExportData> models, string outputPath, Dictionary<string, string>? texMap, ExportOptions opt)
        {
            using var ms = new MemoryStream(4 << 20);
            using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                // Magic header
                bw.Write(Encoding.ASCII.GetBytes("Kaydara FBX Binary  "));
                bw.Write((byte)0x00);
                bw.Write((byte)0x1A);
                bw.Write((byte)0x00);
                bw.Write((uint)7400);

                var allMeshes = FlattenMeshes(models);
                var (matList, matIndex) = CollectMaterials(allMeshes);
                var matTexPaths = BuildMatTexPaths(matList, texMap);
                int texCount = matTexPaths.Count(p => p != null);
                var mbNodeId = MakeModelBinIds(models.Count);
                long MeshModelId(int fi) => 200_000L + fi * 2;
                long MeshGeoId(int fi)   => 200_001L + fi * 2;
                long MatNodeId(int mi)   => 900_000L + mi;
                long TexNodeId(int mi)   => 950_000L + mi;
                var exportTransform = GetFbxExportTransform(opt);
                var bones = CollectBones(models, mbNodeId, opt);
                var now = DateTime.UtcNow;

                // FBXHeaderExtension
                BN(bw, "FBXHeaderExtension", null, w =>
                {
                    BN(w, "FBXHeaderVersion", new[] { BP.I32(1003) });
                    BN(w, "FBXVersion",       new[] { BP.I32(7400) });
                    BN(w, "CreationTimeStamp", null, ts =>
                    {
                        BN(ts, "Version",     new[] { BP.I32(1000) });
                        BN(ts, "Year",        new[] { BP.I32(now.Year) });
                        BN(ts, "Month",       new[] { BP.I32(now.Month) });
                        BN(ts, "Day",         new[] { BP.I32(now.Day) });
                        BN(ts, "Hour",        new[] { BP.I32(now.Hour) });
                        BN(ts, "Minute",      new[] { BP.I32(now.Minute) });
                        BN(ts, "Second",      new[] { BP.I32(now.Second) });
                        BN(ts, "Millisecond", new[] { BP.I32(now.Millisecond) });
                    });
                    BN(w, "Creator", new[] { BP.S("ForzaTechStudio") });
                });

                // GlobalSettings
                BN(bw, "GlobalSettings", null, gs =>
                {
                    BN(gs, "Version", new[] { BP.I32(1000) });
                    BN(gs, "Properties70", null, p =>
                    {
                        BP70(p, "UpAxis",         "int",    "Integer", "", BP.I32(1));
                        BP70(p, "UpAxisSign",      "int",    "Integer", "", BP.I32(1));
                        BP70(p, "FrontAxis",       "int",    "Integer", "", BP.I32(2));
                        BP70(p, "FrontAxisSign",   "int",    "Integer", "", BP.I32(1));
                        BP70(p, "CoordAxis",       "int",    "Integer", "", BP.I32(0));
                        BP70(p, "CoordAxisSign",   "int",    "Integer", "", BP.I32(1));
                        BP70(p, "UnitScaleFactor", "double", "Number",  "", BP.D(100.0));
                    });
                });

                // Documents
                BN(bw, "Documents", null, docs =>
                {
                    BN(docs, "Count", new[] { BP.I32(1) });
                    BN(docs, "Document", new[] { BP.I64(999999999L), BP.S("\x00\x01Scene"), BP.S("") }, doc =>
                        BN(doc, "RootNode", new[] { BP.I64(0) }));
                });

                // Definitions
                int modelCount = allMeshes.Count + models.Count + bones.Count;
                BN(bw, "Definitions", null, defs =>
                {
                    BN(defs, "Version", new[] { BP.I32(100) });
                    BN(defs, "Count",   new[] { BP.I32(3 + modelCount + matList.Count + texCount + bones.Count) });
                    BDefType(defs, "GlobalSettings", 1);
                    BDefType(defs, "Model",    modelCount);
                    BDefType(defs, "Geometry", allMeshes.Count);
                    BDefType(defs, "Material", matList.Count);
                    if (texCount > 0) BDefType(defs, "Texture", texCount);
                    if (bones.Count > 0) BDefType(defs, "NodeAttribute", bones.Count);
                });

                // Objects
                BN(bw, "Objects", null, objs =>
                {
                    // ModelBin null nodes
                    for (int mi = 0; mi < models.Count; mi++)
                    {
                        long nid = mbNodeId[mi];
                        string nm = SanitiseName(models[mi].ModelBinName);
                        // Binary FBX: props[1]="name\x00\x01Model" (class is always Model),
                        // props[2] = subtype ("Null"/"Mesh"). elem_name_ensure_class splits on \x00\x01 and asserts class==b'Model'.
                        BN(objs, "Model", new[] { BP.I64(nid), BP.S($"{nm}\x00\x01Model"), BP.S("Null") }, n =>
                        {
                            BN(n, "Version", new[] { BP.I32(232) });
                            BN(n, "Properties70", null, p =>
                            {
                                // Blender's elem_props_get_bool asserts props_type[4]==INT32, so bool P70 must use I32
                                BP70(p, "RotationActive", "bool",     "",       "", BP.I32(1));
                                BP70(p, "InheritType",    "enum",     "",       "", BP.I32(1));
                                BP70(p, "ScalingMax",     "Vector3D", "Vector", "", BP.D(0), BP.D(0), BP.D(0));
                            });
                        });
                    }

                    // Geometry + MeshModel per mesh
                    for (int fi = 0; fi < allMeshes.Count; fi++)
                    {
                        var (_, _, meshName, meshData) = allMeshes[fi];
                        string sn = SanitiseName(meshName);
                        BinWriteGeometry(objs, MeshGeoId(fi), sn, meshData, opt, exportTransform);
                        BN(objs, "Model", new[] { BP.I64(MeshModelId(fi)), BP.S($"{sn}\x00\x01Model"), BP.S("Mesh") }, m =>
                        {
                            BN(m, "Version", new[] { BP.I32(232) });
                            BN(m, "Properties70", null, p =>
                            {
                                BP70(p, "RotationActive",        "bool",     "",       "", BP.I32(1));
                                BP70(p, "InheritType",           "enum",     "",       "", BP.I32(1));
                                BP70(p, "ScalingMax",            "Vector3D", "Vector", "", BP.D(0), BP.D(0), BP.D(0));
                                BP70(p, "DefaultAttributeIndex", "int",      "Integer","", BP.I32(0));
                            });
                        });
                    }

                    // Materials
                    for (int i = 0; i < matList.Count; i++)
                    {
                        long mid = MatNodeId(i);
                        string mn = matList[i];
                        BN(objs, "Material", new[] { BP.I64(mid), BP.S($"{mn}\x00\x01Material"), BP.S("") }, mat =>
                        {
                            BN(mat, "Version",      new[] { BP.I32(102) });
                            BN(mat, "ShadingModel", new[] { BP.S("phong") });
                            BN(mat, "MultiLayer",   new[] { BP.I32(0) });
                            BN(mat, "Properties70", null, p =>
                            {
                                BP70(p, "AmbientColor",  "Color",  "", "A", BP.D(0),   BP.D(0),   BP.D(0));
                                BP70(p, "DiffuseColor",  "Color",  "", "A", BP.D(0.8), BP.D(0.8), BP.D(0.8));
                                BP70(p, "SpecularColor", "Color",  "", "A", BP.D(0),   BP.D(0),   BP.D(0));
                                BP70(p, "Shininess",     "double", "Number", "", BP.D(20));
                                BP70(p, "Opacity",       "double", "Number", "", BP.D(1));
                            });
                        });
                    }

                    // Textures
                    for (int i = 0; i < matList.Count; i++)
                    {
                        string? texPath = matTexPaths[i];
                        if (texPath == null) continue;
                        string tn = matList[i];
                        var (absPath, relPath) = ResolveTexturePaths(texPath, Path.GetDirectoryName(outputPath) ?? string.Empty);
                        BN(objs, "Texture", new[] { BP.I64(TexNodeId(i)), BP.S($"{tn}\x00\x01Texture"), BP.S("") }, tex =>
                        {
                            BN(tex, "Type",         new[] { BP.S("TextureVideoClip") });
                            BN(tex, "Version",      new[] { BP.I32(202) });
                            BN(tex, "TextureName",  new[] { BP.S($"Texture::{tn}") });
                            BN(tex, "Properties70", null, p =>
                                BP70(p, "UseMaterial", "bool", "", "", BP.I32(1)));
                            BN(tex, "FileName",         new[] { BP.S(absPath) });
                            BN(tex, "RelativeFilename", new[] { BP.S(relPath) });
                        });
                    }

                    // Bones (LimbNode skeleton)
                    foreach (var b in bones)
                    {
                        string bn = SanitiseName(b.Bone.Name ?? "bone");
                        BN(objs, "NodeAttribute", new[] { BP.I64(b.AttrId), BP.S($"{bn}\x00\x01NodeAttribute"), BP.S("LimbNode") }, na =>
                        {
                            BN(na, "Properties70", null, p =>
                                BP70(p, "Size", "double", "Number", "", BP.D(1.0)));
                            BN(na, "TypeFlags", new[] { BP.S("Skeleton") });
                        });
                        var (bt, br, bsc) = BoneLcl(b, exportTransform);
                        BN(objs, "Model", new[] { BP.I64(b.Id), BP.S($"{bn}\x00\x01Model"), BP.S("LimbNode") }, m =>
                        {
                            BN(m, "Version", new[] { BP.I32(232) });
                            BN(m, "Properties70", null, p =>
                            {
                                BP70(p, "RotationActive", "bool",     "",       "", BP.I32(1));
                                BP70(p, "InheritType",    "enum",     "",       "", BP.I32(1));
                                BP70(p, "ScalingMax",     "Vector3D", "Vector", "", BP.D(0), BP.D(0), BP.D(0));
                                BP70(p, "Lcl Translation", "Lcl Translation", "", "A", BP.D(bt.X), BP.D(bt.Y), BP.D(bt.Z));
                                BP70(p, "Lcl Rotation",    "Lcl Rotation",    "", "A", BP.D(br.X), BP.D(br.Y), BP.D(br.Z));
                                BP70(p, "Lcl Scaling",     "Lcl Scaling",     "", "A", BP.D(bsc.X), BP.D(bsc.Y), BP.D(bsc.Z));
                            });
                        });
                    }
                });

                // Connections
                BN(bw, "Connections", null, conns =>
                {
                    for (int mi = 0; mi < models.Count; mi++)
                        BN(conns, "C", new[] { BP.S("OO"), BP.I64(mbNodeId[mi]), BP.I64(0) });

                    for (int fi = 0; fi < allMeshes.Count; fi++)
                    {
                        var (_, mbIdx, _, meshData) = allMeshes[fi];
                        long geoId  = MeshGeoId(fi);
                        long mdlId  = MeshModelId(fi);
                        long parent = mbNodeId[mbIdx];
                        string mat  = ObjExportService.ExtractMaterialBaseName(meshData.MaterialName ?? "default");
                        long matId  = MatNodeId(matIndex[mat]);

                        BN(conns, "C", new[] { BP.S("OO"), BP.I64(geoId), BP.I64(mdlId) });
                        BN(conns, "C", new[] { BP.S("OO"), BP.I64(mdlId), BP.I64(parent) });
                        BN(conns, "C", new[] { BP.S("OO"), BP.I64(matId), BP.I64(mdlId) });
                    }

                    for (int i = 0; i < matList.Count; i++)
                    {
                        if (matTexPaths[i] == null) continue;
                        BN(conns, "C", new[] { BP.S("OP"), BP.I64(TexNodeId(i)), BP.I64(MatNodeId(i)), BP.S("DiffuseColor") });
                    }

                    foreach (var b in bones)
                    {
                        long parentId = b.IsRoot ? b.ParentModelBinId : bones[b.ParentBoneGlobal].Id;
                        BN(conns, "C", new[] { BP.S("OO"), BP.I64(b.Id), BP.I64(parentId) });
                        BN(conns, "C", new[] { BP.S("OO"), BP.I64(b.AttrId), BP.I64(b.Id) });
                    }
                });

                // Top-level null sentinel
                bw.Write(new byte[13]);
                WriteBinaryFooter(bw, 7400);
            }

            File.WriteAllBytes(outputPath, ms.ToArray());
        }

        private static void WriteBinaryFooter(BinaryWriter bw, uint version)
        {
            bw.Write(new byte[]
            {
                0xFA, 0xBC, 0xAB, 0x09, 0xD0, 0xC8, 0xD4, 0x66,
                0xB1, 0x76, 0xFB, 0x83, 0x1C, 0xF7, 0x26, 0x7E
            });
            bw.Write((uint)0);
            bw.Write(version);
            bw.Write(new byte[120]);
            bw.Write(new byte[]
            {
                0xF8, 0x5A, 0x8C, 0x6A, 0xDE, 0xF5, 0xD9, 0x7E,
                0xEC, 0xE9, 0x0C, 0xE3, 0x75, 0x8F, 0x29, 0x0B
            });
        }

        // Binary: Geometry node 

        private static void BinWriteGeometry(BinaryWriter bw, long id, string name, ForzaGeometryData data, ExportOptions opt, Matrix4x4 exportTransform)
        {
            var (worldVerts, indices) = ResolveGeometry(data, exportTransform);
            int vc      = worldVerts.Length;
            var normals = data.Normals;
            var colors = data.Colors;
            bool hasN   = normals != null && normals.Length == vc;
            var uvChannels = GetExportUvChannels(data, opt, vc);
            bool hasColors = opt.IncludeVertexColors && colors != null && colors.Length == vc;
            var rot     = data.GetRotationMatrix();
            bool hasRot  = rot != Matrix4x4.Identity;
            bool hasBone = data.BoneTransform != Matrix4x4.Identity;
            var polygonOrder = BuildFbxPolygonVertexOrder(indices);
            var poly    = BuildFbxPolygonVertexIndices(polygonOrder);

            // Flatten vertex positions
            var vArr = new double[vc * 3];
            for (int i = 0; i < vc; i++)
            {
                vArr[i * 3]     = worldVerts[i].X;
                vArr[i * 3 + 1] = worldVerts[i].Y;
                vArr[i * 3 + 2] = worldVerts[i].Z;
            }

            BN(bw, "Geometry", new[] { BP.I64(id), BP.S($"{name}\x00\x01Geometry"), BP.S("Mesh") }, geo =>
            {
                BN(geo, "GeometryVersion", new[] { BP.I32(124) });
                BN(geo, "Vertices",           new[] { BP.DA(vArr) });
                BN(geo, "PolygonVertexIndex", new[] { BP.IA(poly) });

                if (hasN && normals != null)
                {
                    var nArr = new double[polygonOrder.Length * 3];
                    for (int i = 0; i < polygonOrder.Length; i++)
                    {
                        var n = normals[polygonOrder[i]];
                        if (hasRot)  n = Vector3.Normalize(Vector3.TransformNormal(n, rot));
                        if (hasBone) n = Vector3.Normalize(Vector3.TransformNormal(n, data.BoneTransform));
                        n = Vector3.Normalize(Vector3.TransformNormal(n, exportTransform));
                        nArr[i * 3] = n.X; nArr[i * 3 + 1] = n.Y; nArr[i * 3 + 2] = n.Z;
                    }
                    BN(geo, "LayerElementNormal", null, ln =>
                    {
                        BN(ln, "Version", new[] { BP.I32(102) });
                        BN(ln, "Name",    new[] { BP.S("") });
                        BN(ln, "MappingInformationType",   new[] { BP.S("ByPolygonVertex") });
                        BN(ln, "ReferenceInformationType", new[] { BP.S("Direct") });
                        BN(ln, "Normals", new[] { BP.DA(nArr) });
                    });
                }

                for (int ui = 0; ui < uvChannels.Count; ui++)
                {
                    var uvChannel = uvChannels[ui];
                    var uvArr = new double[vc * 2];
                    var uvIdx = new int[polygonOrder.Length];
                    for (int i = 0; i < vc; i++) { uvArr[i * 2] = uvChannel.UVs[i].X; uvArr[i * 2 + 1] = uvChannel.UVs[i].Y; }
                    for (int i = 0; i < polygonOrder.Length; i++) uvIdx[i] = polygonOrder[i];

                    BN(geo, "LayerElementUV", null, lu =>
                    {
                        BN(lu, "Version", new[] { BP.I32(101) });
                        BN(lu, "Name",    new[] { BP.S(uvChannel.Name) });
                        BN(lu, "MappingInformationType",   new[] { BP.S("ByPolygonVertex") });
                        BN(lu, "ReferenceInformationType", new[] { BP.S("IndexToDirect") });
                        BN(lu, "UV",      new[] { BP.DA(uvArr) });
                        BN(lu, "UVIndex", new[] { BP.IA(uvIdx) });
                    });
                }

                if (hasColors && colors != null)
                {
                    var cArr = new double[vc * 4];
                    var cIdx = new int[polygonOrder.Length];
                    for (int i = 0; i < vc; i++)
                    {
                        var c = colors[i];
                        cArr[i * 4] = c.X; cArr[i * 4 + 1] = c.Y; cArr[i * 4 + 2] = c.Z; cArr[i * 4 + 3] = c.W;
                    }
                    for (int i = 0; i < polygonOrder.Length; i++) cIdx[i] = polygonOrder[i];
                    BN(geo, "LayerElementColor", null, lc =>
                    {
                        BN(lc, "Version", new[] { BP.I32(101) });
                        BN(lc, "Name",    new[] { BP.S("VertexColors") });
                        BN(lc, "MappingInformationType",   new[] { BP.S("ByPolygonVertex") });
                        BN(lc, "ReferenceInformationType", new[] { BP.S("IndexToDirect") });
                        BN(lc, "Colors",     new[] { BP.DA(cArr) });
                        BN(lc, "ColorIndex", new[] { BP.IA(cIdx) });
                    });
                }

                BN(geo, "LayerElementMaterial", null, lm =>
                {
                    BN(lm, "Version", new[] { BP.I32(101) });
                    BN(lm, "Name",    new[] { BP.S("") });
                    BN(lm, "MappingInformationType",   new[] { BP.S("AllSame") });
                    BN(lm, "ReferenceInformationType", new[] { BP.S("IndexToDirect") });
                    BN(lm, "Materials", new[] { BP.IA(new[] { 0 }) });
                });

                BN(geo, "Layer", null, layer =>
                {
                    BN(layer, "Version", new[] { BP.I32(100) });
                    if (hasN) BN(layer, "LayerElement", null, le => { BN(le, "Type", new[] { BP.S("LayerElementNormal") });   BN(le, "TypedIndex", new[] { BP.I32(0) }); });
                    for (int ui = 0; ui < uvChannels.Count; ui++) BN(layer, "LayerElement", null, le => { BN(le, "Type", new[] { BP.S("LayerElementUV") }); BN(le, "TypedIndex", new[] { BP.I32(ui) }); });
                    if (hasColors) BN(layer, "LayerElement", null, le => { BN(le, "Type", new[] { BP.S("LayerElementColor") }); BN(le, "TypedIndex", new[] { BP.I32(0) }); });
                    BN(layer, "LayerElement", null, le => { BN(le, "Type", new[] { BP.S("LayerElementMaterial") }); BN(le, "TypedIndex", new[] { BP.I32(0) }); });
                });
            });
        }

        // Binary node writer helpers
        private static void BN(BinaryWriter bw, string name, BP[]? props, Action<BinaryWriter>? children = null)
        {
            props ??= Array.Empty<BP>();
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);

            // Serialise properties into a temporary buffer so we know their total byte length.
            using var propMs = new MemoryStream();
            using var propBw = new BinaryWriter(propMs, Encoding.UTF8, leaveOpen: true);
            foreach (var p in props) p.Write(propBw);
            propBw.Flush();
            byte[] propBytes = propMs.ToArray();

            // Write node header
            long endOffsetPos = bw.BaseStream.Position;
            bw.Write((uint)0);                      // placeholder � patched below
            bw.Write((uint)props.Length);           // NumProperties
            bw.Write((uint)propBytes.Length);       // PropertyListLen
            bw.Write((byte)nameBytes.Length);       // NameLen
            bw.Write(nameBytes);                    // Name
            bw.Write(propBytes);                    // Properties

            // Write nested children directly
            if (children != null)
            {
                children(bw);
                bw.Write(new byte[13]);             // null sentinel closes the child list
            }

            // Back-patch EndOffset with the current absolute stream position
            long endPos = bw.BaseStream.Position;
            bw.BaseStream.Seek(endOffsetPos, SeekOrigin.Begin);
            bw.Write((uint)endPos);
            bw.BaseStream.Seek(endPos, SeekOrigin.Begin);
        }

        private static void BP70(BinaryWriter bw, string propName, string t1, string t2, string flags, params BP[] values)
        {
            var all = new[] { BP.S(propName), BP.S(t1), BP.S(t2), BP.S(flags) }.Concat(values).ToArray();
            BN(bw, "P", all);
        }

        private static void BDefType(BinaryWriter bw, string typeName, int count)
            => BN(bw, "ObjectType", new[] { BP.S(typeName) }, ot => BN(ot, "Count", new[] { BP.I32(count) }));

        // BP: binary property value 

        private readonly struct BP
        {
            private readonly byte   _t;
            private readonly object _v;
            private BP(byte t, object v) { _t = t; _v = v; }

            public static BP I32(int v)      => new BP((byte)'I', v);
            public static BP I64(long v)     => new BP((byte)'L', v);
            public static BP D(double v)     => new BP((byte)'D', v);
            public static BP S(string v)     => new BP((byte)'S', v ?? "");
            public static BP IA(int[] v)     => new BP((byte)'i', v);
            public static BP DA(double[] v)  => new BP((byte)'d', v);

            public void Write(BinaryWriter bw)
            {
                bw.Write(_t);
                switch (_t)
                {
                    case (byte)'I': bw.Write((int)_v);    break;
                    case (byte)'L': bw.Write((long)_v);   break;
                    case (byte)'D': bw.Write((double)_v); break;
                    case (byte)'S':
                    {
                        byte[] b = Encoding.UTF8.GetBytes((string)_v);
                        bw.Write((uint)b.Length);
                        bw.Write(b);
                        break;
                    }
                    case (byte)'i': WriteArr(bw, (int[])_v,    (w, v) => w.Write(v), 4); break;
                    case (byte)'d': WriteArr(bw, (double[])_v, (w, v) => w.Write(v), 8); break;
                }
            }

            // Writes an FBX compressed-array property.
            // Encoding 1 = zlib deflate (used when raw bytes > 256); encoding 0 = raw.
            private static void WriteArr<T>(BinaryWriter bw, T[] arr, Action<BinaryWriter, T> writeFn, int stride)
            {
                using var rawMs = new MemoryStream(arr.Length * stride);
                using var rawBw = new BinaryWriter(rawMs);
                foreach (var v in arr) writeFn(rawBw, v);
                rawBw.Flush();
                byte[] raw = rawMs.ToArray();

                if (raw.Length > 256)
                {
                    // zlib: CMF=0x78, FLG=0x9C (deflate, best compression, valid checksum)
                    using var cmpMs = new MemoryStream(raw.Length);
                    cmpMs.WriteByte(0x78); cmpMs.WriteByte(0x9C);
                    using (var def = new DeflateStream(cmpMs, CompressionLevel.Optimal, leaveOpen: true))
                        def.Write(raw, 0, raw.Length);
                    // Adler-32 (big-endian) required by zlib framing
                    uint a32 = Adler32(raw);
                    cmpMs.WriteByte((byte)(a32 >> 24)); cmpMs.WriteByte((byte)(a32 >> 16));
                    cmpMs.WriteByte((byte)(a32 >> 8));  cmpMs.WriteByte((byte)a32);
                    byte[] cmp = cmpMs.ToArray();

                    bw.Write((uint)arr.Length);
                    bw.Write((uint)1);            // encoding: deflate
                    bw.Write((uint)cmp.Length);
                    bw.Write(cmp);
                }
                else
                {
                    bw.Write((uint)arr.Length);
                    bw.Write((uint)0);            // encoding: raw
                    bw.Write((uint)raw.Length);
                    bw.Write(raw);
                }
            }

            private static uint Adler32(byte[] data)
            {
                const uint MOD = 65521;
                uint a = 1, b = 0;
                foreach (byte x in data) { a = (a + x) % MOD; b = (b + a) % MOD; }
                return (b << 16) | a;
            }
        }


        //  Shared helpers


        private static long[] MakeModelBinIds(int count)
        {
            var ids = new long[count];
            for (int i = 0; i < count; i++) ids[i] = 100_000L + i;
            return ids;
        }

        private static Matrix4x4 GetFbxExportTransform(ExportOptions opt)
            => Matrix4x4.CreateScale(-1f, 1f, 1f) * opt.GetAxisScaleMatrix();

        private static (Vector3[] WorldVerts, int[] Indices) ResolveGeometry(ForzaGeometryData data, Matrix4x4 exportTransform)
        {
            int vc      = data.RawPositions.Length;
            var scale   = data.SourceMesh?.PositionScale     ?? Vector4.One;
            var trans   = data.SourceMesh?.PositionTranslate ?? Vector4.Zero;
            var rot     = data.GetRotationMatrix();
            bool hasRot  = rot != Matrix4x4.Identity;
            bool hasBone = data.BoneTransform != Matrix4x4.Identity;

            var wv = new Vector3[vc];
            for (int i = 0; i < vc; i++)
            {
                var r = data.RawPositions[i];
                var s = new Vector3(r.X * scale.X, r.Y * scale.Y, r.Z * scale.Z);
                if (hasRot) s = Vector3.Transform(s, rot);
                var v = new Vector3(s.X + trans.X, s.Y + trans.Y, s.Z + trans.Z);
                var world = hasBone ? Vector3.Transform(v, data.BoneTransform) : v;
                wv[i] = Vector3.Transform(world, exportTransform);
            }

            int[] idx;
            if (data.Indices != null && data.Indices.Length > 0)
                idx = data.Indices;
            else { idx = new int[vc]; for (int i = 0; i < vc; i++) idx[i] = i; }

            return (wv, idx);
        }

        private static List<(int ChannelIndex, string Name, Vector2[] UVs)> GetExportUvChannels(ForzaGeometryData data, ExportOptions opt, int vertexCount)
        {
            var channels = new List<(int ChannelIndex, string Name, Vector2[] UVs)>();
            if (!opt.IncludeUVs)
                return channels;

            bool IsValid(Vector2[]? uvs) => uvs != null && uvs.Length == vertexCount;

            if (opt.UvMode == ExportUvMode.PrimaryOnly)
            {
                if (data.UvChannels != null && data.UvChannels.TryGetValue(0, out var primary) && IsValid(primary))
                    channels.Add((0, GetUvChannelName(0), primary));
                else if (IsValid(data.UVs))
                    channels.Add((0, GetUvChannelName(0), data.UVs));
                return channels;
            }

            if (data.UvChannels != null)
            {
                foreach (var kv in data.UvChannels.OrderBy(kv => kv.Key))
                    if (IsValid(kv.Value))
                        channels.Add((kv.Key, GetUvChannelName(kv.Key), kv.Value));
            }

            if (channels.Count == 0 && IsValid(data.UVs))
                channels.Add((0, GetUvChannelName(0), data.UVs));

            return channels;
        }

        private static string GetUvChannelName(int channelIndex)
            => channelIndex == 0 ? "UVMap" : $"UVMap_{channelIndex}";

        private static int[] BuildFbxPolygonVertexOrder(int[] indices)
        {
            var order = new int[indices.Length];
            int i = 0;
            for (; i + 2 < indices.Length; i += 3)
            {
                order[i] = indices[i];
                order[i + 1] = indices[i + 2];
                order[i + 2] = indices[i + 1];
            }

            for (; i < indices.Length; i++)
                order[i] = indices[i];

            return order;
        }

        private static int[] BuildFbxPolygonVertexIndices(int[] polygonOrder)
        {
            var p = new int[polygonOrder.Length];
            for (int i = 0; i < polygonOrder.Length; i++)
                p[i] = (i % 3 == 2) ? ~polygonOrder[i] : polygonOrder[i];
            return p;
        }

        private static List<(string ModelBinName, int MBIndex, string MeshName, ForzaGeometryData Data)>
            FlattenMeshes(List<ModelBinExportData> models)
        {
            var list = new List<(string, int, string, ForzaGeometryData)>();
            for (int mi = 0; mi < models.Count; mi++)
                foreach (var (n, d) in models[mi].Meshes)
                    if (d?.RawPositions != null && d.RawPositions.Length > 0)
                        list.Add((models[mi].ModelBinName, mi, n, d));
            return list;
        }

        private static (List<string> MatList, Dictionary<string, int> MatIndex)
            CollectMaterials(List<(string, int, string, ForzaGeometryData Data)> meshes)
        {
            var ml = new List<string>();
            var mi = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, _, _, d) in meshes)
            {
                string m = ObjExportService.ExtractMaterialBaseName(d.MaterialName ?? "default");
                if (!mi.ContainsKey(m)) { mi[m] = ml.Count; ml.Add(m); }
            }
            return (ml, mi);
        }

        private static string SanitiseName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "unnamed";
            var c = name.ToCharArray();
            for (int i = 0; i < c.Length; i++)
                if (char.IsWhiteSpace(c[i]) || c[i] == '#' || c[i] == '\\' || c[i] == '/' || c[i] == ':')
                    c[i] = '_';
            return new string(c);
        }

        // Skeleton bone flattened for FBX emission
        private sealed class FbxBone
        {
            public Bone Bone = null!;
            public long Id;
            public long AttrId;
            public bool IsRoot;
            public int ParentBoneGlobal;   // index into the global bones list
            public long ParentModelBinId;  // root parent = owning ModelBin null node
        }

        private static List<FbxBone> CollectBones(List<ModelBinExportData> models, long[] mbNodeId, ExportOptions opt)
        {
            var bones = new List<FbxBone>();
            if (!opt.IncludeBones) return bones;

            for (int mi = 0; mi < models.Count; mi++)
            {
                var skel = models[mi].Bundle?.Blobs.OfType<SkeletonBlob>().FirstOrDefault();
                if (skel == null || skel.Bones.Count == 0) continue;

                int baseIdx = bones.Count;
                for (int bi = 0; bi < skel.Bones.Count; bi++)
                {
                    int g = bones.Count;
                    var b = skel.Bones[bi];
                    bool isRoot = b.ParentId < 0 || b.ParentId >= skel.Bones.Count;
                    bones.Add(new FbxBone
                    {
                        Bone = b,
                        Id = 700_000L + g * 2,
                        AttrId = 700_001L + g * 2,
                        IsRoot = isRoot,
                        ParentBoneGlobal = isRoot ? -1 : baseIdx + b.ParentId,
                        ParentModelBinId = mbNodeId[mi]
                    });
                }
            }
            return bones;
        }

        // Local TRS for a bone; root bones fold in the axis/scale conversion
        private static (Vector3 T, Vector3 R, Vector3 S) BoneLcl(FbxBone b, Matrix4x4 exportTransform)
        {
            var m = b.IsRoot ? b.Bone.Matrix * exportTransform : b.Bone.Matrix;
            if (!Matrix4x4.Decompose(m, out var scale, out var rot, out var trans))
            {
                scale = Vector3.One; rot = Quaternion.Identity; trans = m.Translation;
            }
            return (trans, QuatToEulerXyz(rot), scale);
        }

        // Quaternion to XYZ Euler angles in degrees
        private static Vector3 QuatToEulerXyz(Quaternion q)
        {
            float sinrCosp = 2f * (q.W * q.X + q.Y * q.Z);
            float cosrCosp = 1f - 2f * (q.X * q.X + q.Y * q.Y);
            float x = MathF.Atan2(sinrCosp, cosrCosp);

            float sinp = 2f * (q.W * q.Y - q.Z * q.X);
            float y = MathF.Abs(sinp) >= 1f ? MathF.CopySign(MathF.PI / 2f, sinp) : MathF.Asin(sinp);

            float sinyCosp = 2f * (q.W * q.Z + q.X * q.Y);
            float cosyCosp = 1f - 2f * (q.Y * q.Y + q.Z * q.Z);
            float z = MathF.Atan2(sinyCosp, cosyCosp);

            const float r2d = 180f / MathF.PI;
            return new Vector3(x * r2d, y * r2d, z * r2d);
        }

        // ASCII bone Model + NodeAttribute nodes
        private static void AsciiWriteBones(StringBuilder sb, List<FbxBone> bones, Matrix4x4 exportTransform)
        {
            foreach (var b in bones)
            {
                string bn = SanitiseName(b.Bone.Name ?? "bone");
                sb.AppendLine($"\tNodeAttribute: {b.AttrId}, \"NodeAttribute::{bn}\", \"LimbNode\" {{");
                sb.AppendLine("\t\tProperties70:  {");
                sb.AppendLine("\t\t\tP: \"Size\", \"double\", \"Number\", \"\",1");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t\tTypeFlags: \"Skeleton\"");
                sb.AppendLine("\t}");

                var (t, r, s) = BoneLcl(b, exportTransform);
                sb.AppendLine($"\tModel: {b.Id}, \"Model::{bn}\", \"LimbNode\" {{");
                sb.AppendLine("\t\tVersion: 232");
                sb.AppendLine("\t\tProperties70:  {");
                sb.AppendLine("\t\t\tP: \"RotationActive\", \"bool\", \"\", \"\",1");
                sb.AppendLine("\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
                sb.AppendLine("\t\t\tP: \"ScalingMax\", \"Vector3D\", \"Vector\", \"\",0,0,0");
                sb.AppendLine($"\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\",{t.X.ToString("F6", CI)},{t.Y.ToString("F6", CI)},{t.Z.ToString("F6", CI)}");
                sb.AppendLine($"\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A\",{r.X.ToString("F6", CI)},{r.Y.ToString("F6", CI)},{r.Z.ToString("F6", CI)}");
                sb.AppendLine($"\t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A\",{s.X.ToString("F6", CI)},{s.Y.ToString("F6", CI)},{s.Z.ToString("F6", CI)}");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t}");
            }
        }

        // Returns an array (parallel to matList) where each element is the DDS
        // relative path from texMap, or null if no texture was found for that material.
        private static string?[] BuildMatTexPaths(List<string> matList, Dictionary<string, string>? texMap)
        {
            var result = new string?[matList.Count];
            if (texMap == null) return result;
            for (int i = 0; i < matList.Count; i++)
                texMap.TryGetValue(matList[i], out result[i]);
            return result;
        }

        // FBX FileName must be an absolute path (forward slashes); RelativeFilename
        // is relative to the FBX file (backslashes). texPath is the relative path.
        private static (string Abs, string Rel) ResolveTexturePaths(string texPath, string outputDir)
        {
            string rel = texPath.Replace('/', '\\');
            string abs = string.IsNullOrEmpty(outputDir)
                ? texPath
                : Path.GetFullPath(Path.Combine(outputDir, rel));
            return (abs.Replace('\\', '/'), rel);
        }
    }
}
