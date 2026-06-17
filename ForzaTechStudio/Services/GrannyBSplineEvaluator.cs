using System;
using System.Numerics;

namespace ForzaTechStudio.Services
{
    // Evaluates Granny2 B-spline animation curves (degrees 0-3), handling identity, constant, and compressed formats.
    public static class GrannyBSplineEvaluator
    {
        //  High-level: sample a complete transform track at time t

        // Samples all three curves (position, orientation, scale/shear) of a transform
        // track at the given time, returning the local-space transform components.
        public static void SampleTransformTrack(
            GrannyTransformTrack track, float time,
            out Vector3 position, out Quaternion orientation,
            out Vector3 scale, out float[]? scaleShear9)
        {
            position = Vector3.Zero;
            orientation = Quaternion.Identity;
            scale = Vector3.One;
            scaleShear9 = null;

            var posCurve = track.PositionCurve;
            var oriCurve = track.OrientationCurve;
            var ssCurve  = track.ScaleShearCurve;

            // Treat curves with no parsed control data as identity (guards against parse failures).
            bool posIsId = posCurve == null || posCurve.IsIdentity || !posCurve.HasData;
            bool oriIsId = oriCurve == null || oriCurve.IsIdentity || !oriCurve.HasData;
            bool ssIsId  = ssCurve  == null || ssCurve.IsIdentity  || !ssCurve.HasData;

            if (!posIsId && posCurve != null && posCurve.Controls.Length >= 3)
            {
                Span<float> buf = stackalloc float[3];
                EvaluateCurve(posCurve, time, buf);
                position = new Vector3(buf[0], buf[1], buf[2]);
            }

            if (!oriIsId && oriCurve != null && oriCurve.Controls.Length >= 4)
            {
                Span<float> buf = stackalloc float[4];
                EvaluateCurve(oriCurve, time, buf);
                var rawQ = new Quaternion(buf[0], buf[1], buf[2], buf[3]);
                orientation = rawQ.LengthSquared() > 1e-12f ? Quaternion.Normalize(rawQ) : Quaternion.Identity;
            }

            if (!ssIsId && ssCurve != null && ssCurve.Controls.Length >= 3)
            {
                int dim = ssCurve.Dimension > 0 ? ssCurve.Dimension : 3;
                Span<float> buf = stackalloc float[dim];
                EvaluateCurve(ssCurve, time, buf);

                if (dim == 9)
                {
                    scaleShear9 = new float[9];
                    for (int i = 0; i < 9; i++) scaleShear9[i] = buf[i];
                    scale = new Vector3(buf[0], buf[4], buf[8]); // diagonal
                }
                else
                {
                    scale = new Vector3(buf[0],
                                        dim > 1 ? buf[1] : 1f,
                                        dim > 2 ? buf[2] : 1f);
                }
            }
        }

        //  Core: evaluate a single curve at time t

        // Evaluates a Granny curve at time t; O(1) for identity/constant, O(log n + degree^2) for animated B-splines.
        public static void EvaluateCurve(GrannyCurveInfo curve, float time, Span<float> result)
        {
            int dim = curve.Dimension > 0 ? curve.Dimension : result.Length;
            if (dim > result.Length) dim = result.Length;

            // Fast path: Identity
            if (curve.IsIdentity || curve.Controls == null || curve.Controls.Length == 0)
            {
                WriteIdentity(result, dim);
                return;
            }

            // Fast path: Constant
            if (curve.IsConstant || curve.Knots == null || curve.Knots.Length == 0)
            {
                int avail = Math.Min(dim, curve.Controls.Length);
                for (int i = 0; i < avail; i++) result[i] = curve.Controls[i];
                for (int i = avail; i < dim; i++) result[i] = 0f;
                return;
            }

            int degree = curve.Degree;
            int knotCount = curve.Knots.Length;
            int ctrlCount = curve.Controls.Length / dim;

            // Sanity: need at least degree+1 control points for B-spline
            if (ctrlCount < degree + 1 || knotCount < 1)
            {
                // Degenerate — return first control point
                int avail = Math.Min(dim, curve.Controls.Length);
                for (int i = 0; i < avail; i++) result[i] = curve.Controls[i];
                for (int i = avail; i < dim; i++) result[i] = 0f;
                return;
            }

            // Degree 0: piecewise constant (step function)
            if (degree == 0)
            {
                int idx = FindKnotSpan(curve.Knots, time);
                idx = Math.Clamp(idx, 0, ctrlCount - 1);
                int baseIdx = idx * dim;
                for (int i = 0; i < dim; i++)
                    result[i] = baseIdx + i < curve.Controls.Length ? curve.Controls[baseIdx + i] : 0f;
                return;
            }

            // Degree 1+: B-spline evaluation using knot-span search and De Boor's algorithm.
            EvaluateBSpline(curve, time, dim, degree, result);
        }

