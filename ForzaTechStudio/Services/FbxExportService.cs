using System;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text;

namespace ForzaTechStudio.Services
{
    public enum FbxExportFormat { Ascii, Binary }

    // Writes FBX 7.4 ASCII or Binary from Modelbin export data.

    public static class FbxExportService
    {
        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        // Public entry point 

        public static void Export(IEnumerable<ModelBinExportData> models, string outputPath, FbxExportFormat format,
            Dictionary<string, string>? texMap = null)
        {
            var list = models.ToList();
            if (format == FbxExportFormat.Binary)
                ExportBinary(list, outputPath, texMap);
            else
                ExportAscii(list, outputPath, texMap);
        }


        //  ASCII writer


        private static void ExportAscii(List<ModelBinExportData> models, string outputPath, Dictionary<string, string>? texMap)
            => File.WriteAllText(outputPath, BuildFbxAscii(models, texMap), new UTF8Encoding(false));

        private static string BuildFbxAscii(List<ModelBinExportData> models, Dictionary<string, string>? texMap)
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
            int modelCount = allMeshes.Count + models.Count;
            sb.AppendLine("Definitions:  {");
            sb.AppendLine("\tVersion: 100");
            sb.AppendLine($"\tCount: {3 + modelCount + matList.Count + texCount}");
            sb.AppendLine("\tObjectType: \"GlobalSettings\" {"); sb.AppendLine("\t\tCount: 1"); sb.AppendLine("\t}");
            sb.AppendLine($"\tObjectType: \"Model\" {{"); sb.AppendLine($"\t\tCount: {modelCount}"); sb.AppendLine("\t}");
            sb.AppendLine($"\tObjectType: \"Geometry\" {{"); sb.AppendLine($"\t\tCount: {allMeshes.Count}"); sb.AppendLine("\t}");
            sb.AppendLine($"\tObjectType: \"Material\" {{"); sb.AppendLine($"\t\tCount: {matList.Count}"); sb.AppendLine("\t}");
            if (texCount > 0) { sb.AppendLine($"\tObjectType: \"Texture\" {{"); sb.AppendLine($"\t\tCount: {texCount}"); sb.AppendLine("\t}"); }
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
                AsciiWriteGeometry(sb, MeshGeoId(fi), sn, data);
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
                string absPath = texPath.Replace('\\', '/');
                string relPath = texPath.Replace('/', '\\');
                sb.AppendLine($"\t\tFileName: \"{absPath}\"");
                sb.AppendLine($"\t\tRelativeFilename: \"{relPath}\"");
                sb.AppendLine("\t}");
            }

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

