using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Syroot.BinaryData;
using ForzaTools.Bundles;

namespace ForzaTechStudio.Services
{
    public class PhysicsDefinitionParser
    {
        public enum PhysicsDefinitionType : int
        {
            CollidableObject = 0,
            Vehicle = 1,
            Cloth = 2,
        }

        public enum ECollisionShapeType : int
        {
            Sphere = 0,
            Box = 1,
            ConvexHull = 2,
            PointCloud = 5,
        }

        public struct AABB
        {
            public Vector4 Min;
            public Vector4 Max;
            public float Radius;

            public static AABB Read(BinaryStream bs)
            {
                return new AABB
                {
                    Min = bs.ReadVector4(),
                    Max = bs.ReadVector4(),
                    Radius = bs.ReadSingle()
                };
            }

            public void Write(BinaryStream bs)
            {
                bs.WriteVector4(Min);
                bs.WriteVector4(Max);
                bs.WriteSingle(Radius);
            }
        }

        public class PhysicsDefinition
        {
            public uint Version { get; set; } = 26;
            public PhysicsDefinitionType DefinitionType { get; set; } = PhysicsDefinitionType.Vehicle;
            public float Mass { get; set; } = 1500.0f;
            public AABB InertiaTensor { get; set; }
            public AABB InverseInertiaTensor { get; set; }
            public Vector3 HalfExtents { get; set; }
            public float BoundingRadius { get; set; }
            public Vector3 GraphicsOffset { get; set; }
            public int NumChildren { get; set; }
            public Vector3 AabbCentreOffset { get; set; }

            public class PointCloudShape
            {
                public List<Vector3> Points { get; set; } = new List<Vector3>();

                public byte[] Serialize()
                {
                    using (var ms = new MemoryStream())
                    using (var bw = new BinaryWriter(ms))
                    {
                        foreach (var p in Points)
                        {
                            bw.Write(p.X);
                            bw.Write(p.Y);
                            bw.Write(p.Z);
                            bw.Write(0.0f); // Write W padding to ensure 16-byte stride
                        }
                        return ms.ToArray();
                    }
                }
            }

            public class SphereShape
            {
                public float Radius { get; set; }
            }

            public class BoxShape
            {
                public Vector3 HalfExtents { get; set; }
            }

            public class ConvexHullShape
            {
                public List<Vector3> Vertices { get; set; } = new List<Vector3>();
            }

            public class CollisionShape
            {
                public ECollisionShapeType Type { get; set; }
                public PointCloudShape PointCloud { get; set; }
                public SphereShape Sphere { get; set; }
                public BoxShape Box { get; set; }
                public ConvexHullShape ConvexHull { get; set; }
            }
            
            public List<CollisionShape> Shapes { get; set; } = new List<CollisionShape>();
            public List<PhysicsDefinition> Children { get; set; } = new List<PhysicsDefinition>();

            public class UnkBlock
            {
                public int Unk1 { get; set; }
                public byte[] Unk2 { get; set; }
            }
            public List<UnkBlock> UnkBlocks { get; set; } = new List<UnkBlock>();
        }

        public class PhysicsDefinitionList
        {
            public List<PhysicsDefinition> Definitions { get; set; } = new List<PhysicsDefinition>();
            public ulong MaxGlobalIndex { get; set; }
        }

        public PhysicsDefinitionList Parse(Stream stream)
        {
            var list = new PhysicsDefinitionList();
            using (var bs = new BinaryStream(stream))
            {
                ulong numDefinitions = bs.ReadUInt64();
                
                for (ulong i = 0; i < numDefinitions; i++)
                {
                    var def = ReadDefinition(bs);

                    if (def.Version >= 24)
                    {
                        uint unkLength = bs.ReadUInt32();
                        for (uint j = 0; j < unkLength; j++)
                        {
                            var unk = new PhysicsDefinition.UnkBlock();
                            unk.Unk1 = bs.ReadInt32();
                            int unk2Length = bs.ReadInt32();
                            unk.Unk2 = bs.ReadBytes(unk2Length);
                            def.UnkBlocks.Add(unk);
                        }
                    }
                    
                    list.Definitions.Add(def);
                }

                if (list.Definitions.Count > 0 && list.Definitions[0].Version >= 11)
                {
                    list.MaxGlobalIndex = bs.ReadUInt64();
                }
            }
            return list;
        }