        //  B-spline evaluation core

        private static void EvaluateBSpline(GrannyCurveInfo curve, float time, int dim, int degree, Span<float> result)
        {
            float[] knots = curve.Knots;
            float[] controls = curve.Controls;
            int knotCount = knots.Length;
            int ctrlCount = controls.Length / dim;

            // Clamp degree: Granny's max meaningful animation degree is 3 (cubic).
            // Guards against garbage byte values from mis-parsed CurveDataHeaders.
            if (degree < 1) degree = 1;
            if (degree > 3) degree = 3;

            // Clamp time to valid range
            float tMin = knots[0];
            float tMax = knots[knotCount - 1];
            if (time <= tMin) time = tMin;
            else if (time >= tMax) time = tMax - 1e-7f; // nudge slightly inside

            // Find the knot span index: the last knot[i] where knot[i] <= time
            int spanIdx = FindKnotSpan(knots, time);

            // Gather (degree+1) control points around the span, clamping boundary indices.

            switch (degree)
            {
                case 1:
                    EvaluateLinear(knots, controls, dim, spanIdx, ctrlCount, time, result);
                    break;
                case 2:
                    EvaluateQuadratic(knots, controls, dim, spanIdx, knotCount, ctrlCount, time, result);
                    break;
                case 3:
                    EvaluateCubic(knots, controls, dim, spanIdx, knotCount, ctrlCount, time, result);
                    break;
                default:
                    // Fallback for higher degrees: use generic recursive evaluation
                    EvaluateGeneric(knots, controls, dim, degree, spanIdx, knotCount, ctrlCount, time, result);
                    break;
            }
        }

        // Degree 1: Linear interpolation between two adjacent control points.
        // Equivalent to Lerp. Matches SDK's LinearCoefficients.
        private static void EvaluateLinear(float[] knots, float[] controls, int dim,
            int spanIdx, int ctrlCount, float time, Span<float> result)
        {
            int i0 = Math.Clamp(spanIdx, 0, ctrlCount - 1);
            int i1 = Math.Clamp(spanIdx + 1, 0, ctrlCount - 1);

            float t0 = GetKnot(knots, spanIdx);
            float t1 = GetKnot(knots, spanIdx + 1);
            float range = t1 - t0;
            float alpha = range > 1e-7f ? (time - t0) / range : 0f;

            int b0 = i0 * dim, b1 = i1 * dim;
            for (int d = 0; d < dim; d++)
            {
                float c0 = GetControl(controls, b0 + d);
                float c1 = GetControl(controls, b1 + d);
                result[d] = c0 + (c1 - c0) * alpha;
            }
        }

