using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;


namespace ForzaTechStudio.ViewModels
{

    public enum FxbPropertyType : uint
    {
        Integer           = 0,
        IntegerRange      = 1,
        IntegerArray      = 2,
        ColorARGBKeyFrame = 3,
        Float             = 4,  
        FloatRange        = 5,
        FloatArray        = 6,
        FloatKeyFrame     = 7,
        String            = 8,   
        StringArray       = 9,
        Vector3           = 10,
        Vector3Range      = 11,
        Vector3Array      = 12,
        FixedFunction     = 13,
        Vector4           = 14,
    }

    public enum FxbFloatKeyFrameType : uint
    {
        Linear = 0,
        Cubic  = 1,
    }

    public enum FxbFixedFunctionType : uint
    {
        Quadratic  = 1,
        Sinusoidal = 2,
    }



    public readonly struct FxbBankHeader
    {
        public readonly uint m_MagicId;                    // +0x00  'FxBk' = 0x6B427846
        public readonly uint m_Version;                    // +0x04  21000  = 0x5208
        public readonly uint m_PlatformId;                 // +0x08
        public readonly uint m_BankSize;                   // +0x0C
        public readonly uint m_BankName;                   // +0x10  offset ? string
        public readonly uint m_StringTableOffset;          // +0x14
        public readonly uint m_Vector3TableOffset;         // +0x18
        public readonly uint m_Vector4TableOffset;         // +0x1C
        public readonly uint m_FloatRangeTableOffset;      // +0x20
        public readonly uint m_IntegerRangeTableOffset;    // +0x24
        public readonly uint m_FixedFunctionOffset;        // +0x28
        public readonly uint m_ColorARGBChannelDataOffset; // +0x2C
        public readonly uint m_FloatChannelDataOffset;     // +0x30
        public readonly uint m_ChannelTableOffset;         // +0x34
        public readonly uint m_LODTableOffset;             // +0x38
        public readonly uint m_NumberComponentDefs;        // +0x3C
        public readonly uint m_ComponetDefOffset;          // +0x40 
        public readonly uint m_NumInputs;                  // +0x44
        public readonly uint m_InputDefinitionOffset;      // +0x48
        public readonly uint m_LODCategories;              // +0x4C
        public readonly uint m_EffectNameTable;            // +0x50
        public readonly uint m_EffectIdTable;              // +0x54
        public readonly uint m_NumberEffects;              // +0x58
        public readonly uint m_EffectOffset;               // +0x5C

        public FxbBankHeader(
            uint m_MagicId, uint m_Version, uint m_PlatformId, uint m_BankSize,
            uint m_BankName, uint m_StringTableOffset, uint m_Vector3TableOffset,
            uint m_Vector4TableOffset, uint m_FloatRangeTableOffset, uint m_IntegerRangeTableOffset,
            uint m_FixedFunctionOffset, uint m_ColorARGBChannelDataOffset,
            uint m_FloatChannelDataOffset, uint m_ChannelTableOffset, uint m_LODTableOffset,
            uint m_NumberComponentDefs, uint m_ComponetDefOffset, uint m_NumInputs,
            uint m_InputDefinitionOffset, uint m_LODCategories, uint m_EffectNameTable,
            uint m_EffectIdTable, uint m_NumberEffects, uint m_EffectOffset)
        {
            this.m_MagicId = m_MagicId; this.m_Version = m_Version; this.m_PlatformId = m_PlatformId;
            this.m_BankSize = m_BankSize; this.m_BankName = m_BankName;
            this.m_StringTableOffset = m_StringTableOffset; this.m_Vector3TableOffset = m_Vector3TableOffset;
            this.m_Vector4TableOffset = m_Vector4TableOffset; this.m_FloatRangeTableOffset = m_FloatRangeTableOffset;
            this.m_IntegerRangeTableOffset = m_IntegerRangeTableOffset; this.m_FixedFunctionOffset = m_FixedFunctionOffset;
            this.m_ColorARGBChannelDataOffset = m_ColorARGBChannelDataOffset;
            this.m_FloatChannelDataOffset = m_FloatChannelDataOffset; this.m_ChannelTableOffset = m_ChannelTableOffset;
            this.m_LODTableOffset = m_LODTableOffset; this.m_NumberComponentDefs = m_NumberComponentDefs;
            this.m_ComponetDefOffset = m_ComponetDefOffset; this.m_NumInputs = m_NumInputs;
            this.m_InputDefinitionOffset = m_InputDefinitionOffset; this.m_LODCategories = m_LODCategories;
            this.m_EffectNameTable = m_EffectNameTable; this.m_EffectIdTable = m_EffectIdTable;
            this.m_NumberEffects = m_NumberEffects; this.m_EffectOffset = m_EffectOffset;
        }

        public const uint MagicValue   = 0x6B427846u; // 'FxBk' LE
        public const uint VersionValue = 21000u;       // 0x5208
        public const int  SizeBytes    = 96;           // 0x60
    }