        private PhysicsDefinition ReadDefinition(BinaryStream bs)
        {
            var def = new PhysicsDefinition();

            // CPhysicsDefinition structure
            def.Version = bs.ReadUInt32();

            if (def.Version >= 8)
            {
                def.DefinitionType = (PhysicsDefinitionType)bs.ReadInt32();
            }

            def.Mass = bs.ReadSingle();

            def.InertiaTensor = AABB.Read(bs);
            def.InverseInertiaTensor = AABB.Read(bs);

            def.HalfExtents = bs.ReadVector3();
            def.BoundingRadius = bs.ReadSingle();
            def.GraphicsOffset = bs.ReadVector3();
            def.NumChildren = bs.ReadInt32();

            if (def.Version >= 12)
            {
                def.AabbCentreOffset = bs.ReadVector3();
            }

            // SerializeShapes
            int numCollisionShapes = bs.ReadInt32();

            for (int s = 0; s < numCollisionShapes; s++)
            {
                var shape = new PhysicsDefinition.CollisionShape();
                shape.Type = (ECollisionShapeType)bs.ReadInt32();

                if (shape.Type == ECollisionShapeType.PointCloud)
                {
                    int pointCloudSize = bs.ReadInt32();
                    byte[] pointCloudData = bs.ReadBytes(pointCloudSize);

                    var pointCloud = new PhysicsDefinition.PointCloudShape();

                    using (var ms = new MemoryStream(pointCloudData))
                    using (var br = new BinaryReader(ms))
                    {
                        if (pointCloudSize > 0 && pointCloudSize % 16 == 0)
                        {
                            int pointCount = pointCloudSize / 16;
                            for (int p = 0; p < pointCount; p++)
                            {
                                float x = br.ReadSingle();
                                float y = br.ReadSingle();
                                float z = br.ReadSingle();
                                br.ReadSingle();
                                pointCloud.Points.Add(new Vector3(x, y, z));
                            }
                        }
                        else if (pointCloudSize > 0 && pointCloudSize % 12 == 0)
                        {
                            int pointCount = pointCloudSize / 12;
                            for (int p = 0; p < pointCount; p++)
                            {
                                float x = br.ReadSingle();
                                float y = br.ReadSingle();
                                float z = br.ReadSingle();
                                pointCloud.Points.Add(new Vector3(x, y, z));
                            }
                        }
                    }
                    shape.PointCloud = pointCloud;
                }
                else if (shape.Type == ECollisionShapeType.Sphere)
                {
                    shape.Sphere = new PhysicsDefinition.SphereShape { Radius = bs.ReadSingle() };
                }
                else if (shape.Type == ECollisionShapeType.Box)
                {
                    shape.Box = new PhysicsDefinition.BoxShape { HalfExtents = bs.ReadVector3() };
                }
                else if (shape.Type == ECollisionShapeType.ConvexHull)
                {
                    int numVertices = bs.ReadInt32();
                    shape.ConvexHull = new PhysicsDefinition.ConvexHullShape();
                    for (int v = 0; v < numVertices; v++)
                    {
                        shape.ConvexHull.Vertices.Add(bs.ReadVector3());
                    }
                }
                def.Shapes.Add(shape);
            }

            // Children
            for (int i = 0; i < def.NumChildren; i++)
            {
                def.Children.Add(ReadDefinition(bs));
            }
            return def;
        }

        public void Serialize(Stream stream, PhysicsDefinitionList list)
        {
            using (var bs = new BinaryStream(stream))
            {
                bs.WriteUInt64((ulong)list.Definitions.Count);
                foreach (var def in list.Definitions)
                {
                    WriteDefinition(bs, def);

                    if (def.Version >= 24)
                    {
                        bs.WriteUInt32((uint)def.UnkBlocks.Count);
                        foreach (var unk in def.UnkBlocks)
                        {
                            bs.WriteInt32(unk.Unk1);
                            bs.WriteInt32(unk.Unk2.Length);
                            bs.WriteBytes(unk.Unk2);
                        }
                    }
                }

                if (list.Definitions.Count > 0 && list.Definitions[0].Version >= 11)
                {
                    bs.WriteUInt64(list.MaxGlobalIndex);
                }
            }
        }

        private void WriteDefinition(BinaryStream bs, PhysicsDefinition def)
        {
            bs.WriteUInt32(def.Version);
            if (def.Version >= 8)
            {
                bs.WriteInt32((int)def.DefinitionType);
            }
            bs.WriteSingle(def.Mass);
            def.InertiaTensor.Write(bs);
            def.InverseInertiaTensor.Write(bs);
            bs.WriteVector3(def.HalfExtents);
            bs.WriteSingle(def.BoundingRadius);
            bs.WriteVector3(def.GraphicsOffset);
            bs.WriteInt32(def.Children.Count);
            if (def.Version >= 12)
            {
                bs.WriteVector3(def.AabbCentreOffset);
            }

            bs.WriteInt32(def.Shapes.Count);
            foreach (var shape in def.Shapes)
            {
                bs.WriteInt32((int)shape.Type);
                if (shape.Type == ECollisionShapeType.PointCloud && shape.PointCloud != null)
                {
                    byte[] data = shape.PointCloud.Serialize();
                    bs.WriteInt32(data.Length);
                    bs.WriteBytes(data);
                }
                else if (shape.Type == ECollisionShapeType.Sphere && shape.Sphere != null)
                {
                    bs.WriteSingle(shape.Sphere.Radius);
                }
                else if (shape.Type == ECollisionShapeType.Box && shape.Box != null)
                {
                    bs.WriteVector3(shape.Box.HalfExtents);
                }
                else if (shape.Type == ECollisionShapeType.ConvexHull && shape.ConvexHull != null)
                {
                    bs.WriteInt32(shape.ConvexHull.Vertices.Count);
                    foreach (var v in shape.ConvexHull.Vertices)
                    {
                        bs.WriteVector3(v);
                    }
                }
            }

            foreach (var child in def.Children)
            {
                WriteDefinition(bs, child);
            }
        }
    }
}
