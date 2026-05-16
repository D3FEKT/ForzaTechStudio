using ForzaTechStudio.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ForzaTechStudio.Services
{
    public static class AvPinsParser
    {
        // Helpers

        private static bool   B(XElement? el, string name, bool   def = false)
        {
            var v = el?.Attribute(name)?.Value ?? el?.Element(name)?.Value?.Trim();
            if (v == null) return def;
            return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static double D(XElement? el, string name, double def = 0.0)
        {
            var v = el?.Attribute(name)?.Value ?? el?.Element(name)?.Value?.Trim();
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : def;
        }

        private static string S(XElement? el, string name, string def = "")
        {
            var v = el?.Attribute(name)?.Value ?? el?.Element(name)?.Value?.Trim();
            return v ?? def;
        }

        private static string Fmt(double v) => v.ToString("F6", CultureInfo.InvariantCulture);
        private static string BoolStr(bool v) => v ? "1" : "0";

        private static readonly HashSet<string> _knownPoiAttrs = new(StringComparer.OrdinalIgnoreCase)
        {
            "Name","OverrideMode","Type","IconPath","IconType","Icon",
            "TutorialAudioID","TutorialTextID","PGPin","AutovistaLite",
            "IsActionable","HandlesBack","HandlesEngineOff","HandlesExplode","HandlesImplode",
            "SpaceControllerAngle","SpaceControllerAngleError"
        };

        // Conditions (Set / UnSet lists)

        private static void ParseConditions(XElement? parentEl, IList<PoiCondition> list)
        {
            if (parentEl == null) return;
            foreach (var el in parentEl.Elements())
            {
                if (el.Name.LocalName != "Set" && el.Name.LocalName != "UnSet") continue;
                var cond = new PoiCondition
                {
                    IsSet        = el.Name.LocalName == "Set",
                    State        = el.Attribute("State")?.Value ?? el.Element("State")?.Value?.Trim() ?? "",
                    OverrideMode = el.Attribute("OverrideMode")?.Value ?? "Add",
                };
                list.Add(cond);
            }
        }

        private static void WriteConditions(XmlWriter w, IEnumerable<PoiCondition> conditions, string wrapperName)
        {
            w.WriteStartElement(wrapperName);
            foreach (var c in conditions)
            {
                w.WriteStartElement(c.IsSet ? "Set" : "UnSet");
                if (c.OverrideMode != "Add")
                    w.WriteAttributeString("OverrideMode", c.OverrideMode);
                w.WriteAttributeString("State", c.State);
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        // Visibility

        private static PoiVisibility ParseVisibility(XElement? el)
        {
            var v = new PoiVisibility();
            if (el == null) return v;

            v.Enabled       = B(el, "Enabled", true);
            v.IconScale     = D(el, "IconScale", 1.0);
            v.InstantSelect = B(el, "InstantSelect");
            v.Locator       = S(el, "Locator");

            var pos = el.Element("Pos");
            v.PosX = D(pos, "X"); v.PosY = D(pos, "Y"); v.PosZ = D(pos, "Z");

            var axis      = el.Element("Axis");
            v.AxisYaw     = D(axis, "Yaw"); v.AxisPitch = D(axis, "Pitch");

            var apex      = el.Element("Apex");
            v.ApexYaw     = D(apex, "Yaw"); v.ApexPitch = D(apex, "Pitch");

            var aa        = el.Element("ActiveApex");
            v.ActiveApexYaw = D(aa, "Yaw"); v.ActiveApexPitch = D(aa, "Pitch");

            var mid       = el.Element("MidApex");
            v.MidApexYaw  = D(mid, "Yaw"); v.MidApexPitch = D(mid, "Pitch");

            var dist      = el.Element("Distance");
            v.NearRadius  = D(dist, "NearRadius");
            v.MidRadius   = D(dist, "MidRadius");
            v.FarRadius   = D(dist, "FarRadius");

            var cam       = el.Element("Camera");
            if (cam != null)
            {
                v.HasCamera            = true;
                v.CameraFovScale       = D(cam, "FOVScale");
                v.CameraWalkSpeedScale = D(cam, "WalkSpeedScale");
            }

            var lookAt = el.Element("LookAt");
            if (lookAt != null)
            {
                v.HasLookAt       = true;
                v.LookAtHeightBoost = D(lookAt, "HeightBoost");
                v.LookAtBlend       = D(lookAt, "Blend");
                var lp = lookAt.Element("Pos");
                v.LookAtPosX = D(lp, "X"); v.LookAtPosY = D(lp, "Y"); v.LookAtPosZ = D(lp, "Z");
            }

            var cursor    = el.Element("Cursor");
            if (cursor != null)
            {
                v.HasCursor           = true;
                v.CursorMinActivateZ  = D(cursor, "MinActivateZ");
            }

            return v;
        }

        private static void WriteVisibility(XmlWriter w, PoiVisibility v)
        {
            w.WriteStartElement("Visibility");
            w.WriteAttributeString("Enabled",  BoolStr(v.Enabled));
            if (v.IconScale != 1.0)
                w.WriteAttributeString("IconScale", Fmt(v.IconScale));
            if (v.InstantSelect)
                w.WriteAttributeString("InstantSelect", "1");
            w.WriteAttributeString("Locator", v.Locator);

            w.WriteStartElement("Pos");
            w.WriteAttributeString("X", Fmt(v.PosX));
            w.WriteAttributeString("Y", Fmt(v.PosY));
            w.WriteAttributeString("Z", Fmt(v.PosZ));
            w.WriteEndElement();

            w.WriteStartElement("Axis");
            w.WriteAttributeString("Yaw", Fmt(v.AxisYaw)); w.WriteAttributeString("Pitch", Fmt(v.AxisPitch));
            w.WriteEndElement();

            w.WriteStartElement("Apex");
            w.WriteAttributeString("Yaw", Fmt(v.ApexYaw)); w.WriteAttributeString("Pitch", Fmt(v.ApexPitch));
            w.WriteEndElement();

            w.WriteStartElement("ActiveApex");
            w.WriteAttributeString("Yaw", Fmt(v.ActiveApexYaw)); w.WriteAttributeString("Pitch", Fmt(v.ActiveApexPitch));
            w.WriteEndElement();

            w.WriteStartElement("MidApex");
            w.WriteAttributeString("Yaw", Fmt(v.MidApexYaw)); w.WriteAttributeString("Pitch", Fmt(v.MidApexPitch));
            w.WriteEndElement();

            w.WriteStartElement("Distance");
            w.WriteAttributeString("NearRadius", Fmt(v.NearRadius));
            w.WriteAttributeString("MidRadius",  Fmt(v.MidRadius));
            w.WriteAttributeString("FarRadius",  Fmt(v.FarRadius));
            w.WriteEndElement();

            if (v.HasCamera)
            {
                w.WriteStartElement("Camera");
                if (v.CameraFovScale       != 0) w.WriteAttributeString("FOVScale",       Fmt(v.CameraFovScale));
                if (v.CameraWalkSpeedScale != 0) w.WriteAttributeString("WalkSpeedScale", Fmt(v.CameraWalkSpeedScale));
                w.WriteEndElement();
            }

            if (v.HasLookAt)
            {
                w.WriteStartElement("LookAt");
                if (v.LookAtHeightBoost != 0) w.WriteAttributeString("HeightBoost", Fmt(v.LookAtHeightBoost));
                if (v.LookAtBlend       != 0) w.WriteAttributeString("Blend",       Fmt(v.LookAtBlend));
                if (v.LookAtPosX != 0 || v.LookAtPosY != 0 || v.LookAtPosZ != 0)
                {
                    w.WriteStartElement("Pos");
                    w.WriteAttributeString("X", Fmt(v.LookAtPosX));
                    w.WriteAttributeString("Y", Fmt(v.LookAtPosY));
                    w.WriteAttributeString("Z", Fmt(v.LookAtPosZ));
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }

            if (v.HasCursor)
            {
                w.WriteStartElement("Cursor");
                w.WriteAttributeString("MinActivateZ", Fmt(v.CursorMinActivateZ));
                w.WriteEndElement();
            }

            w.WriteEndElement(); // Visibility
        }

        // Action

        private static PoiAction ParseAction(XElement? el)
        {
            var a = new PoiAction();
            if (el == null) return a;

            a.Cinematic             = S(el, "Cinematic", " ");
            a.ShouldHideUI          = B(el, "ShouldHideUI");
            a.RestoreAnimationState = B(el, "RestoreAnimationState");
            a.FadeOutUITransition   = B(el, "FadeOutUITransition");
            a.SceneName             = S(el, "SceneName", " ");

            var sv = el.Element("SetView");
            if (sv != null)
            {
                a.SetViewName               = S(sv, "Name", " ");
                a.SetViewTransitionLocator  = S(sv, "TransitionLocator", " ");
                a.TransitionX = D(sv, "TransitionPoint.x");
                a.TransitionY = D(sv, "TransitionPoint.y");
                a.TransitionZ = D(sv, "TransitionPoint.z");
                a.TransitionW = D(sv, "TransitionPoint.w");
            }

            var req = el.Element("Requires");
            if (req != null)
            {
                foreach (var poiEl in req.Elements("POI"))
                {
                    var n = S(poiEl, "Name");
                    if (!string.IsNullOrEmpty(n)) a.RequiredPOIs.Add(n);
                }
            }

            return a;
        }

        private static void WriteAction(XmlWriter w, PoiAction a)
        {
            w.WriteStartElement("Action");
            w.WriteAttributeString("Cinematic",             string.IsNullOrEmpty(a.Cinematic)  ? " " : a.Cinematic);
            w.WriteAttributeString("ShouldHideUI",          BoolStr(a.ShouldHideUI));
            w.WriteAttributeString("RestoreAnimationState", BoolStr(a.RestoreAnimationState));
            w.WriteAttributeString("FadeOutUITransition",   BoolStr(a.FadeOutUITransition));
            w.WriteAttributeString("SceneName",             string.IsNullOrEmpty(a.SceneName)  ? " " : a.SceneName);

            if (a.RequiredPOIs.Count > 0)
            {
                w.WriteStartElement("Requires");
                foreach (var name in a.RequiredPOIs)
                {
                    w.WriteStartElement("POI");
                    w.WriteAttributeString("Name", name);
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }

            // SetView always written
            w.WriteStartElement("SetView");
            w.WriteAttributeString("Name",               string.IsNullOrEmpty(a.SetViewName)              ? " " : a.SetViewName);
            w.WriteAttributeString("TransitionLocator",  string.IsNullOrEmpty(a.SetViewTransitionLocator) ? " " : a.SetViewTransitionLocator);
            w.WriteAttributeString("TransitionPoint.x",  Fmt(a.TransitionX));
            w.WriteAttributeString("TransitionPoint.y",  Fmt(a.TransitionY));
            w.WriteAttributeString("TransitionPoint.z",  Fmt(a.TransitionZ));
            w.WriteAttributeString("TransitionPoint.w",  Fmt(a.TransitionW));
            w.WriteEndElement(); // SetView

            w.WriteEndElement(); // Action
        }

        // View 

        private static PoiViewEntry ParseView(XElement el)
        {
            var v = new PoiViewEntry();
            v.Name         = el.Attribute("Name")?.Value ?? el.Element("Name")?.Value?.Trim() ?? "";
            v.Guid         = el.Attribute("Guid")?.Value ?? "";
            v.Locator      = el.Attribute("Locator")?.Value ?? el.Element("Locator")?.Value?.Trim() ?? "";
            v.OverrideMode = el.Attribute("OverrideMode")?.Value ?? "Add";
            v.EnableBackOut = B(el, "EnableBackOut");

            var pos = el.Element("Pos");
            v.PosX = D(pos, "X"); v.PosY = D(pos, "Y"); v.PosZ = D(pos, "Z");

            v.DirectionYaw = D(el.Element("Direction"), "Yaw");
            v.SizeWidth    = D(el.Element("Size"),      "Width", 0.1);

            var lim = el.Element("Limits");
            v.MinYaw   = D(lim, "MinYaw",   -360);
            v.MaxYaw   = D(lim, "MaxYaw",    360);
            v.MinPitch = D(lim, "MinPitch", -360);
            v.MaxPitch = D(lim, "MaxPitch",  360);

            return v;
        }

        private static void WriteView(XmlWriter w, PoiViewEntry v)
        {
            w.WriteStartElement("View");
            w.WriteAttributeString("Name",    v.Name);
            if (!string.IsNullOrEmpty(v.Guid))
                w.WriteAttributeString("Guid", v.Guid);
            w.WriteAttributeString("Locator", v.Locator);
            if (v.OverrideMode != "Add")
                w.WriteAttributeString("OverrideMode", v.OverrideMode);
            if (v.EnableBackOut)
                w.WriteAttributeString("EnableBackOut", "1");

            w.WriteStartElement("Pos");
            w.WriteAttributeString("X", Fmt(v.PosX)); w.WriteAttributeString("Y", Fmt(v.PosY)); w.WriteAttributeString("Z", Fmt(v.PosZ));
            w.WriteEndElement();

            if (v.DirectionYaw != 0)
            {
                w.WriteStartElement("Direction");
                w.WriteAttributeString("Yaw", Fmt(v.DirectionYaw));
                w.WriteEndElement();
            }

            if (v.SizeWidth != 0.1)
            {
                w.WriteStartElement("Size");
                w.WriteAttributeString("Width", Fmt(v.SizeWidth));
                w.WriteEndElement();
            }

            bool hasLimits = v.MinYaw != -360 || v.MaxYaw != 360 || v.MinPitch != -360 || v.MaxPitch != 360;
            if (hasLimits)
            {
                w.WriteStartElement("Limits");
                w.WriteAttributeString("MinYaw",   Fmt(v.MinYaw));
                w.WriteAttributeString("MaxYaw",   Fmt(v.MaxYaw));
                w.WriteAttributeString("MinPitch", Fmt(v.MinPitch));
                w.WriteAttributeString("MaxPitch", Fmt(v.MaxPitch));
                w.WriteEndElement();
            }

            w.WriteEndElement(); // View
        }

        // Parse

        public static AvPinsData Parse(string xmlText)
        {
            var doc  = XDocument.Parse(xmlText);
            var root = doc.Root;
            var data = new AvPinsData();
            if (root == null) return data;

            // Template — may be attribute OR child element
            data.Template = root.Attribute("Template")?.Value
                         ?? root.Element("Template")?.Value?.Trim()
                         ?? "Default";

            // InitialStates
            var initEl = root.Element("InitialStates");
            if (initEl != null)
                ParseConditions(initEl, data.InitialStates);

            // Views
            var viewsEl = root.Element("Views");
            if (viewsEl != null)
                foreach (var el in viewsEl.Elements("View"))
                    data.Views.Add(ParseView(el));

            // PointOfInterest elements
            foreach (var poiEl in root.Elements("PointOfInterest"))
            {
                var poi = new PointOfInterest
                {
                    Name             = S(poiEl, "Name"),
                    OverrideMode     = poiEl.Attribute("OverrideMode")?.Value ?? "Add",
                    PoiType          = poiEl.Attribute("Type")?.Value           ?? "Pin",
                    IconPath         = S(poiEl, "IconPath"),
                    IconType         = poiEl.Attribute("IconType")?.Value       ?? "None",
                    Icon             = S(poiEl, "Icon"),
                    TutorialAudioId  = poiEl.Attribute("TutorialAudioID")?.Value ?? " ",
                    TutorialTextId   = poiEl.Attribute("TutorialTextID")?.Value  ?? " ",
                    PgPin            = B(poiEl, "PGPin"),
                    AutovistaLite    = B(poiEl, "AutovistaLite"),
                    IsActionable     = B(poiEl, "IsActionable"),
                    HandlesBack      = B(poiEl, "HandlesBack"),
                    HandlesEngineOff = B(poiEl, "HandlesEngineOff"),
                    HandlesExplode   = B(poiEl, "HandlesExplode"),
                    HandlesImplode   = B(poiEl, "HandlesImplode"),
                    SpaceControllerAngle      = D(poiEl, "SpaceControllerAngle"),
                    SpaceControllerAngleError = D(poiEl, "SpaceControllerAngleError"),
                };

                ParseConditions(poiEl.Element("PreConditions"),  poi.PreConditions);
                ParseConditions(poiEl.Element("PostConditions"), poi.PostConditions);
                poi.Visibility = ParseVisibility(poiEl.Element("Visibility"));
                poi.Action     = ParseAction(poiEl.Element("Action"));

                foreach (var attr in poiEl.Attributes())
                    if (!_knownPoiAttrs.Contains(attr.Name.LocalName))
                        poi.ExtraAttributes[attr.Name.LocalName] = attr.Value;

                data.POIs.Add(poi);
            }

            return data;
        }

        // Serialize

        public static string Serialize(AvPinsData data)
        {
            var sb       = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent             = true,
                IndentChars        = "  ",
                OmitXmlDeclaration = true,
                NewLineChars       = "\n",
                Encoding           = new UTF8Encoding(false)
            };

            using var w = XmlWriter.Create(sb, settings);

            w.WriteStartElement("PointsOfInterest");
            if (!string.IsNullOrEmpty(data.Template))
                w.WriteAttributeString("Template", data.Template);

            // InitialStates
            w.WriteStartElement("InitialStates");
            foreach (var c in data.InitialStates)
            {
                w.WriteStartElement(c.IsSet ? "Set" : "UnSet");
                w.WriteAttributeString("State", c.State);
                w.WriteAttributeString("OverrideMode", c.OverrideMode);
                w.WriteEndElement();
            }
            w.WriteEndElement();

            // Views
            w.WriteStartElement("Views");
            foreach (var view in data.Views)
                WriteView(w, view);
            w.WriteEndElement();

            // POIs
            foreach (var poi in data.POIs)
            {
                w.WriteStartElement("PointOfInterest");
                w.WriteAttributeString("Name",           poi.Name);
                if (poi.OverrideMode != "Add")
                    w.WriteAttributeString("OverrideMode", poi.OverrideMode);

                // Skip remaining fields for Remove-mode POIs (they carry no sub-elements)
                if (poi.OverrideMode != "Remove")
                {
                    if (poi.PoiType != "Pin")
                        w.WriteAttributeString("Type", poi.PoiType);
                    if (!string.IsNullOrEmpty(poi.Icon))
                        w.WriteAttributeString("Icon", poi.Icon);

                    w.WriteAttributeString("IconPath",        string.IsNullOrEmpty(poi.IconPath)  ? " " : poi.IconPath);
                    w.WriteAttributeString("IconType",        poi.IconType);
                    w.WriteAttributeString("TutorialAudioID", string.IsNullOrEmpty(poi.TutorialAudioId) ? " " : poi.TutorialAudioId);
                    w.WriteAttributeString("TutorialTextID",  string.IsNullOrEmpty(poi.TutorialTextId)  ? " " : poi.TutorialTextId);

                    if (poi.PgPin)            w.WriteAttributeString("PGPin",            "1");
                    if (poi.AutovistaLite)    w.WriteAttributeString("AutovistaLite",    "1");
                    if (poi.IsActionable)     w.WriteAttributeString("IsActionable",     "1");
                    if (poi.HandlesBack)      w.WriteAttributeString("HandlesBack",      "1");
                    if (poi.HandlesEngineOff) w.WriteAttributeString("HandlesEngineOff", "1");
                    if (poi.HandlesExplode)   w.WriteAttributeString("HandlesExplode",   "1");
                    if (poi.HandlesImplode)   w.WriteAttributeString("HandlesImplode",   "1");

                    if (poi.PoiType == "Space")
                    {
                        w.WriteAttributeString("SpaceControllerAngle",      Fmt(poi.SpaceControllerAngle));
                        w.WriteAttributeString("SpaceControllerAngleError", Fmt(poi.SpaceControllerAngleError));
                    }

                    // Pass-through extra attributes
                    foreach (var kv in poi.ExtraAttributes)
                        w.WriteAttributeString(kv.Key, kv.Value);

                    WriteConditions(w, poi.PreConditions,  "PreConditions");
                    if (poi.Visibility != null)
                        WriteVisibility(w, poi.Visibility);
                    if (poi.Action != null)
                        WriteAction(w, poi.Action);
                    else
                    {
                        w.WriteStartElement("Action");
                        w.WriteEndElement();
                    }
                    WriteConditions(w, poi.PostConditions, "PostConditions");
                }

                w.WriteEndElement(); // PointOfInterest
            }

            w.WriteEndElement(); // PointsOfInterest
            w.Flush();
            return sb.ToString();
        }
    }
}