    public readonly struct FxbEffectHeader
    {
        public readonly uint  m_NameId;           // +0x00  offset ? string
        public readonly uint  m_nId;              // +0x04  numeric effect ID
        public readonly float m_fDuration;        // +0x08  seconds; -1 = infinite
        public readonly uint  m_nLODCategory;     // +0x0C  offset to LODCategory (0xFFFFFFFF = none)
        public readonly uint  m_nNumInputs;       // +0x10
        public readonly uint  m_nInputOffset;     // +0x14
        public readonly uint  m_nInputDataSize;   // +0x18
        public readonly uint  m_nNumPhases;       // +0x1C
        public readonly uint  m_nPhaseOffset;     // +0x20
        public readonly uint  m_nNumComponents;   // +0x24
        public readonly uint  m_nComponentOffset; // +0x28

        public FxbEffectHeader(uint nameId, uint nId, float fDuration, uint nLODCategory,
            uint nNumInputs, uint nInputOffset, uint nInputDataSize,
            uint nNumPhases, uint nPhaseOffset, uint nNumComponents, uint nComponentOffset)
        {
            m_NameId = nameId; m_nId = nId; m_fDuration = fDuration; m_nLODCategory = nLODCategory;
            m_nNumInputs = nNumInputs; m_nInputOffset = nInputOffset; m_nInputDataSize = nInputDataSize;
            m_nNumPhases = nNumPhases; m_nPhaseOffset = nPhaseOffset;
            m_nNumComponents = nNumComponents; m_nComponentOffset = nComponentOffset;
        }

        public const int SizeBytes = 44;          // 0x2C
    }

    public readonly struct FxbPhaseDefinition
    {
        public readonly uint  m_NameId;      // +0x00 offset ? string
        public readonly float m_fDuration;   // +0x04 seconds
        public readonly int   m_nPlayCount;  // +0x08 loops; -1 = infinite
        public const int SizeBytes = 12;
    }

    public readonly struct FxbComponentHeader
    {
        public readonly uint  m_nComponentDefinitionOffset; // +0x00
        public readonly float m_fStartTime;                 // +0x04
        public readonly float m_fEndTime;                   // +0x08
        public readonly uint  m_nTrackGroup;                // +0x0C
        public readonly uint  m_nNumPropertyValues;         // +0x10
        public readonly uint  m_nPropertyOffset;            // +0x14
        public readonly uint  m_nNumInputValues;            // +0x18
        public readonly uint  m_nInputOffset;               // +0x1C
        public readonly uint  m_nNumDynamicValues;          // +0x20
        public readonly uint  m_nDynamicOffset;             // +0x24

        public FxbComponentHeader(uint  nComponentDefinitionOffset, float  startTime, float  endTime, uint  trackGroup, uint  numPropertyValues, uint  propertyOffset, uint  numInputValues, uint  inputOffset, uint  numDynamicValues, uint  dynamicOffset)
        {
            m_nComponentDefinitionOffset = nComponentDefinitionOffset;
            m_fStartTime = startTime;
            m_fEndTime = endTime;
            m_nTrackGroup = trackGroup;
            m_nNumPropertyValues = numPropertyValues;
            m_nPropertyOffset = propertyOffset;
            m_nNumInputValues = numInputValues;
            m_nInputOffset = inputOffset;
            m_nNumDynamicValues = numDynamicValues;
            m_nDynamicOffset = dynamicOffset;
        }

        public const int FixedSizeBytes = 40;


        // Stride to the next ComponentHeader in the effect's component array.
        // stride = 40 + 8*nProp + 8*nInput + 12*nDynamic

        public uint Stride =>
            (uint)FixedSizeBytes
            + 8u * m_nNumPropertyValues
            + 8u * m_nNumInputValues
            + 12u * m_nNumDynamicValues;
    }

    public readonly struct FxbComponentProperty
    {
        public readonly uint m_PropertyIndex; // +0x00
        public readonly uint m_Data;          // +0x04  offset to value data
        public const int SizeBytes = 8;
    }

    public readonly struct FxbComponentInput
    {
        public readonly uint m_PropertyIndex;         // +0x00
        public readonly uint m_InputDefinitionOffset; // +0x04
        public const int SizeBytes = 8;
    }