            sb.AppendLine("}");
            sb.AppendLine();
            return sb.ToString();
        }

        // ASCII Geometry node 

        private static void AsciiWriteGeometry(StringBuilder sb, long id, string name, ForzaGeometryData data)
        {
            var (worldVerts, indices) = ResolveGeometry(data);
            int vc = worldVerts.Length;
            bool hasN = data.Normals != null && data.Normals.Length == vc;
            bool hasU = data.UVs     != null && data.UVs.Length     == vc;

            var rot    = data.GetRotationMatrix();
            bool hasRot  = rot != Matrix4x4.Identity;
            bool hasBone = data.BoneTransform != Matrix4x4.Identity;
            var poly = BuildPolyIdx(indices);

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
            if (hasN)
            {
                sb.AppendLine("\t\tLayerElementNormal: 0 {");
                sb.AppendLine("\t\t\tVersion: 102"); sb.AppendLine("\t\t\tName: \"\"");
                sb.AppendLine("\t\t\tMappingInformationType: \"ByPolygonVertex\"");
                sb.AppendLine("\t\t\tReferenceInformationType: \"Direct\"");
                sb.AppendLine($"\t\t\tNormals: *{indices.Length * 3} {{");
                sb.Append("\t\t\t\ta: ");
                bool first = true;
                for (int i = 0; i < indices.Length; i++)
                {
                    if (!first) sb.Append(','); first = false;
                    var n = data.Normals[indices[i]];
                    if (hasRot)  n = Vector3.Normalize(Vector3.TransformNormal(n, rot));
                    if (hasBone) n = Vector3.Normalize(Vector3.TransformNormal(n, data.BoneTransform));
                    sb.Append(n.X.ToString("F6", CI)); sb.Append(',');
                    sb.Append(n.Y.ToString("F6", CI)); sb.Append(',');
                    sb.Append(n.Z.ToString("F6", CI));
                }
                sb.AppendLine(); sb.AppendLine("\t\t\t}"); sb.AppendLine("\t\t}");
            }

            // UVs
            if (hasU)
            {
                sb.AppendLine("\t\tLayerElementUV: 0 {");
                sb.AppendLine("\t\t\tVersion: 101"); sb.AppendLine("\t\t\tName: \"UVMap\"");
                sb.AppendLine("\t\t\tMappingInformationType: \"ByPolygonVertex\"");
                sb.AppendLine("\t\t\tReferenceInformationType: \"IndexToDirect\"");
                sb.AppendLine($"\t\t\tUV: *{vc * 2} {{");
                sb.Append("\t\t\t\ta: ");
                for (int i = 0; i < vc; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(data.UVs[i].X.ToString("F6", CI)); sb.Append(',');
                    sb.Append(data.UVs[i].Y.ToString("F6", CI));
                }
                sb.AppendLine(); sb.AppendLine("\t\t\t}");
                sb.AppendLine($"\t\t\tUVIndex: *{indices.Length} {{");
                sb.Append("\t\t\t\ta: ");
                for (int i = 0; i < indices.Length; i++) { if (i > 0) sb.Append(','); sb.Append(indices[i]); }
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
            if (hasU) { sb.AppendLine("\t\t\tLayerElement:  {"); sb.AppendLine("\t\t\t\tType: \"LayerElementUV\"");        sb.AppendLine("\t\t\t\tTypedIndex: 0"); sb.AppendLine("\t\t\t}"); }
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


        private static void ExportBinary(List<ModelBinExportData> models, string outputPath, Dictionary<string, string>? texMap)
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
                int modelCount = allMeshes.Count + models.Count;
                BN(bw, "Definitions", null, defs =>
                {
                    BN(defs, "Version", new[] { BP.I32(100) });
                    BN(defs, "Count",   new[] { BP.I32(3 + modelCount + matList.Count + texCount) });
                    BDefType(defs, "GlobalSettings", 1);
                    BDefType(defs, "Model",    modelCount);
                    BDefType(defs, "Geometry", allMeshes.Count);
                    BDefType(defs, "Material", matList.Count);
                    if (texCount > 0) BDefType(defs, "Texture", texCount);
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
                        BinWriteGeometry(objs, MeshGeoId(fi), sn, meshData);
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
                        string absPath = texPath.Replace('\\', '/');
                        string relPath = texPath.Replace('/', '\\');
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
                });

                // Top-level null sentinel
                bw.Write(new byte[13]);
            }

            File.WriteAllBytes(outputPath, ms.ToArray());
        }

        // Binary: Geometry node 

        private static void BinWriteGeometry(BinaryWriter bw, long id, string name, ForzaGeometryData data)
        {
            var (worldVerts, indices) = ResolveGeometry(data);
            int vc      = worldVerts.Length;
            bool hasN   = data.Normals != null && data.Normals.Length == vc;
            bool hasU   = data.UVs     != null && data.UVs.Length     == vc;
            var rot     = data.GetRotationMatrix();
            bool hasRot  = rot != Matrix4x4.Identity;
            bool hasBone = data.BoneTransform != Matrix4x4.Identity;
            var poly    = BuildPolyIdx(indices);

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

                if (hasN)
                {
                    var nArr = new double[indices.Length * 3];
                    for (int i = 0; i < indices.Length; i++)
                    {
                        var n = data.Normals[indices[i]];
                        if (hasRot)  n = Vector3.Normalize(Vector3.TransformNormal(n, rot));
                        if (hasBone) n = Vector3.Normalize(Vector3.TransformNormal(n, data.BoneTransform));
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

                if (hasU)
                {
                    var uvArr = new double[vc * 2];
                    var uvIdx = new int[indices.Length];
                    for (int i = 0; i < vc; i++) { uvArr[i * 2] = data.UVs[i].X; uvArr[i * 2 + 1] = data.UVs[i].Y; }
                    for (int i = 0; i < indices.Length; i++) uvIdx[i] = indices[i];

                    BN(geo, "LayerElementUV", null, lu =>
                    {
                        BN(lu, "Version", new[] { BP.I32(101) });
                        BN(lu, "Name",    new[] { BP.S("UVMap") });
                        BN(lu, "MappingInformationType",   new[] { BP.S("ByPolygonVertex") });
                        BN(lu, "ReferenceInformationType", new[] { BP.S("IndexToDirect") });
                        BN(lu, "UV",      new[] { BP.DA(uvArr) });
                        BN(lu, "UVIndex", new[] { BP.IA(uvIdx) });
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
                    if (hasU) BN(layer, "LayerElement", null, le => { BN(le, "Type", new[] { BP.S("LayerElementUV") });        BN(le, "TypedIndex", new[] { BP.I32(0) }); });
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
            bw.Write((uint)0);                      // placeholder — patched below
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

        private static (Vector3[] WorldVerts, int[] Indices) ResolveGeometry(ForzaGeometryData data)
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
                wv[i] = hasBone ? Vector3.Transform(v, data.BoneTransform) : v;
            }

            int[] idx;
            if (data.Indices != null && data.Indices.Length > 0)
                idx = data.Indices;
            else { idx = new int[vc]; for (int i = 0; i < vc; i++) idx[i] = i; }

            return (wv, idx);
        }

        private static int[] BuildPolyIdx(int[] indices)
        {
            var p = new int[indices.Length];
            for (int i = 0; i < indices.Length; i++)
                p[i] = (i % 3 == 2) ? ~indices[i] : indices[i];
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
    }
}