        // Degree 2: Quadratic B-spline. Uses 3 control points and 4 knots.
        private static void EvaluateQuadratic(float[] knots, float[] controls, int dim,
            int spanIdx, int knotCount, int ctrlCount, float time, Span<float> result)
        {
            // Need knots: ti-2, ti-1, ti, ti+1  and controls: ci-2, ci-1, ci
            float ti_2 = GetKnot(knots, spanIdx - 2, knotCount);
            float ti_1 = GetKnot(knots, spanIdx - 1, knotCount);
            float ti   = GetKnot(knots, spanIdx,     knotCount);
            float ti1  = GetKnot(knots, spanIdx + 1, knotCount);

            // Quadratic basis coefficients
            float dL0   = ti - ti_1;
            float dL1_1 = ti - ti_2;
            float dL1_2 = ti1 - ti_1;

            float L0   = SafeDiv(time - ti_1, dL0);
            float L1_1 = SafeDiv(time - ti_2, dL1_1);
            float L1_2 = SafeDiv(time - ti_1, dL1_2);

            float mL0   = 1f - L0;
            float mL1_1 = 1f - L1_1;
            float mL1_2 = 1f - L1_2;

            float c2 = mL0 * mL1_1;
            float c1 = mL0 * L1_1 + L0 * mL1_2;
            float c0 = L0 * L1_2;

            int ci2 = Math.Clamp(spanIdx - 2, 0, ctrlCount - 1) * dim;
            int ci1 = Math.Clamp(spanIdx - 1, 0, ctrlCount - 1) * dim;
            int ci0 = Math.Clamp(spanIdx,     0, ctrlCount - 1) * dim;

            for (int d = 0; d < dim; d++)
            {
                result[d] = c2 * GetControl(controls, ci2 + d)
                          + c1 * GetControl(controls, ci1 + d)
                          + c0 * GetControl(controls, ci0 + d);
            }
        }

        // Degree 3: Cubic B-spline. Uses 4 control points and 6 knots.
        // Most common curve type in Granny animations.
        private static void EvaluateCubic(float[] knots, float[] controls, int dim,
            int spanIdx, int knotCount, int ctrlCount, float time, Span<float> result)
        {
            // Need knots: ti-3, ti-2, ti-1, ti, ti+1, ti+2
            float ti_3 = GetKnot(knots, spanIdx - 3, knotCount);
            float ti_2 = GetKnot(knots, spanIdx - 2, knotCount);
            float ti_1 = GetKnot(knots, spanIdx - 1, knotCount);
            float ti   = GetKnot(knots, spanIdx,     knotCount);
            float ti1  = GetKnot(knots, spanIdx + 1, knotCount);
            float ti2  = GetKnot(knots, spanIdx + 2, knotCount);

            // Cubic basis coefficients — matches SDK's CubicCoefficients exactly
            float tmti_1 = time - ti_1;
            float tmti_2 = time - ti_2;
            float tmti_3 = time - ti_3;

            float dL0    = ti  - ti_1;
            float dL1_1  = ti  - ti_2;
            float dL1_2  = ti1 - ti_1;
            float dL2_1  = ti  - ti_3;
            float dL2_2  = ti1 - ti_2;
            float dL2_3  = ti2 - ti_1;

            float L0   = SafeDiv(tmti_1, dL0);
            float L1_1 = SafeDiv(tmti_2, dL1_1);
            float L1_2 = SafeDiv(tmti_1, dL1_2);
            float L2_1 = SafeDiv(tmti_3, dL2_1);
            float L2_2 = SafeDiv(tmti_2, dL2_2);
            float L2_3 = SafeDiv(tmti_1, dL2_3);

            float mL0   = 1f - L0;
            float mL1_1 = 1f - L1_1;
            float mL1_2 = 1f - L1_2;
            float mL2_1 = 1f - L2_1;
            float mL2_2 = 1f - L2_2;
            float mL2_3 = 1f - L2_3;

            float mL0mL1_1 = mL0 * mL1_1;
            float mL0L1_1  = mL0 * L1_1;
            float L0mL1_2  = L0 * mL1_2;
            float L0L1_2   = L0 * L1_2;

            float c3 = mL0mL1_1 * mL2_1;
            float c2 = mL0mL1_1 * L2_1 + mL0L1_1 * mL2_2 + L0mL1_2 * mL2_2;
            float c1 = mL0L1_1 * L2_2 + L0mL1_2 * L2_2 + L0L1_2 * mL2_3;
            float c0 = L0L1_2 * L2_3;

            int ci3 = Math.Clamp(spanIdx - 3, 0, ctrlCount - 1) * dim;
            int ci2 = Math.Clamp(spanIdx - 2, 0, ctrlCount - 1) * dim;
            int ci1 = Math.Clamp(spanIdx - 1, 0, ctrlCount - 1) * dim;
            int ci0 = Math.Clamp(spanIdx,     0, ctrlCount - 1) * dim;

            for (int d = 0; d < dim; d++)
            {
                result[d] = c3 * GetControl(controls, ci3 + d)
                          + c2 * GetControl(controls, ci2 + d)
                          + c1 * GetControl(controls, ci1 + d)
                          + c0 * GetControl(controls, ci0 + d);
            }
        }