    public readonly struct FxbEffectNameTableEntry
    {
        public readonly uint m_NameId;        // +0x00
        public readonly uint m_EffectOffset;  // +0x04
        public FxbEffectNameTableEntry(uint nameId, uint effectOffset)
        { m_NameId = nameId; m_EffectOffset = effectOffset; }
        public const int SizeBytes = 8;
    }

    public readonly struct FxbEffectIdTableEntry
    {
        public readonly uint m_Id;            // +0x00
        public readonly uint m_EffectOffset;  // +0x04
        public FxbEffectIdTableEntry(uint id, uint effectOffset)
        { m_Id = id; m_EffectOffset = effectOffset; }
        public const int SizeBytes = 8;
    }

    public readonly struct FxbLODCategory
    {
        public readonly uint  m_NameId;      // +0x00
        public readonly uint  m_nNumLevels;  // +0x04
        public readonly float m_fData;       // +0x08
        public const int SizeBytes = 12;
    }

    public readonly struct FxbLinearFloatKeyFrame
    {
        public readonly float m_fUnitTime; // [0,1] normalised
        public readonly float m_fValue;
        public const int SizeBytes = 8;
    }

    public readonly struct FxbCubicFloatKeyFrame
    {
        public readonly float m_fEndUnitTime;
        public readonly float m_fA;
        public readonly float m_fB;
        public readonly float m_fC;
        public readonly float m_fD;
        public const int SizeBytes = 20;
    }

    public readonly struct FxbColorARGBKeyFrame
    {
        public readonly float m_fUnitTime;
        public readonly uint  m_nRawValue;  // union: ColorARGB (B,G,R,A bytes) or raw int
        public const int SizeBytes = 8;

        public byte Blue  => (byte)(m_nRawValue & 0xFF);
        public byte Green => (byte)((m_nRawValue >> 8) & 0xFF);
        public byte Red   => (byte)((m_nRawValue >> 16) & 0xFF);
        public byte Alpha => (byte)((m_nRawValue >> 24) & 0xFF);
    }


    // View model nodes for TreeView and detail panels


    public partial class FxbBankNode : ObservableObject
    {
        [ObservableProperty] private string _bankName = "";
        [ObservableProperty] private uint _version;
        [ObservableProperty] private uint _bankSize;
        [ObservableProperty] private uint _numberEffects;

        public ObservableCollection<FxbEffectNode> Effects { get; } = new();

        public string FilePath { get; set; } = "";
    }

    public partial class FxbEffectNode : ObservableObject
    {
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private uint   _nId;
        [ObservableProperty] private float  _fDuration;
        [ObservableProperty] private uint   _lodCategory;

        // file offsets for in-place patching
        public long EffectHeaderOffset  { get; set; }
        public long DurationFieldOffset { get; set; }  // offset of m_fDuration inside file

        public ObservableCollection<FxbPhaseNode>     Phases     { get; } = new();
        public ObservableCollection<FxbComponentNode> Components { get; } = new();

        public string DisplayName => string.IsNullOrEmpty(Name) ? $"[0x{NId:X}]" : Name;
    }

    public partial class FxbPhaseNode : ObservableObject
    {
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private float  _fDuration;
        [ObservableProperty] private int    _nPlayCount;

        // File offsets for in-place patching
        public long DurationFieldOffset  { get; set; }
        public long PlayCountFieldOffset { get; set; }

        public string PlayCountDisplay => NPlayCount == -1 ? "inf" : NPlayCount.ToString();
        public string DisplayName => string.IsNullOrEmpty(Name) ? "(unnamed phase)" : Name;
    }

    public partial class FxbComponentNode : ObservableObject
    {
        [ObservableProperty] private string _typeName = "";
        [ObservableProperty] private float  _fStartTime;
        [ObservableProperty] private float  _fEndTime;
        [ObservableProperty] private uint   _trackGroup;
        [ObservableProperty] private uint   _numPropertyValues;
        [ObservableProperty] private uint   _numInputValues;
        [ObservableProperty] private uint   _numDynamicValues;

        // File offsets for in-place patching
        public long StartTimeFieldOffset { get; set; }
        public long EndTimeFieldOffset   { get; set; }

        public ObservableCollection<FxbPropertyValueNode> PropertyValues { get; } = new();

        public string DisplayName => string.IsNullOrEmpty(TypeName) ? "(unknown component)" : TypeName;
        public string TimeRange   => $"{FStartTime:F3}s - {FEndTime:F3}s";
    }

    public partial class FxbPropertyValueNode : ObservableObject
    {
        [ObservableProperty] private string          _name    = "";
        [ObservableProperty] private FxbPropertyType _typeId;
        [ObservableProperty] private string          _summary = "";

