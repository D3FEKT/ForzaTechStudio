using ForzaTechStudio.ViewModels;
using System;
using System.IO;
using System.Text;

namespace ForzaTechStudio.Services
{
    // In-place binary writer for FxStudio Particle Bank (.fxb) files.
    // Patches numeric fields in-place (floats, colors, durations) without rebuilding offset tables.
    public sealed class FxbWriter : IDisposable
    {
        private readonly Stream _stream;
        private readonly BinaryWriter _writer;
        private bool _disposed;

        public FxbWriter(Stream stream)
        {
            if (!stream.CanWrite)
                throw new ArgumentException("Stream must be writable.", nameof(stream));
            _stream = stream;
            _writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        }

        // Effect-level patches


        public void PatchEffectDuration(FxbEffectNode effect, float newDuration)
        {
            if (effect.DurationFieldOffset <= 0)
                throw new InvalidOperationException("DurationFieldOffset not set on this effect node.");
            WriteFloat(effect.DurationFieldOffset, newDuration);
            effect.FDuration = newDuration;
        }

        // Phase-level patches


        public void PatchPhaseDuration(FxbPhaseNode phase, float newDuration)
        {
            if (phase.DurationFieldOffset <= 0)
                throw new InvalidOperationException("DurationFieldOffset not set on this phase node.");
            WriteFloat(phase.DurationFieldOffset, newDuration);
            phase.FDuration = newDuration;
        }

        // Patches PhaseDefinition.m_nPlayCount (-1 = infinite).
        public void PatchPhasePlayCount(FxbPhaseNode phase, int newPlayCount)
        {
            if (phase.PlayCountFieldOffset <= 0)
                throw new InvalidOperationException("PlayCountFieldOffset not set on this phase node.");
            WriteInt32(phase.PlayCountFieldOffset, newPlayCount);
            phase.NPlayCount = newPlayCount;
        }

        // Component-level patches

        // Patches ComponentHeader.m_fStartTime.
        public void PatchComponentStartTime(FxbComponentNode comp, float newTime)
        {
            if (comp.StartTimeFieldOffset <= 0)
                throw new InvalidOperationException("StartTimeFieldOffset not set.");
            WriteFloat(comp.StartTimeFieldOffset, newTime);
            comp.FStartTime = newTime;
        }

        // Patches ComponentHeader.m_fEndTime.
        public void PatchComponentEndTime(FxbComponentNode comp, float newTime)
        {
            if (comp.EndTimeFieldOffset <= 0)
                throw new InvalidOperationException("EndTimeFieldOffset not set.");
            WriteFloat(comp.EndTimeFieldOffset, newTime);
            comp.FEndTime = newTime;
        }

        // Float keyframe patches

        // Patches a single LinearFloatKeyFrame in-place.
        public void PatchLinearKeyframe(FxbLinearKeyframeRow kf, float newUnitTime, float newValue)
        {
            if (kf.UnitTimeOffset <= 0)
                throw new InvalidOperationException("UnitTimeOffset not set.");
            WriteFloat(kf.UnitTimeOffset, newUnitTime);
            WriteFloat(kf.ValueOffset,    newValue);
            kf.UnitTime = newUnitTime;
            kf.Value    = newValue;
        }

        // Patches a single CubicFloatKeyFrame in-place.
        public void PatchCubicKeyframe(FxbCubicKeyframeRow kf,
                                        float endUnitTime, float a, float b, float c, float d)
        {
            if (kf.DataOffset <= 0)
                throw new InvalidOperationException("DataOffset not set.");
            Seek(kf.DataOffset);
            WriteFloat(kf.DataOffset,      endUnitTime);
            WriteFloat(kf.DataOffset + 4,  a);
            WriteFloat(kf.DataOffset + 8,  b);
            WriteFloat(kf.DataOffset + 12, c);
            WriteFloat(kf.DataOffset + 16, d);
            kf.EndUnitTime = endUnitTime;
            kf.A = a; kf.B = b; kf.C = c; kf.D = d;
        }

        // Color keyframe patches