        // Generic B-spline evaluation for arbitrary degree using the recursive
        // de Boor algorithm. Matches SDK's RecursiveCoefficients.
        private static void EvaluateGeneric(float[] knots, float[] controls, int dim, int degree,
            int spanIdx, int knotCount, int ctrlCount, float time, Span<float> result)
        {
            int order = degree + 1;
            Span<float> coeffs = stackalloc float[order];
            coeffs.Clear();

            // Compute B-spline basis coefficients using recursive subdivision
            RecursiveCoefficients(degree, degree,
                knots, knotCount, spanIdx, time, coeffs, 0, 1.0f);

            // Blend control points
            for (int d = 0; d < dim; d++) result[d] = 0f;

            for (int i = 0; i < order; i++)
            {
                int ctrlIdx = Math.Clamp(spanIdx - degree + i, 0, ctrlCount - 1) * dim;
                float w = coeffs[i];
                if (MathF.Abs(w) < 1e-10f) continue;
                for (int d = 0; d < dim; d++)
                    result[d] += w * GetControl(controls, ctrlIdx + d);
            }
        }

        // Recursive B-spline coefficient computation.
        private static void RecursiveCoefficients(int degree, int k,
            float[] knots, int knotCount, int spanIdx, float time,
            Span<float> coeffs, int coeffOffset, float weight)
        {
            if (k == 0)
            {
                coeffs[coeffOffset] += weight;
                return;
            }

            // ti[-1] in SDK = knots[spanIdx - k]
            // ti[d-k] in SDK = knots[spanIdx + degree - k]
            float tLow  = GetKnot(knots, spanIdx - k, knotCount);
            float tHigh = GetKnot(knots, spanIdx + degree - k, knotCount);
            float blend = SafeDiv(time - tLow, tHigh - tLow);

            RecursiveCoefficients(degree, k - 1, knots, knotCount, spanIdx - 1, time,
                coeffs, coeffOffset, weight * (1f - blend));
            RecursiveCoefficients(degree, k - 1, knots, knotCount, spanIdx, time,
                coeffs, coeffOffset + 1, weight * blend);
        }

        //  Helpers


        private static int FindKnotSpan(float[] knots, float time)
        {
            if (knots.Length == 0) return 0;
            if (time <= knots[0]) return 0;
            if (time >= knots[^1]) return knots.Length - 1;

            int lo = 0, hi = knots.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (knots[mid] <= time)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            return lo;
        }

        // Gets a knot value with clamped boundary access (replicates first/last knot).
        private static float GetKnot(float[] knots, int index, int knotCount)
        {
            if (index < 0) return knots[0];
            if (index >= knotCount) return knots[knotCount - 1];
            return knots[index];
        }

        // Gets a knot value with unclamped boundary.
        private static float GetKnot(float[] knots, int index)
        {
            if (index < 0) return knots[0];
            if (index >= knots.Length) return knots[^1];
            return knots[index];
        }

        // Gets a control value with clamped boundary access.
        private static float GetControl(float[] controls, int index)
        {
            if (index < 0) return controls[0];
            if (index >= controls.Length) return controls[^1];
            return controls[index];
        }

        // Safe division that returns 0 when denominator is near zero.
        private static float SafeDiv(float num, float den)
        {
            return MathF.Abs(den) > 1e-10f ? num / den : 0f;
        }

        // Writes identity values into the result span (0 for position, 1,0,0,0 for quat, identity scale).
        private static void WriteIdentity(Span<float> result, int dim)
        {
            // Generic zero-fill is correct for positions.
            // For quaternions (dim=4): x=0, y=0, z=0, w=1
            // For scale 3D (dim=3): 1, 1, 1
            // For scale 9D (dim=9): identity 3x3
            result.Clear();
            if (dim == 4)
            {
                result[3] = 1f; // quaternion w
            }
            else if (dim == 3)
            {
                result[0] = 1f; result[1] = 1f; result[2] = 1f;
            }
            else if (dim == 9)
            {
                result[0] = 1f; result[4] = 1f; result[8] = 1f; // identity matrix diagonal
            }
        }
    }
}