        // re-notify visibility helpers when TypeId changes
        partial void OnNameChanged(string value) => OnPropertyChanged(nameof(SemanticHint));
        partial void OnTypeIdChanged(FxbPropertyType value)
        {
            OnPropertyChanged(nameof(SemanticHint));
            OnPropertyChanged(nameof(IsFloatKeyframe));
            OnPropertyChanged(nameof(IsColorKeyframe));
            OnPropertyChanged(nameof(IsFloatRange));
            OnPropertyChanged(nameof(IsIntegerRange));
            OnPropertyChanged(nameof(IsIntegerInline));
            OnPropertyChanged(nameof(IsFloatInline));
            OnPropertyChanged(nameof(IsStringProperty));
            OnPropertyChanged(nameof(IsVector3));
            OnPropertyChanged(nameof(IsVector4));
            OnPropertyChanged(nameof(HasLinearKeyframes));
            OnPropertyChanged(nameof(HasCubicKeyframes));
            OnPropertyChanged(nameof(DisplayName));
        }

        // offset of value data block
        public long DataOffset { get; set; }
        [ObservableProperty] private float _floatMin;
        [ObservableProperty] private float _floatMax;
        public long FloatMinOffset { get; set; }
        public long FloatMaxOffset { get; set; }

        [ObservableProperty] private int _intMin;
        [ObservableProperty] private int _intMax;
        public long IntMinOffset { get; set; }
        public long IntMaxOffset { get; set; }

        [ObservableProperty] private int _intValue;
        public long IntValueFieldOffset { get; set; }


        [ObservableProperty] private float _floatValue;
        public long FloatValueFieldOffset { get; set; }

        [ObservableProperty] private string _stringValue = "";
        [ObservableProperty] private float _vecX;
        [ObservableProperty] private float _vecY;
        [ObservableProperty] private float _vecZ;
        [ObservableProperty] private float _vecW;
        public long VecOffset { get; set; }

        // Keyframe collections
        public ObservableCollection<FxbLinearKeyframeRow>  LinearKeyframes { get; } = new();
        public ObservableCollection<FxbCubicKeyframeRow>   CubicKeyframes  { get; } = new();
        public ObservableCollection<FxbColorKeyframeRow>   ColorKeyframes  { get; } = new();

        // visibility helpers for XAML panel switching
        public bool IsFloatKeyframe    => TypeId == FxbPropertyType.FloatKeyFrame;
        public bool IsColorKeyframe    => TypeId == FxbPropertyType.ColorARGBKeyFrame;
        public bool IsFloatRange       => TypeId == FxbPropertyType.FloatRange;
        public bool IsIntegerRange     => TypeId == FxbPropertyType.IntegerRange;
        public bool IsIntegerInline    => TypeId == FxbPropertyType.Integer;
        public bool IsFloatInline      => TypeId == FxbPropertyType.Float;
        public bool IsStringProperty   => TypeId == FxbPropertyType.String;
        public bool IsVector3          => TypeId == FxbPropertyType.Vector3 || TypeId == FxbPropertyType.Vector3Range;
        public bool IsVector4          => TypeId == FxbPropertyType.Vector4;
        public bool HasLinearKeyframes => IsFloatKeyframe && LinearKeyframes.Count > 0;
        public bool HasCubicKeyframes  => IsFloatKeyframe && CubicKeyframes.Count > 0;

        public string DisplayName => string.IsNullOrEmpty(Name) ? $"[{TypeId}]" : Name;

        public string SemanticHint => FxbPropertySemantics.Describe(Name, TypeId);
    }

    public partial class FxbLinearKeyframeRow : ObservableObject
    {
        [ObservableProperty] private float _unitTime;
        [ObservableProperty] private float _value;
        public long UnitTimeOffset { get; set; }
        public long ValueOffset    { get; set; }
    }

    public partial class FxbCubicKeyframeRow : ObservableObject
    {
        [ObservableProperty] private float _endUnitTime;
        [ObservableProperty] private float _a;
        [ObservableProperty] private float _b;
        [ObservableProperty] private float _c;
        [ObservableProperty] private float _d;
        public long DataOffset { get; set; }
    }

    public partial class FxbColorKeyframeRow : ObservableObject
    {
        [ObservableProperty] private float _unitTime;
        [ObservableProperty] private byte  _red;
        [ObservableProperty] private byte  _green;
        [ObservableProperty] private byte  _blue;
        [ObservableProperty] private byte  _alpha;
        public long UnitTimeOffset  { get; set; }
        public long ColorValueOffset { get; set; }
    }
}
