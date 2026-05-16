using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ForzaTechStudio.Models
{
    // Condition (Pre/Post)


    public partial class PoiCondition : ObservableObject
    {
        [ObservableProperty] private bool   _isSet = true;   // true → <Set>, false → <UnSet>
        [ObservableProperty] private string _state = "";
        [ObservableProperty] private string _overrideMode = "Add"; // "Remove" | "Add"
    }

    // Visibility


    public partial class PoiVisibility : ObservableObject
    {
        [ObservableProperty] private bool   _enabled    = true;
        [ObservableProperty] private double _iconScale  = 1.0;
        [ObservableProperty] private bool   _instantSelect;
        [ObservableProperty] private string _locator    = "";

        // Position
        [ObservableProperty] private double _posX;
        [ObservableProperty] private double _posY;
        [ObservableProperty] private double _posZ;

        // Axis (facing direction)
        [ObservableProperty] private double _axisYaw;
        [ObservableProperty] private double _axisPitch;

        // Apex cone
        [ObservableProperty] private double _apexYaw;
        [ObservableProperty] private double _apexPitch;

        // ActiveApex
        [ObservableProperty] private double _activeApexYaw;
        [ObservableProperty] private double _activeApexPitch;

        // MidApex
        [ObservableProperty] private double _midApexYaw;
        [ObservableProperty] private double _midApexPitch;

        // Distance
        [ObservableProperty] private double _nearRadius;
        [ObservableProperty] private double _midRadius;
        [ObservableProperty] private double _farRadius;

        // Optional Camera sub-group
        [ObservableProperty] private bool   _hasCamera;
        [ObservableProperty] private double _cameraFovScale;
        [ObservableProperty] private double _cameraWalkSpeedScale;

        // Optional LookAt sub-group
        [ObservableProperty] private bool   _hasLookAt;
        [ObservableProperty] private double _lookAtHeightBoost;
        [ObservableProperty] private double _lookAtBlend;
        [ObservableProperty] private double _lookAtPosX;
        [ObservableProperty] private double _lookAtPosY;
        [ObservableProperty] private double _lookAtPosZ;

        // Optional Cursor sub-group
        [ObservableProperty] private bool   _hasCursor;
        [ObservableProperty] private double _cursorMinActivateZ;
    }

    // Action


    public partial class PoiAction : ObservableObject
    {
        [ObservableProperty] private string _cinematic             = " ";
        [ObservableProperty] private bool   _shouldHideUI;
        [ObservableProperty] private bool   _restoreAnimationState;
        [ObservableProperty] private bool   _fadeOutUITransition;
        [ObservableProperty] private string _sceneName             = " ";

        // SetView sub group
        [ObservableProperty] private string _setViewName                 = " ";
        [ObservableProperty] private string _setViewTransitionLocator    = " ";
        [ObservableProperty] private double _transitionX;
        [ObservableProperty] private double _transitionY;
        [ObservableProperty] private double _transitionZ;
        [ObservableProperty] private double _transitionW;

        // Requires list of POI names that must be visited first
        public ObservableCollection<string> RequiredPOIs { get; } = new();
    }

    // View entry 


    public partial class PoiViewEntry : ObservableObject
    {
        [ObservableProperty] private string _name         = "";
        [ObservableProperty] private string _guid         = "";
        [ObservableProperty] private string _locator      = "";
        [ObservableProperty] private string _overrideMode = "Add";
        [ObservableProperty] private bool   _enableBackOut;

        // Position offset
        [ObservableProperty] private double _posX;
        [ObservableProperty] private double _posY;
        [ObservableProperty] private double _posZ;

        // Direction
        [ObservableProperty] private double _directionYaw;

        // Size
        [ObservableProperty] private double _sizeWidth = 0.1;

        // Limits
        [ObservableProperty] private double _minYaw   = -360;
        [ObservableProperty] private double _maxYaw   =  360;
        [ObservableProperty] private double _minPitch = -360;
        [ObservableProperty] private double _maxPitch =  360;
    }

    // Point of Interest


    public partial class PointOfInterest : ObservableObject
    {
        // Top level identity
        [ObservableProperty] private string _name            = "";
        [ObservableProperty] private string _overrideMode    = "Add";  
        [ObservableProperty] private string _poiType         = "Pin";   
        [ObservableProperty] private string _iconPath        = "";
        [ObservableProperty] private string _iconType        = "None";
        [ObservableProperty] private string _icon            = "";      // e.g. "CloseDoor", "OpenDoor"
        [ObservableProperty] private string _tutorialAudioId = " ";
        [ObservableProperty] private string _tutorialTextId  = " ";

        // Boolean flags
        [ObservableProperty] private bool _pgPin;
        [ObservableProperty] private bool _autovistaLite;
        [ObservableProperty] private bool _isActionable;
        [ObservableProperty] private bool _handlesBack;
        [ObservableProperty] private bool _handlesEngineOff;
        [ObservableProperty] private bool _handlesExplode;
        [ObservableProperty] private bool _handlesImplode;

        // Space-type only
        [ObservableProperty] private double _spaceControllerAngle;
        [ObservableProperty] private double _spaceControllerAngleError;

        // Sub-structures
        public ObservableCollection<PoiCondition> PreConditions  { get; } = new();
        public ObservableCollection<PoiCondition> PostConditions { get; } = new();
        [ObservableProperty] private PoiVisibility? _visibility;
        [ObservableProperty] private PoiAction?     _action;

        // Pass-through for any unknown attributes not modelled above
        internal Dictionary<string, string> ExtraAttributes { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    // Root document

    public class AvPinsData
    {
        public string Template { get; set; } = "Default";

        public ObservableCollection<PoiCondition>    InitialStates { get; } = new();
        public ObservableCollection<PoiViewEntry>    Views { get; } = new();
        public ObservableCollection<PointOfInterest> POIs  { get; } = new();
    }
}
