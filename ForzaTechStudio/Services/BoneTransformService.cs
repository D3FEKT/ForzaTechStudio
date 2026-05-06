using System;
using System.Linq;
using System.Numerics;

namespace ForzaTechStudio.Services
{

    // Service for calculating inverse bone transforms to correctly save mesh position edits
    // when meshes are attached to bones in the skeleton hierarchy.

    public static class BoneTransformService
    {
        private const float EPSILON = 0.0001f;


        public static bool IsSignificantBone(short boneIndex)
        {
            // Exclude -1 (no bone), 0 (<root> placeholder), 1 (root/anchor)
            return boneIndex > 1;
        }


        public static (Vector4 newScale, Vector4 newTranslate) CalculateInverseBoneTransform(
            Vector3[] rawPositions,
            Vector3[] editedWorldPositions,
            Matrix4x4 boneTransform)
        {
            if (rawPositions == null || rawPositions.Length == 0)
                throw new ArgumentException("Raw positions cannot be null or empty", nameof(rawPositions));
            
            if (editedWorldPositions == null || editedWorldPositions.Length == 0)
                throw new ArgumentException("Edited positions cannot be null or empty", nameof(editedWorldPositions));
            
            if (rawPositions.Length != editedWorldPositions.Length)
                throw new ArgumentException("Position arrays must have the same length");

            // Step 1: Transform edited world positions back to local space
            var localPositions = WorldToLocalSpace(editedWorldPositions, boneTransform);

            // Step 2: Calculate Scale/Translate that maps raw -> local
            return FitScaleTranslate(rawPositions, localPositions);
        }


        public static bool ValidateTransform(
            Vector3[] rawPositions,
            Vector4 scale,
            Vector4 translate,
            Matrix4x4 boneTransform,
            Vector3[] expectedWorldPositions,
            float tolerance = 0.01f)
        {
            if (rawPositions == null || expectedWorldPositions == null)
                return false;

            if (rawPositions.Length != expectedWorldPositions.Length)
                return false;

            // Forward calculation: raw ? local ? world
            for (int i = 0; i < rawPositions.Length; i++)
            {
                var raw = rawPositions[i];
                
                // Decompress: local = raw * scale + translate
                var local = raw * new Vector3(scale.X, scale.Y, scale.Z) + 
                           new Vector3(translate.X, translate.Y, translate.Z);
                
                // Apply bone transform: world = local * boneMatrix
                var world = Vector3.Transform(local, boneTransform);
                
                var error = Vector3.Distance(world, expectedWorldPositions[i]);
                if (error > tolerance)
                    return false;
            }
            
            return true;
        }

        #region Private Helper Methods

        // Converts world space positions to local space using inverse bone transform.
        private static Vector3[] WorldToLocalSpace(Vector3[] worldPositions, Matrix4x4 boneTransform)
        {
            // Attempt to invert the bone transform
            if (!Matrix4x4.Invert(boneTransform, out var inverseBone))
            {
                // If matrix is not invertible, return a copy of world positions
                // This shouldn't happen with valid bone transforms, but we handle it gracefully
                return (Vector3[])worldPositions.Clone();
            }

            return worldPositions
                .Select(p => Vector3.Transform(p, inverseBone))
                .ToArray();
        }

        // Calculates Scale and Translate parameters that best fit the mapping from
        // raw normalized positions to local space positions using bounding box method.
        private static (Vector4 scale, Vector4 translate) FitScaleTranslate(
            Vector3[] rawPositions,
            Vector3[] localPositions)
        {
            // Calculate bounding boxes
            var rawMin = GetMinBounds(rawPositions);
            var rawMax = GetMaxBounds(rawPositions);
            var localMin = GetMinBounds(localPositions);
            var localMax = GetMaxBounds(localPositions);

            // Calculate extents
            var rawExtent = rawMax - rawMin;
            var localExtent = localMax - localMin;

            // Calculate scale: (localMax - localMin) / (rawMax - rawMin)
            // Protect against division by zero with epsilon checks
            Vector3 scale = new Vector3(
                Math.Abs(rawExtent.X) > EPSILON ? localExtent.X / rawExtent.X : 1.0f,
                Math.Abs(rawExtent.Y) > EPSILON ? localExtent.Y / rawExtent.Y : 1.0f,
                Math.Abs(rawExtent.Z) > EPSILON ? localExtent.Z / rawExtent.Z : 1.0f
            );

            // Clamp scale to reasonable values to avoid degenerate cases
            scale = Vector3.Clamp(scale, new Vector3(-10000f), new Vector3(10000f));

            // Calculate translate: localMin - (rawMin * scale)
            Vector3 translate = localMin - rawMin * scale;

            return (new Vector4(scale, 1.0f), new Vector4(translate, 0.0f));
        }

        // Finds the minimum bounds across all positions.
        private static Vector3 GetMinBounds(Vector3[] positions)
        {
            if (positions == null || positions.Length == 0)
                return Vector3.Zero;

            Vector3 min = new Vector3(float.MaxValue);
            
            foreach (var pos in positions)
            {
                min = Vector3.Min(min, pos);
            }
            
            return min;
        }

        // Finds the maximum bounds across all positions.
        private static Vector3 GetMaxBounds(Vector3[] positions)
        {
            if (positions == null || positions.Length == 0)
                return Vector3.Zero;

            Vector3 max = new Vector3(float.MinValue);
            
            foreach (var pos in positions)
            {
                max = Vector3.Max(max, pos);
            }
            
            return max;
        }

        #endregion
    }
}