        // Patches a single ColorARGBKeyFrame in-place.
        public void PatchColorKeyframe(FxbColorKeyframeRow kf,
                                        float unitTime, byte r, byte g, byte b, byte a)
        {
            if (kf.UnitTimeOffset <= 0)
                throw new InvalidOperationException("UnitTimeOffset not set.");

            WriteFloat(kf.UnitTimeOffset, unitTime);

            // ColorARGB memory layout: byte[0]=blue, [1]=green, [2]=red, [3]=alpha
            uint packed = (uint)b | ((uint)g << 8) | ((uint)r << 16) | ((uint)a << 24);
            WriteUInt32(kf.ColorValueOffset, packed);

            kf.UnitTime = unitTime;
            kf.Red = r; kf.Green = g; kf.Blue = b; kf.Alpha = a;
        }

        // Property scalar patches

        // Patches a FloatRange [min, max] pair in-place.
        public void PatchFloatRange(FxbPropertyValueNode prop, float min, float max)
        {
            if (prop.FloatMinOffset <= 0)
                throw new InvalidOperationException("FloatMinOffset not set.");
            WriteFloat(prop.FloatMinOffset, min);
            WriteFloat(prop.FloatMaxOffset, max);
            prop.FloatMin = min;
            prop.FloatMax = max;
            prop.Summary  = $"[{min:G5}, {max:G5}]";
        }

        // Patches an IntegerRange [min, max] pair in-place.
        public void PatchIntegerRange(FxbPropertyValueNode prop, int min, int max)
        {
            if (prop.IntMinOffset <= 0)
                throw new InvalidOperationException("IntMinOffset not set.");
            WriteInt32(prop.IntMinOffset, min);
            WriteInt32(prop.IntMaxOffset, max);
            prop.IntMin   = min;
            prop.IntMax   = max;
            prop.Summary  = $"[{min}, {max}]";
        }

        // Patches a Vector3 (X, Y, Z) in-place.
        public void PatchVector3(FxbPropertyValueNode prop, float x, float y, float z)
        {
            if (prop.VecOffset <= 0)
                throw new InvalidOperationException("VecOffset not set.");
            WriteFloat(prop.VecOffset,     x);
            WriteFloat(prop.VecOffset + 4, y);
            WriteFloat(prop.VecOffset + 8, z);
            prop.VecX    = x;
            prop.VecY    = y;
            prop.VecZ    = z;
            prop.Summary = $"({x:G4}, {y:G4}, {z:G4})";
        }

        // Patches a Vector4 (X, Y, Z, W) in-place.
        public void PatchVector4(FxbPropertyValueNode prop, float x, float y, float z, float w)
        {
            if (prop.VecOffset <= 0)
                throw new InvalidOperationException("VecOffset not set.");
            WriteFloat(prop.VecOffset,      x);
            WriteFloat(prop.VecOffset + 4,  y);
            WriteFloat(prop.VecOffset + 8,  z);
            WriteFloat(prop.VecOffset + 12, w);
            prop.VecX    = x;
            prop.VecY    = y;
            prop.VecZ    = z;
            prop.VecW    = w;
            prop.Summary = $"({x:G4}, {y:G4}, {z:G4}, {w:G4})";
        }

        // Patches an inline Integer property value in-place.
        public void PatchIntegerInline(FxbPropertyValueNode prop, int newValue)
        {
            if (prop.IntValueFieldOffset <= 0)
                throw new InvalidOperationException("IntValueFieldOffset not set on this property node.");
            WriteInt32(prop.IntValueFieldOffset, newValue);
            prop.IntValue = newValue;
            prop.Summary  = $"{newValue}";
        }

        // Patches an inline Float property value in-place. m_Data stores an IEEE 754 float as uint32.
        public void PatchFloatInline(FxbPropertyValueNode prop, float newValue)
        {
            if (prop.FloatValueFieldOffset <= 0)
                throw new InvalidOperationException("FloatValueFieldOffset not set on this property node.");
            WriteFloat(prop.FloatValueFieldOffset, newValue);
            prop.FloatValue = newValue;
            prop.Summary    = $"{newValue:G6}";
        }

        // Primitives

        private void Seek(long offset) => _stream.Seek(offset, SeekOrigin.Begin);

        private void WriteFloat(long offset, float value)
        {
            Seek(offset);
            _writer.Write(value);
        }

        private void WriteInt32(long offset, int value)
        {
            Seek(offset);
            _writer.Write(value);
        }

        private void WriteUInt32(long offset, uint value)
        {
            Seek(offset);
            _writer.Write(value);
        }

        // IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _writer.Flush();
            _writer.Dispose();
            _disposed = true;
        }
    }
}
