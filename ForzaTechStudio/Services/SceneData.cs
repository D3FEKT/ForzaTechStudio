using System.Collections.Generic;
using System.Numerics;

namespace ForzaTechStudio.Services
{
    public enum CoordinateAxis
    {
        PositiveX, NegativeX,
        PositiveY, NegativeY,
        PositiveZ, NegativeZ
    }

    public class SceneGroup
    {
        public string Name { get; set; }
        public string MaterialName { get; set; }
        public List<int> Indices { get; set; } = new();
    }

    public class SceneData
    {
        public string Name { get; set; }
        public string MaterialLib { get; set; }
        public Vector3[] Positions { get; set; }
        public Vector3[] Normals { get; set; }
        public Vector2[][] UVChannels { get; set; }  // [channel][vertex], channel 0 = primary
        public Vector4[] Tangents { get; set; }  // W component = handedness
        public Vector4[] Colors { get; set; }    // Vertex colors (RGBA)
        public List<SceneGroup> Groups { get; set; } = new();
    }

    public class ImportSettings
    {
        public CoordinateAxis ForwardAxis { get; set; } = CoordinateAxis.NegativeZ;
        public CoordinateAxis UpAxis { get; set; } = CoordinateAxis.PositiveY;
    }
}