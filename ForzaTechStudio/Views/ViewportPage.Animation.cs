using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using ForzaTools.Bundles.Blobs;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SDX = SharpDX;
using Color = Windows.UI.Color;

namespace ForzaTechStudio.Views
{
    // Helper for populating the track-filter ComboBox
    internal sealed class TrackSelectorItem
    {
        // null = play all tracks
        public GrannyTransformTrack? Track { get; init; }
        public string? GroupName { get; init; }

        public string DisplayName
        {
            get
            {
                if (Track == null) return "(All Tracks)";
                string kfInfo = Track.HasAnimationData
                    ? $"{Track.KnotCount} knots"
                    : "identity";
                string curves = "";
                bool hasPos = Track.PositionCurve    != null && !Track.PositionCurve.IsIdentity;
                bool hasOri = Track.OrientationCurve != null && !Track.OrientationCurve.IsIdentity;
                bool hasScl = Track.ScaleShearCurve  != null && !Track.ScaleShearCurve.IsIdentity;
                if (hasPos || hasOri || hasScl)
                    curves = $" [{(hasPos?"P":"")}{(hasOri?"R":"")}{(hasScl?"S":"")}]";
                return $"{Track.Name}{curves}  ({kfInfo})";
            }
        }
    }

    // Animation Playback and Skeleton Visualization
    public sealed partial class ViewportPage : Page
    {
        private DispatcherTimer? _animTimer;
        private bool _isAnimPlaying;
        private float _animCurrentTime;
        private float _animDuration;
        private GrannyAnimation? _currentAnimation;
        private GrannySkeleton? _currentAnimSkeleton;
        private SkeletonNode? _currentAnimSkeletonNode;
        private bool _isUpdatingAnimSlider;

        // null = play all tracks; non-null = only this one track
        private GrannyTransformTrack? _currentTrackFilter;
        private bool _isUpdatingTrackSelector;

        // Stores original bone transforms before animation so we can reset
        private Dictionary<string, Matrix4x4>? _preAnimBoneTransforms;

        // Cached map: bone name ? list of MeshNodes linked to that bone across all ModelBin
        private Dictionary<string, List<MeshNode>>? _boneMeshLinkMap;

        // Stores transforms computed directly from animation tracks for bones not in the GR2 skeleton
        // (e.g., Track "boneDoorLF" exists but GR2 skeleton doesn't have it - compute transform from keyframes)
        private Dictionary<string, Matrix4x4>? _trackOnlyBoneTransforms;

        // Cached GR2 bind-pose world transforms (captured before animation starts)
        private Dictionary<string, Matrix4x4>? _gr2BindPoseWorldTransforms;

        // Cached GR2 inverse bind-pose world transforms from GrannyBone.InverseWorld4x4 (authoritative)
        private Dictionary<string, Matrix4x4>? _gr2InverseBindPoseTransforms;

        // Cached ModelBin skeleton bind-pose world transforms keyed by bone name
        // (parent-chained from SkeletonBlob.Bone.Matrix � same chaining the importer uses)
        private Dictionary<string, Matrix4x4>? _mbBindPoseWorldTransforms;

        // Cached ModelBin skeleton inverse bind-pose world transforms
        private Dictionary<string, Matrix4x4>? _mbInverseBindPoseTransforms;

        // Cached original ModelBin bone transforms per mesh (GeometryData.BoneTransform at load time)
        private Dictionary<MeshNode, Matrix4x4>? _originalMeshBoneTransforms;

        // Per-bone inverse of gr2AnimWorld evaluated at t=0 when Play is pressed.
        // Used as the spatial anchor so the model stays at its original MB position
        // regardless of where the GR2 animation curves start.
        //   finalTransform = mbBind � _animAnchorInverse[bone] � gr2AnimWorld
        private Dictionary<string, Matrix4x4>? _animAnchorInverseTransforms;

        // Cached bone name?index map for the current skeleton, rebuilt only when skeleton changes
        private Dictionary<string, int>? _cachedBoneMap;
        private GrannySkeleton? _cachedBoneMapSkeleton;

        // Per-bone LOCAL transforms (animated) before world-chaining � avoids aliasing in ComputeWorldTransforms
        private Matrix4x4[]? _boneLocalTransforms;

        // Tracks actual wall-clock time for accurate delta-time in the animation timer
        private DateTime _lastAnimTickTime;

        // Path to the currently linked _skeleton.modelbin (or null)
        private string? _linkedSkeletonMbPath;

        private void AnimationSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AnimationSelector.SelectedItem is AnimationClipNode animNode)
            {
                _currentAnimation = animNode.AnimationData;
                _animDuration = animNode.AnimationData.Duration;

                // Clamp duration to last B-spline knot time so the timer doesn't overrun curve data and cause a freeze-then-snap; GR2 Duration can legally exceed maxKnot.
                {
                    float maxKnotTime = 0f;
                    foreach (var tg in animNode.AnimationData.TrackGroups)
                        foreach (var tt in tg.TransformTracks)
                        {
                            if (tt.PositionCurve?.Knots?.Length > 0)
                                maxKnotTime = Math.Max(maxKnotTime, tt.PositionCurve.Knots[^1]);
                            if (tt.OrientationCurve?.Knots?.Length > 0)
                                maxKnotTime = Math.Max(maxKnotTime, tt.OrientationCurve.Knots[^1]);
                            if (tt.ScaleShearCurve?.Knots?.Length > 0)
                                maxKnotTime = Math.Max(maxKnotTime, tt.ScaleShearCurve.Knots[^1]);
                        }
                    if (maxKnotTime > 0f && maxKnotTime < _animDuration)
                        _animDuration = maxKnotTime;
                }

                _animCurrentTime = 0;

                // Build set of track bone names for matching
                var trackBoneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var tg in animNode.AnimationData.TrackGroups)
                {
                    foreach (var tt in tg.TransformTracks)
                    {
                        if (!string.IsNullOrEmpty(tt.Name))
                            trackBoneNames.Add(tt.Name);
                    }
                }

                // First: try the skeleton in the same GrannyFile
                _currentAnimSkeleton = null;
                _currentAnimSkeletonNode = null;

                var parent = animNode.Parent as GrannyFileNode;
                if (parent?.FileData != null && parent.FileData.Skeletons.Count > 0)
                {
                    _currentAnimSkeleton = parent.FileData.Skeletons[0];
                    _currentAnimSkeletonNode = parent.Children.OfType<SkeletonNode>().FirstOrDefault();
                }

                // Second: if no skeleton in same file, or poor match, search all loaded GrannyFiles
                if (_currentAnimSkeleton == null || !HasGoodBoneMatch(_currentAnimSkeleton, trackBoneNames))
                {
                    var allGrannyNodes = ViewModel.GetAllGrannyFileNodes();
                    GrannySkeleton? bestSkeleton = null;
                    SkeletonNode? bestNode = null;
                    int bestMatch = _currentAnimSkeleton != null ? CountBoneMatches(_currentAnimSkeleton, trackBoneNames) : -1;

                    foreach (var gfn in allGrannyNodes)
                    {
                        if (gfn == parent || gfn.FileData == null) continue;
                        foreach (var skel in gfn.FileData.Skeletons)
                        {
                            int matches = CountBoneMatches(skel, trackBoneNames);
                            if (matches > bestMatch)
                            {
                                bestMatch = matches;
                                bestSkeleton = skel;
                                bestNode = gfn.Children.OfType<SkeletonNode>().FirstOrDefault(s => s.SkeletonData == skel);
                            }
                        }
                    }

                    if (bestSkeleton != null && bestNode != null)
                    {
                        _currentAnimSkeleton = bestSkeleton;
                        _currentAnimSkeletonNode = bestNode;
                    }
                }

                // Invalidate cached bone map when skeleton changes
                _cachedBoneMapSkeleton = null;
                _cachedBoneMap = null;
                _boneLocalTransforms = null;

                // Reset skeleton to bind pose before caching transforms
                // (in case a previous animation left WorldTransform in animated state)
                if (_currentAnimSkeleton != null)
                {
                    ResetSkeletonToBindPose(_currentAnimSkeleton);
                }

                // Clear the spatial anchor so it gets rebuilt fresh when Play is pressed
                _animAnchorInverseTransforms = null;

                // Build bone-mesh link map and save pre-animation transforms
                RebuildBoneMeshLinkMap();
                SavePreAnimationTransforms();
                CacheGr2BindPoseTransforms();

                // Build bone name?index map NOW so RenderAnimationPaths can use it
                if (_currentAnimSkeleton != null)
                {
                    _cachedBoneMap = new Dictionary<string, int>(_currentAnimSkeleton.Bones.Count, StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < _currentAnimSkeleton.Bones.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(_currentAnimSkeleton.Bones[i].Name))
                            _cachedBoneMap[_currentAnimSkeleton.Bones[i].Name] = i;
                    }
                    _cachedBoneMapSkeleton = _currentAnimSkeleton;
                }

                _isUpdatingAnimSlider = true;
                AnimTimeSlider.Maximum = _animDuration;
                AnimTimeSlider.Value = 0;
                _isUpdatingAnimSlider = false;

                // Reset track filter to "All Tracks" when a new animation is selected
                _currentTrackFilter = null;
                PopulateTrackSelector(_currentAnimation);

                UpdateAnimTimeDisplay();
                UpdateAnimBoneInfo();
                UpdateBoneMatchDetails();
            }
        }

        private bool HasGoodBoneMatch(GrannySkeleton skeleton, HashSet<string> trackBoneNames)
        {
            if (trackBoneNames.Count == 0) return true;
            int matches = CountBoneMatches(skeleton, trackBoneNames);
            // Consider it a "good" match if >= 50% of tracks match skeleton bones
            return matches >= trackBoneNames.Count * 0.5;
        }

        private int CountBoneMatches(GrannySkeleton skeleton, HashSet<string> trackBoneNames)
        {
            var boneNames = new HashSet<string>(
                skeleton.Bones.Where(b => !string.IsNullOrEmpty(b.Name)).Select(b => b.Name),
                StringComparer.OrdinalIgnoreCase);
            return trackBoneNames.Count(t => boneNames.Contains(t));
        }

        // Track Selector

        // Populates the TrackSelector ComboBox with "(All Tracks)" plus one entry per
        // transform track in the animation, grouped by track-group name.
        private void PopulateTrackSelector(GrannyAnimation anim)
        {
            if (TrackSelector == null) return;

            _isUpdatingTrackSelector = true;
            var items = new List<TrackSelectorItem>
            {
                new TrackSelectorItem { Track = null, GroupName = null }  // "All Tracks"
            };

            if (anim != null)
            {
                foreach (var tg in anim.TrackGroups)
                {
                    string groupName = tg.Name ?? "";
                    foreach (var tt in tg.TransformTracks)
                    {
                        if (string.IsNullOrEmpty(tt.Name)) continue;
                        items.Add(new TrackSelectorItem { Track = tt, GroupName = groupName });
                    }
                }
            }

            TrackSelector.ItemsSource = items;
            TrackSelector.SelectedIndex = 0;
            _isUpdatingTrackSelector = false;

            // Show keyframe info for "all tracks" is blank
            UpdateTrackKeyframeInfo(null);
        }

        // Called when the user picks a specific track (or "All Tracks") from the selector.
        private void TrackSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingTrackSelector) return;
            if (TrackSelector.SelectedItem is TrackSelectorItem item)
            {
                _currentTrackFilter = item.Track;
                UpdateTrackKeyframeInfo(_currentTrackFilter);
            }
        }

        // Updates the keyframe detail text block for the selected track.
        // Shows time, position, orientation, scale for every sampled keyframe.
        private void UpdateTrackKeyframeInfo(GrannyTransformTrack? track)
        {
            if (TrackKeyframeInfoText == null) return;

            if (track == null || !track.HasAnimationData)
            {
                TrackKeyframeInfoText.Visibility = Visibility.Collapsed;
                TrackKeyframeInfoText.Text = "";
                return;
            }

            TrackKeyframeInfoText.Visibility = Visibility.Visible;

            var positionCurve = track.PositionCurve;
            var orientationCurve = track.OrientationCurve;
            var scaleShearCurve = track.ScaleShearCurve;

            bool hasPos = positionCurve    != null && !positionCurve.IsIdentity;
            bool hasOri = orientationCurve != null && !orientationCurve.IsIdentity;
            bool hasScl = scaleShearCurve  != null && !scaleShearCurve.IsIdentity;

            var lines = new System.Text.StringBuilder();
            lines.AppendLine($"Track: {track.Name}   {track.KnotCount} knots");
            lines.AppendLine($"Curves: {(hasPos?"Pos ":"")}{(hasOri?"Ori ":"")}{(hasScl?"Scale":"")}");

            // Sample up to 8 evenly-spaced points along the curves for a quick preview
            float maxKnot = 0f;
            if (hasPos && positionCurve?.Knots?.Length > 0)
                maxKnot = Math.Max(maxKnot, positionCurve.Knots[^1]);
            if (hasOri && orientationCurve?.Knots?.Length > 0)
                maxKnot = Math.Max(maxKnot, orientationCurve.Knots[^1]);
            if (hasScl && scaleShearCurve?.Knots?.Length > 0)
                maxKnot = Math.Max(maxKnot, scaleShearCurve.Knots[^1]);

            int show = Math.Min(track.KnotCount, 8);
            if (show > 0 && maxKnot > 0f)
            {
                for (int i = 0; i < show; i++)
                {
                    float t = maxKnot * i / Math.Max(show - 1, 1);
                    GrannyBSplineEvaluator.SampleTransformTrack(track, t,
                        out var pos, out var ori, out var scl, out _);
                    string posStr = hasPos ? $"P({pos.X:F3},{pos.Y:F3},{pos.Z:F3})" : "";
                    string oriStr = hasOri ? $"Q({ori.X:F3},{ori.Y:F3},{ori.Z:F3},{ori.W:F3})" : "";
                    string sclStr = hasScl ? $"S({scl.X:F3},{scl.Y:F3},{scl.Z:F3})" : "";
                    string parts  = string.Join(" ", new[] { posStr, oriStr, sclStr }.Where(s => s.Length > 0));
                    lines.AppendLine($"  [{i:D2}] t={t:F3}s  {parts}");
                }
            }

            TrackKeyframeInfoText.Text = lines.ToString().TrimEnd();
        }

        // Skeleton ModelBin Linking

        // Handles the "Link Skel MB" button click � lets the user pick a _skeleton.modelbin
        // file manually and links it for animation bind-pose caching.
        private async void LinkSkeletonMb_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.FileTypeFilter.Add(".modelbin");
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            ApplySkeletonModelBinBindPose(file.Path);
        }

        // When any file is loaded into the 3-D viewer, check whether it is a _skeleton.modelbin
        // and, if so, automatically apply its bind pose.  Also scans loaded files for a match.
        private void TryAutoLinkSkeletonModelBin()
        {
            // Look for any ModelBinNode whose filename ends with _skeleton.modelbin (case-insensitive)
            foreach (var mb in ViewModel.GetAllModelBinNodes())
            {
                string fname = System.IO.Path.GetFileName(mb.FilePath ?? mb.FileName ?? "");
                if (fname.EndsWith("_skeleton.modelbin", StringComparison.OrdinalIgnoreCase) ||
                    fname.Equals("skeleton.modelbin", StringComparison.OrdinalIgnoreCase))
                {
                    string? effectivePath = mb.FilePath ?? mb.FileName;
                    if (_linkedSkeletonMbPath != effectivePath)
                    {
                        _linkedSkeletonMbPath = effectivePath;
                        BuildMbBindPoseFromNode(mb);
                        UpdateSkeletonMbLinkText();
                    }
                    return;
                }
            }
        }

        // Loads a _skeleton.modelbin from disk, parses its SkeletonBlob, and builds the bind pose transforms.
        private void ApplySkeletonModelBinBindPose(string filePath)
        {
            try
            {
                var bundle = new ForzaTools.Bundles.Bundle();
                using (var stream = System.IO.File.OpenRead(filePath))
                {
                    bundle.Load(stream);
                }
                
                var skelBlob = bundle.Blobs.OfType<SkeletonBlob>().FirstOrDefault();
                if (skelBlob == null || skelBlob.Bones.Count == 0) return;

                _linkedSkeletonMbPath = filePath;
                BuildMbBindPoseFromSkelBlob(skelBlob);
                UpdateSkeletonMbLinkText();
            }
            catch { /* file unreadable � silently ignore */ }
        }

        // Builds the mb bind-pose world transform cache from a live ModelBinNode that already
        // has its SkeletonBlob loaded in memory (no disk re-read needed).
        private void BuildMbBindPoseFromNode(ModelBinNode mb)
        {
            var skelBlob = mb.Bundle?.Blobs.OfType<SkeletonBlob>().FirstOrDefault();
            if (skelBlob == null) return;
            BuildMbBindPoseFromSkelBlob(skelBlob);
        }

        // Chains bone local transforms parent-first to produce world-space bind-pose matrices.
        // Bones are stored in topological order (parent index always less than child).
        private void BuildMbBindPoseFromSkelBlob(SkeletonBlob skelBlob)
        {
            if (_mbBindPoseWorldTransforms == null)
                _mbBindPoseWorldTransforms = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
            if (_mbInverseBindPoseTransforms == null)
                _mbInverseBindPoseTransforms = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);

            var worldArr = new Matrix4x4[skelBlob.Bones.Count];
            for (int i = 0; i < skelBlob.Bones.Count; i++)
            {
                var b = skelBlob.Bones[i];
                // world = local � parent  (row-vector, matches game UpdateWorldTransforms)
                worldArr[i] = (b.ParentId >= 0 && b.ParentId < i)
                    ? b.Matrix * worldArr[b.ParentId]
                    : b.Matrix;

                if (string.IsNullOrEmpty(b.Name)) continue;

                // Always overwrite � skeleton.modelbin is the authoritative bind pose
                _mbBindPoseWorldTransforms[b.Name] = worldArr[i];
                if (Matrix4x4.Invert(worldArr[i], out var inv))
                    _mbInverseBindPoseTransforms[b.Name] = inv;
            }
        }

        private void UpdateSkeletonMbLinkText()
        {
            if (SkeletonMbLinkText == null) return;
            if (string.IsNullOrEmpty(_linkedSkeletonMbPath))
            {
                SkeletonMbLinkText.Text = "No skeleton.modelbin linked";
            }
            else
            {
                int boneCount = _mbBindPoseWorldTransforms?.Count ?? 0;
                SkeletonMbLinkText.Text =
                    $"Linked: {System.IO.Path.GetFileName(_linkedSkeletonMbPath)} ({boneCount} bones)";
            }
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAnimation == null) return;

            if (_isAnimPlaying)
            {
                StopAnimTimer();
                PlayPauseIcon.Glyph = "\uE768"; // Play icon
            }
            else
            {
                // Ensure we have saved transforms before starting
                if (_preAnimBoneTransforms == null || _preAnimBoneTransforms.Count == 0)
                    SavePreAnimationTransforms();

                // Cache GR2 bind-pose world transforms before animation starts
                if (_gr2BindPoseWorldTransforms == null || _gr2BindPoseWorldTransforms.Count == 0)
                    CacheGr2BindPoseTransforms();

                // Build spatial anchor: evaluate the animation at t=0 and record the
                // inverse of each bone's world transform. This ensures the model stays at
                // its original MB position even if the GR2 curves don't start at the bind pose.
                //   finalTransform = mbBind � anchorInverse[bone] � gr2AnimWorld(t)
                // At t=0: anchorInverse � gr2AnimWorld(0) = Identity ? result = mbBind ?
                if (_currentAnimSkeleton != null && _currentAnimation != null)
                {
                    ApplyAnimationPose(_currentAnimSkeleton, _currentAnimation, 0f);
                    _animAnchorInverseTransforms = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
                    foreach (var bone in _currentAnimSkeleton.Bones)
                    {
                        if (string.IsNullOrEmpty(bone.Name)) continue;
                        if (Matrix4x4.Invert(bone.WorldTransform, out var anchorInv))
                            _animAnchorInverseTransforms[bone.Name] = anchorInv;
                    }
                    // Build anchors for track-only bones (not in GR2 skeleton) so their meshes stay at MB position rather than GR2 world-origin.
                    CacheTrackOnlyAnchors();
                }

                StartAnimTimer();
                PlayPauseIcon.Glyph = "\uE769"; // Pause icon
            }
        }

        private void StopAnimation_Click(object sender, RoutedEventArgs e)
        {
            StopAnimTimer();
            ResetAnimationToStart();
        }

        // Resets animation to t=0 and restores the bind pose.
        private void ResetAnimationToStart()
        {
            _animCurrentTime = 0;
            PlayPauseIcon.Glyph = "\uE768";

            _isUpdatingAnimSlider = true;
            AnimTimeSlider.Value = 0;
            _isUpdatingAnimSlider = false;

            UpdateAnimTimeDisplay();

            // Reset skeleton to bind pose
            if (_currentAnimSkeleton != null)
            {
                ResetSkeletonToBindPose(_currentAnimSkeleton);
                RefreshSkeletonRendering();
            }

            // Restore pre-animation bone transforms on all ModelBin meshes
            RestorePreAnimationTransforms();

            // Clear cached bind-pose transforms so they get recaptured next time.
            // Preserve _mbBindPoseWorldTransforms if a skeleton.modelbin is linked � its
            // bind pose is authoritative and does not change between animations.
            _gr2BindPoseWorldTransforms = null;
            _gr2InverseBindPoseTransforms = null;
            _animAnchorInverseTransforms = null;
            if (string.IsNullOrEmpty(_linkedSkeletonMbPath))
            {
                _mbBindPoseWorldTransforms = null;
                _mbInverseBindPoseTransforms = null;
            }
        }

        // Resets a skeleton's WorldTransform values to bind pose by computing
        // from LocalTransform with proper parent chaining (world = local � parent).
        private void ResetSkeletonToBindPose(GrannySkeleton skeleton)
        {
            for (int i = 0; i < skeleton.Bones.Count; i++)
            {
                var bone = skeleton.Bones[i];
                var local = bone.LocalTransform.ToMatrix();
                // world = local � parent  (row-vector convention, matches ColumnMatrixMultiply4x3)
                bone.WorldTransform = (bone.ParentIndex >= 0 && bone.ParentIndex < i)
                    ? local * skeleton.Bones[bone.ParentIndex].WorldTransform
                    : local;
            }
        }

        private void AnimTimeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isUpdatingAnimSlider) return;

            _animCurrentTime = (float)e.NewValue;
            UpdateAnimTimeDisplay();

            if (_currentAnimation != null)
            {
                if (_currentAnimSkeleton != null)
                    ApplyAnimationPose(_currentAnimSkeleton, _currentAnimation, _animCurrentTime);
                RefreshSkeletonRendering();
            }
        }

        private void AnimSpeedSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            var speedText = this.FindName("SpeedValueText") as TextBlock;
            if (speedText != null)
                speedText.Text = $"{e.NewValue:F1}x";
            UpdateAnimTimeDisplay();
        }

        private void StartAnimTimer()
        {
            if (_animTimer == null)
            {
                _animTimer = new DispatcherTimer();
                _animTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60fps
                _animTimer.Tick += AnimTimer_Tick;
            }

            _lastAnimTickTime = DateTime.UtcNow;
            _isAnimPlaying = true;
            _animTimer.Start();
        }

        private void StopAnimTimer()
        {
            _isAnimPlaying = false;
            _animTimer?.Stop();
        }

        private void AnimTimer_Tick(object? sender, object e)
        {
            if (_currentAnimation == null) return;

            // Use actual elapsed wall-clock time for accurate playback speed
            var now = DateTime.UtcNow;
            float dt = (float)(now - _lastAnimTickTime).TotalSeconds;
            _lastAnimTickTime = now;

            // Clamp dt to avoid huge jumps if the timer fires late (e.g. window was minimized)
            dt = Math.Min(dt, 0.1f);

            float speed = (float)(AnimSpeedSlider?.Value ?? 1.0);
            _animCurrentTime += dt * speed;

            // Loop
            if (_animCurrentTime >= _animDuration)
            {
                if (_currentAnimation.DefaultLoopCount == 0)
                {
                    _animCurrentTime %= _animDuration;
                }
                else
                {
                    StopAnimTimer();
                    ResetAnimationToStart();
                    return;
                }
            }

            _isUpdatingAnimSlider = true;
            AnimTimeSlider.Value = _animCurrentTime;
            _isUpdatingAnimSlider = false;

            UpdateAnimTimeDisplay();

            // Apply animation pose and update rendering
            if (_currentAnimSkeleton != null && _currentAnimation != null)
                ApplyAnimationPose(_currentAnimSkeleton, _currentAnimation, _animCurrentTime);
            RefreshSkeletonRendering();
        }

        private void ApplyAnimationPose(GrannySkeleton skeleton, GrannyAnimation anim, float time)
        {
            // Rebuild the bone name?index map only when the skeleton changes (not every tick)
            if (skeleton != null && !ReferenceEquals(skeleton, _cachedBoneMapSkeleton))
            {
                _cachedBoneMap = new Dictionary<string, int>(skeleton.Bones.Count, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < skeleton.Bones.Count; i++)
                {
                    if (!string.IsNullOrEmpty(skeleton.Bones[i].Name))
                        _cachedBoneMap[skeleton.Bones[i].Name] = i;
                }
                _cachedBoneMapSkeleton = skeleton;
            }

            var boneMap = skeleton != null ? _cachedBoneMap
                          : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // Allocate / reuse a separate local-transform array so ComputeWorldTransforms
            // never reads a value that was already partially written to WorldTransform.
            int boneCount = skeleton?.Bones.Count ?? 0;
            if (_boneLocalTransforms == null || _boneLocalTransforms.Length != boneCount)
                _boneLocalTransforms = new Matrix4x4[boneCount];
            var localTransforms = _boneLocalTransforms;
            if (localTransforms == null)
                return;

            // Track which skeleton bones are driven this frame; undriven root bones may have a non-zero GR2 bind-pose translation that would shift child-bone rotation pivots.
            bool[] updatedBones = new bool[boneCount];

            if (skeleton != null)
            {
                // Seed every bone with its bind-pose local transform.
                for (int i = 0; i < boneCount; i++)
                    localTransforms[i] = skeleton.Bones[i].LocalTransform.ToMatrix();
            }

            // Clear track-only transforms
            _trackOnlyBoneTransforms ??= new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
            _trackOnlyBoneTransforms.Clear();

            // Apply animation tracks � write into _boneLocalTransforms (not WorldTransform)
            // When _currentTrackFilter is set, only process that one specific track;
            // all other bones stay at their bind-pose local transform (seeded above).
            foreach (var trackGroup in anim.TrackGroups)
            {
                foreach (var track in trackGroup.TransformTracks)
                {
                    if (string.IsNullOrEmpty(track.Name)) continue;

                    // Respect track filter � when a specific track is chosen, only draw its path
                    if (_currentTrackFilter != null && !ReferenceEquals(track, _currentTrackFilter))
                        continue;

                    int boneIdx = -1;
                    bool inSkeleton = skeleton != null && boneMap.TryGetValue(track.Name, out boneIdx);

                    if (track.HasAnimationData)
                    {
                        GrannyBSplineEvaluator.SampleTransformTrack(track, time,
                            out var sampledPos, out var sampledOri,
                            out var sampledScl, out var sampledSS9);

                        if (inSkeleton && skeleton != null)
                        {
                            var bone = skeleton.Bones[boneIdx];
                            var bindPose = bone.LocalTransform;

                            bool posIdentity  = track.PositionCurve    == null || track.PositionCurve.IsIdentity;
                            bool oriIdentity  = track.OrientationCurve == null || track.OrientationCurve.IsIdentity;
                            bool sclIdentity  = track.ScaleShearCurve  == null || track.ScaleShearCurve.IsIdentity;

                            if (posIdentity && oriIdentity && sclIdentity)
                            {
                                // All curves are identity � leave the bind-pose local matrix already set.
                                continue;
                            }

                            Matrix4x4 animLocal;

                            if (sclIdentity)
                            {
                                // Preserve the full bind-pose scale/shear matrix (including shear terms).
                                // Only replace position and/or orientation from the animation track.
                                var bindMat = bindPose.ToMatrix();

                                if (!oriIdentity)
                                {
                                    var rot = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(sampledOri));
                                    Matrix4x4 scaleShear;
                                    if ((bindPose.Flags & 0x4) != 0)
                                    {
                                        scaleShear = new Matrix4x4(
                                            bindPose.ScaleShear0.X, bindPose.ScaleShear0.Y, bindPose.ScaleShear0.Z, 0f,
                                            bindPose.ScaleShear1.X, bindPose.ScaleShear1.Y, bindPose.ScaleShear1.Z, 0f,
                                            bindPose.ScaleShear2.X, bindPose.ScaleShear2.Y, bindPose.ScaleShear2.Z, 0f,
                                            0f, 0f, 0f, 1f);
                                    }
                                    else
                                    {
                                        scaleShear = Matrix4x4.Identity;
                                    }
                                    animLocal = scaleShear * rot;
                                }
                                else
                                {
                                    // Keep bind-pose rotation+scale submatrix
                                    animLocal = new Matrix4x4(
                                        bindMat.M11, bindMat.M12, bindMat.M13, 0f,
                                        bindMat.M21, bindMat.M22, bindMat.M23, 0f,
                                        bindMat.M31, bindMat.M32, bindMat.M33, 0f,
                                        0f, 0f, 0f, 1f);
                                }

                                var pos = posIdentity ? bindPose.Position : sampledPos;
                                animLocal.M41 = pos.X;
                                animLocal.M42 = pos.Y;
                                animLocal.M43 = pos.Z;
                                animLocal.M44 = 1f;
                            }
                            else
                            {
                                // Scale/shear curve is animated � build full TRS from sampled values.
                                // Granny's convention (per BuildCompositeTransform4x4 @ 0x140a5eb60):
                                //   composite_3x3 = scale_3x3 * rotation_3x3
                                //   output = [ composite | 0 ; translation | 1 ]
                                var ori = oriIdentity ? bindPose.Orientation : sampledOri;
                                var pos = posIdentity ? bindPose.Position    : sampledPos;

                                Matrix4x4 scaleShear;
                                if (sampledSS9 != null)
                                {
                                    // Full 3�3 scale/shear matrix available (dim==9 curve)
                                    float[] ss = sampledSS9;
                                    scaleShear = new Matrix4x4(
                                        ss[0], ss[1], ss[2], 0f,
                                        ss[3], ss[4], ss[5], 0f,
                                        ss[6], ss[7], ss[8], 0f,
                                        0f,    0f,    0f,    1f);
                                }
                                else
                                {
                                    scaleShear = Matrix4x4.CreateScale(sampledScl);
                                }

                                animLocal = scaleShear *
                                            Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(ori)) *
                                            Matrix4x4.CreateTranslation(pos);
                            }

                            // Write into the local-transform scratch array � NOT WorldTransform yet.
                            localTransforms[boneIdx] = animLocal;
                            updatedBones[boneIdx] = true;
                        }
                        else
                        {
                            // Track references a bone not present in the GR2 skeleton.
                            // Store its local TRS; parent chaining is done in UpdateModelBinWithSkeleton.
                            // Mirror the GR2 skeleton-bone path: use the full 3x3 scale/shear
                            // matrix when dimension-9 curve data is available.
                            Matrix4x4 trackScaleShear;
                            if (sampledSS9 != null)
                            {
                                float[] ss = sampledSS9;
                                trackScaleShear = new Matrix4x4(
                                    ss[0], ss[1], ss[2], 0f,
                                    ss[3], ss[4], ss[5], 0f,
                                    ss[6], ss[7], ss[8], 0f,
                                    0f,    0f,    0f,    1f);
                            }
                            else
                            {
                                trackScaleShear = Matrix4x4.CreateScale(sampledScl);
                            }
                            _trackOnlyBoneTransforms[track.Name] =
                                trackScaleShear *
                                Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(sampledOri)) *
                                Matrix4x4.CreateTranslation(sampledPos);
                        }
                    }
                    else if (!inSkeleton)
                    {
                        // No keyframes and not in skeleton � identity local transform
                        _trackOnlyBoneTransforms[track.Name] = Matrix4x4.Identity;
                    }
                }
            }

            // Fix: strip bind-pose translation from any root bone that was not driven by an
            // animation track. FH5 GR2 files are sometimes exported at a non-origin world
            // position, so GrannyRootBone.LocalTransform carries a non-zero translation even
            // though no "GrannyRootBone" track exists in the animation. If left in place, that
            // offset leaks into all child bone world transforms and shifts the rotation pivot,
            // producing the wrong animation position for child bones such as door hinges.
            // FH3 is unaffected because its root bind-pose translation is (0, 0, 0).
            if (skeleton != null)
            {
                for (int i = 0; i < boneCount; i++)
                {
                    if (skeleton.Bones[i].ParentIndex < 0 && !updatedBones[i])
                    {
                        // Root bone was not driven by any animation track this frame.
                        // Reset the entire local transform to Identity so that neither the
                        // bind-pose translation nor any bind-pose rotation leaks into child
                        // bone world transforms and shifts the animation pivot incorrectly.
                        // FH5 GR2 files are sometimes exported with a non-zero world-space
                        // position AND orientation in GrannyRootBone.LocalTransform; both must
                        // be stripped here or the door/wheel hinge is displaced from its
                        // correct car-body position.
                        localTransforms[i] = Matrix4x4.Identity;
                    }
                }
            }

            // Compute world transforms from the clean local-transform array.
            // world[i] = local[i] � world[parent[i]]
            // Granny guarantees parent index < child index, so a single forward pass is correct.
            if (skeleton != null)
            {
                ComputeWorldTransforms(skeleton);
            }
        }

        // Chains bone local transforms to world space.
        // Formula: world[i] = local[i] * world[parent[i]]
        // Granny guarantees parent index is always less than child index.
        private void ComputeWorldTransforms(GrannySkeleton skeleton)
        {
            var localTransforms = _boneLocalTransforms;
            if (localTransforms == null)
                return;

            for (int i = 0; i < skeleton.Bones.Count; i++)
            {
                var bone = skeleton.Bones[i];
                // Read from the clean local-transform scratch array � never from WorldTransform.
                var local = localTransforms[i];
                bone.WorldTransform = (bone.ParentIndex >= 0 && bone.ParentIndex < i)
                    ? local * skeleton.Bones[bone.ParentIndex].WorldTransform
                    : local;
            }
        }

        private void RefreshSkeletonRendering()
        {
            // Drive ModelBin meshes through the bone-remap path.
            bool hasSkeleton = _currentAnimSkeleton != null && _currentAnimSkeleton.Bones.Count > 0;
            bool hasTrackOnly = _trackOnlyBoneTransforms != null && _trackOnlyBoneTransforms.Count > 0;
            if (!hasSkeleton && !hasTrackOnly) return;

            var modelBins = ViewModel.GetAllModelBinNodes();
            foreach (var mb in modelBins)
            {
                UpdateModelBinWithSkeleton(mb, _currentAnimSkeleton);
            }
        }

        // Caches the GR2 skeleton bind-pose world transforms before animation starts.
        // These are used to compute delta transforms during animation playback.
        private void CacheGr2BindPoseTransforms()
        {
            _gr2BindPoseWorldTransforms = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
            _gr2InverseBindPoseTransforms = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
            _mbBindPoseWorldTransforms = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
            _mbInverseBindPoseTransforms = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);

            // GR2 skeleton bind pose 
            if (_currentAnimSkeleton != null)
            {
                var bindPoseWorld = new Matrix4x4[_currentAnimSkeleton.Bones.Count];
                for (int i = 0; i < _currentAnimSkeleton.Bones.Count; i++)
                {
                    var bone = _currentAnimSkeleton.Bones[i];


                    Matrix4x4 local = (bone.ParentIndex < 0)
                        ? Matrix4x4.Identity
                        : bone.LocalTransform.ToMatrix();

                    // world = local  parent  (row-vector convention)
                    bindPoseWorld[i] = (bone.ParentIndex >= 0 && bone.ParentIndex < i)
                        ? local * bindPoseWorld[bone.ParentIndex]
                        : local;

                    if (string.IsNullOrEmpty(bone.Name)) continue;
                    _gr2BindPoseWorldTransforms[bone.Name] = bindPoseWorld[i];

                    if (Matrix4x4.Invert(bindPoseWorld[i], out var inv))
                        _gr2InverseBindPoseTransforms[bone.Name] = inv;
                }
            }

            //  ModelBin skeleton bind pose 

            foreach (var mb in ViewModel.GetAllModelBinNodes())
            {
                SkeletonBlob? skelBlob = mb.Bundle?.Blobs.OfType<SkeletonBlob>().FirstOrDefault();
                if (skelBlob == null || skelBlob.Bones.Count == 0) continue;

                var mbWorld = new Matrix4x4[skelBlob.Bones.Count];
                for (int i = 0; i < skelBlob.Bones.Count; i++)
                {
                    var b = skelBlob.Bones[i];
                    // world = local  parent  (matches ModelImporter: m = b.Matrix * boneMatrices[parentId])
                    mbWorld[i] = (b.ParentId >= 0 && b.ParentId < i)
                        ? b.Matrix * mbWorld[b.ParentId]
                        : b.Matrix;

                    if (string.IsNullOrEmpty(b.Name)) continue;

                    // Later ModelBins win only if the bone isn't already cached.
                    if (!_mbBindPoseWorldTransforms.ContainsKey(b.Name))
                    {
                        _mbBindPoseWorldTransforms[b.Name] = mbWorld[i];
                        if (Matrix4x4.Invert(mbWorld[i], out var invMb))
                            _mbInverseBindPoseTransforms[b.Name] = invMb;
                    }
                }
            }
        }

        private void UpdateModelBinWithSkeleton(ModelBinNode modelBin, GrannySkeleton? skeleton)
        {
            //  Step 1: Build bone-name - animated world transform map 
            var gr2BoneTransforms = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
            if (skeleton != null)
            {
                foreach (var bone in skeleton.Bones)
                {
                    if (!string.IsNullOrEmpty(bone.Name))
                        gr2BoneTransforms[bone.Name] = bone.WorldTransform;
                }
            }
            if (_trackOnlyBoneTransforms != null)
            {
                foreach (var kvp in _trackOnlyBoneTransforms)
                {
                    if (!gr2BoneTransforms.ContainsKey(kvp.Key))
                        gr2BoneTransforms[kvp.Key] = kvp.Value;
                }
            }
            if (gr2BoneTransforms.Count == 0) return;

            // Step 2: GR2 skeleton bone name set (O(1) membership) 
            var gr2SkeletonBoneNames = skeleton != null
                ? new HashSet<string>(
                    skeleton.Bones.Where(b => !string.IsNullOrEmpty(b.Name)).Select(b => b.Name!),
                    StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Step 3: Get the ModelBin SkeletonBlob once
            SkeletonBlob? skelBlob = modelBin.Bundle?.Blobs.OfType<SkeletonBlob>().FirstOrDefault();

            // Step 4: Build mesh list via link-map (O(1)) or direct children scan
            var boneMeshLinkMap = _boneMeshLinkMap;
            IEnumerable<MeshNode> meshesToProcess;
            if (boneMeshLinkMap != null && boneMeshLinkMap.Count > 0)
            {
                meshesToProcess = gr2BoneTransforms.Keys
                    .Where(boneMeshLinkMap.ContainsKey)
                    .SelectMany(n => boneMeshLinkMap[n])
                    .Where(m => m?.GeometryData != null &&
                                (m.ParentModelBin == modelBin || (m.Parent as ModelBinNode) == modelBin))
                    .Distinct();
            }
            else
            {
                meshesToProcess = modelBin.Children.OfType<MeshNode>();
            }

            foreach (var mesh in meshesToProcess)
            {
                if (mesh.GeometryData == null) continue;

                // Resolve the bone name for this mesh
                string? boneName = null;
                short boneIdx = mesh.GeometryData.BoneIndex;
                if (skelBlob != null && boneIdx >= 0 && boneIdx < skelBlob.Bones.Count)
                    boneName = skelBlob.Bones[boneIdx].Name;
                if (string.IsNullOrEmpty(boneName))
                    boneName = mesh.GeometryData.BoneName;
                if (string.IsNullOrEmpty(boneName) && mesh.GeometryData.SourceBone != null)
                    boneName = mesh.GeometryData.SourceBone.Name;

                if (string.IsNullOrEmpty(boneName) || !gr2BoneTransforms.TryGetValue(boneName, out var animatedWorld))
                    continue;

                Matrix4x4 finalTransform;

                if (gr2SkeletonBoneNames.Contains(boneName))
                {
                    // GR2 skeleton bone 

                    Matrix4x4 mbBind = Matrix4x4.Identity;
                    Matrix4x4 gr2InvBind = Matrix4x4.Identity;

                    bool mbFound = _mbBindPoseWorldTransforms != null &&
                                   _mbBindPoseWorldTransforms.TryGetValue(boneName, out mbBind);
                    if (!mbFound)
                    {
                        if (_originalMeshBoneTransforms != null &&
                            _originalMeshBoneTransforms.TryGetValue(mesh, out var origMb))
                            mbBind = origMb;
                        else
                            mbBind = mesh.GeometryData?.OriginalBoneTransform ?? Matrix4x4.Identity;
                    }

                    // Prefer the spatial anchor (inverse of gr2AnimWorld at t=0) so the
                    // model stays at its original position when the animation starts.
                    // Fall back to the static bind-pose inverse if anchor isn't built yet.
                    bool anchorFound = _animAnchorInverseTransforms != null &&
                                       _animAnchorInverseTransforms.TryGetValue(boneName, out gr2InvBind);
                    if (!anchorFound && _gr2InverseBindPoseTransforms != null)
                        _gr2InverseBindPoseTransforms.TryGetValue(boneName, out gr2InvBind);

                    finalTransform = mbBind * gr2InvBind * animatedWorld;

                    if (!IsFiniteMatrix(finalTransform))
                        finalTransform = IsFiniteMatrix(animatedWorld) ? animatedWorld : Matrix4x4.Identity;
                }
                else
                {
                    // Track-only bone (not in GR2 skeleton)
                    // Chain track-only local TRS with animated parent to get GR2 world.
                    Matrix4x4 parentWorld = GetTrackOnlyParentWorld(boneIdx, skelBlob, gr2BoneTransforms, gr2SkeletonBoneNames);
                    Matrix4x4 chainedWorld = animatedWorld * parentWorld;

                    // Prefer anchor-based formula: keeps the mesh at its original MB

                    Matrix4x4 mbBind = mesh.GeometryData?.OriginalBoneTransform ?? Matrix4x4.Identity;
                    if (_mbBindPoseWorldTransforms != null &&
                        _mbBindPoseWorldTransforms.TryGetValue(boneName, out var mbBindLookup))
                        mbBind = mbBindLookup;

                    if (_animAnchorInverseTransforms != null &&
                        _animAnchorInverseTransforms.TryGetValue(boneName, out var trackAnchorInv))
                    {
                        finalTransform = mbBind * trackAnchorInv * chainedWorld;
                    }
                    else if (_mbBindPoseWorldTransforms != null &&
                             _mbBindPoseWorldTransforms.TryGetValue(boneName, out var mbBindBridge) &&
                             _gr2BindPoseWorldTransforms != null &&
                             _gr2BindPoseWorldTransforms.TryGetValue(boneName, out var gr2BindBridge) &&
                             Matrix4x4.Invert(gr2BindBridge, out var gr2InvBindBridge))
                    {
                        // Fallback: static bind-pose bridge (used when slider is dragged
                        // before Play is pressed and no anchor has been built yet).
                        finalTransform = mbBindBridge * gr2InvBindBridge * chainedWorld;
                    }
                    else
                    {
                        // No bridge or anchor available - use raw chained GR2 world.
                        // Animation will play but may be offset from the MB position.
                        finalTransform = chainedWorld;
                    }

                    if (!IsFiniteMatrix(finalTransform))
                        finalTransform = Matrix4x4.Identity;
                }

                UpdateMeshRenderingWithBoneTransform(mesh, finalTransform);
            }
        }

        // Builds spatial anchors for track-only bones (bones in animation tracks but absent from the GR2 skeleton).
        // Stores Inverse(chainedWorld(t=0)) in _animAnchorInverseTransforms.
        private void CacheTrackOnlyAnchors()
        {
            if (_trackOnlyBoneTransforms == null || _trackOnlyBoneTransforms.Count == 0) return;
            if (_animAnchorInverseTransforms == null) return;

            // Snapshot of all bone world transforms at t=0 (GR2 skeleton + track-only locals).
            var gr2BoneTransforms = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
            if (_currentAnimSkeleton != null)
            {
                foreach (var b in _currentAnimSkeleton.Bones)
                    if (!string.IsNullOrEmpty(b.Name))
                        gr2BoneTransforms[b.Name] = b.WorldTransform;
            }
            foreach (var kvp in _trackOnlyBoneTransforms)
                if (!gr2BoneTransforms.ContainsKey(kvp.Key))
                    gr2BoneTransforms[kvp.Key] = kvp.Value;

            var gr2SkeletonBoneNames = _currentAnimSkeleton != null
                ? new HashSet<string>(
                    _currentAnimSkeleton.Bones.Where(b => !string.IsNullOrEmpty(b.Name)).Select(b => b.Name),
                    StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var mb in ViewModel.GetAllModelBinNodes())
            {
                SkeletonBlob? skelBlob = mb.Bundle?.Blobs.OfType<SkeletonBlob>().FirstOrDefault();
                if (skelBlob == null) continue;

                foreach (var kvp in _trackOnlyBoneTransforms)
                {
                    string boneName = kvp.Key;
                    if (_animAnchorInverseTransforms.ContainsKey(boneName)) continue; // already cached

                    // Find the bone index in the MB skeleton by name.
                    short boneIdx = -1;
                    for (short bi = 0; bi < skelBlob.Bones.Count; bi++)
                    {
                        if (string.Equals(skelBlob.Bones[bi].Name, boneName, StringComparison.OrdinalIgnoreCase))
                        { boneIdx = bi; break; }
                    }
                    if (boneIdx < 0) continue; // not in this ModelBin's skeleton

                    // chainedWorld(t=0) = localTRS(t=0) � parentWorld(t=0)
                    Matrix4x4 parentWorld0 = GetTrackOnlyParentWorld(boneIdx, skelBlob, gr2BoneTransforms, gr2SkeletonBoneNames);
                    Matrix4x4 chainedWorld0 = kvp.Value * parentWorld0;
                    if (Matrix4x4.Invert(chainedWorld0, out var inv))
                        _animAnchorInverseTransforms[boneName] = inv;
                }
            }

            // Fallback: track-only bones whose name does not appear in any loaded MB skeleton
            foreach (var kvp in _trackOnlyBoneTransforms)
            {
                if (_animAnchorInverseTransforms.ContainsKey(kvp.Key)) continue;
                if (Matrix4x4.Invert(kvp.Value, out var inv))
                    _animAnchorInverseTransforms[kvp.Key] = inv;
            }
        }

        // Returns the world transform of the parent of a track-only bone 
        private Matrix4x4 GetTrackOnlyParentWorld(
            short boneIdx,
            SkeletonBlob? skelBlob,
            Dictionary<string, Matrix4x4> gr2BoneTransforms,
            HashSet<string> gr2SkeletonBoneNames)
        {
            if (skelBlob == null || boneIdx < 0 || boneIdx >= skelBlob.Bones.Count)
                return Matrix4x4.Identity;

            // Walk up the ModelBin parent chain, accumulating local transforms for
            // track-only ancestors, until we find a GR2-skeleton ancestor or the root.
            var chainLocals = new List<Matrix4x4>();
            short currentIdx = skelBlob.Bones[boneIdx].ParentId;

            while (currentIdx >= 0 && currentIdx < skelBlob.Bones.Count)
            {
                string parentName = skelBlob.Bones[currentIdx].Name;
                if (string.IsNullOrEmpty(parentName)) break;

                if (gr2SkeletonBoneNames.Contains(parentName))
                {
                    // Reached a GR2 skeleton bone - use its animated world transform as the base
                    if (gr2BoneTransforms.TryGetValue(parentName, out var gr2World))
                    {
                        // Chain any accumulated track-only locals on top
                        var result = gr2World;
                        for (int i = chainLocals.Count - 1; i >= 0; i--)
                            result = chainLocals[i] * result;
                        return result;
                    }
                    break;
                }

                if (gr2BoneTransforms.TryGetValue(parentName, out var localTRS))
                {
                    // Track-only ancestor - accumulate its local TRS
                    chainLocals.Add(localTRS);
                }
                else if (_mbBindPoseWorldTransforms != null &&
                         _mbBindPoseWorldTransforms.TryGetValue(parentName, out var bindWorld))
                {
                    // No animation for this ancestor � use ModelBin bind-pose world
                    var result = bindWorld;
                    for (int i = chainLocals.Count - 1; i >= 0; i--)
                        result = chainLocals[i] * result;
                    return result;
                }
                else
                {
                    break;
                }

                currentIdx = skelBlob.Bones[currentIdx].ParentId;
            }

            // No animated parent found - chain any accumulated track-only locals onto the
            // MB bind-pose world of the nearest resolved ancestor (or Identity if none).
            {
                Matrix4x4 fallbackBase = Matrix4x4.Identity;
                short parentId = skelBlob.Bones[boneIdx].ParentId;
                if (parentId >= 0 && parentId < skelBlob.Bones.Count)
                {
                    string parentName = skelBlob.Bones[parentId].Name;
                    if (_mbBindPoseWorldTransforms != null)
                        _mbBindPoseWorldTransforms.TryGetValue(parentName, out fallbackBase);
                }
                if (chainLocals.Count > 0)
                {
                    var result = fallbackBase;
                    for (int i = chainLocals.Count - 1; i >= 0; i--)
                        result = chainLocals[i] * result;
                    return result;
                }
                return fallbackBase;
            }
        }

        // Returns the ModelBin bind-pose world transform for the named bone.
        // Used by RestorePreAnimationTransforms; not used in the main animation path.
        private Matrix4x4 GetMbBindPose(string boneName, MeshNode mesh)
        {
            if (_mbBindPoseWorldTransforms != null &&
                _mbBindPoseWorldTransforms.TryGetValue(boneName, out var mbBind))
                return mbBind;
            if (_originalMeshBoneTransforms != null &&
                _originalMeshBoneTransforms.TryGetValue(mesh, out var origT))
                return origT;
            return mesh.GeometryData?.BoneTransform ?? Matrix4x4.Identity;
        }

        // Returns the best available world transform for the parent of a track-only bone.
        // Priority: animated parent world from GR2 skeleton - MB bind-pose world - Identity.
        private Matrix4x4 GetParentAnimatedWorld(
            short boneIdx,
            SkeletonBlob skelBlob,
            Dictionary<string, Matrix4x4> gr2BoneTransforms)
        {
            if (skelBlob == null || boneIdx < 0 || boneIdx >= skelBlob.Bones.Count)
                return Matrix4x4.Identity;

            short parentId = skelBlob.Bones[boneIdx].ParentId;
            if (parentId < 0 || parentId >= skelBlob.Bones.Count)
                return Matrix4x4.Identity;

            string parentName = skelBlob.Bones[parentId].Name;
            if (string.IsNullOrEmpty(parentName))
                return Matrix4x4.Identity;

            // 1. Prefer live animated parent world from GR2 skeleton
            if (gr2BoneTransforms.TryGetValue(parentName, out var animatedParent))
                return animatedParent;

            // 2. Fall back to MB bind-pose world (parent un-animated or not in GR2)
            if (_mbBindPoseWorldTransforms != null &&
                _mbBindPoseWorldTransforms.TryGetValue(parentName, out var bindParent))
                return bindParent;

            return Matrix4x4.Identity;
        }

        // Saves the original bone transforms for all ModelBin meshes so they can be restored after stopping animation.
        private void SavePreAnimationTransforms()
        {
            _preAnimBoneTransforms = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
            _originalMeshBoneTransforms = new Dictionary<MeshNode, Matrix4x4>();

            foreach (var mb in ViewModel.GetAllModelBinNodes())
            {
                SkeletonBlob? skelBlob = null;
                if (mb.Bundle != null)
                    skelBlob = mb.Bundle.Blobs.OfType<SkeletonBlob>().FirstOrDefault();

                foreach (var mesh in mb.Children.OfType<MeshNode>())
                {
                    if (mesh.GeometryData == null) continue;

                    // Cache the per-mesh bone transform (from ModelImporter, includes ModelBin SkeletonBlob matrix)
                    _originalMeshBoneTransforms[mesh] = mesh.GeometryData.BoneTransform;

                    string? boneName = null;
                    short boneIdx = mesh.GeometryData.BoneIndex;
                    if (skelBlob != null && boneIdx >= 0 && boneIdx < skelBlob.Bones.Count)
                        boneName = skelBlob.Bones[boneIdx].Name;

                    if (string.IsNullOrEmpty(boneName))
                        boneName = mesh.GeometryData.BoneName;

                    if (!string.IsNullOrEmpty(boneName) && !_preAnimBoneTransforms.ContainsKey(boneName))
                    {
                        _preAnimBoneTransforms[boneName] = mesh.GeometryData.BoneTransform;
                    }
                }
            }
        }

        // Restores the original bone transforms saved before animation playback.
        private void RestorePreAnimationTransforms()
        {
            if (_originalMeshBoneTransforms == null || _originalMeshBoneTransforms.Count == 0)
            {
                // Fallback to old dictionary-based restore
                if (_preAnimBoneTransforms == null || _preAnimBoneTransforms.Count == 0) return;

                foreach (var mb in ViewModel.GetAllModelBinNodes())
                {
                    SkeletonBlob? skelBlob = null;
                    if (mb.Bundle != null)
                        skelBlob = mb.Bundle.Blobs.OfType<SkeletonBlob>().FirstOrDefault();

                    foreach (var mesh in mb.Children.OfType<MeshNode>())
                    {
                        if (mesh.GeometryData == null) continue;

                        string? boneName = null;
                        short boneIdx = mesh.GeometryData.BoneIndex;
                        if (skelBlob != null && boneIdx >= 0 && boneIdx < skelBlob.Bones.Count)
                            boneName = skelBlob.Bones[boneIdx].Name;

                        if (string.IsNullOrEmpty(boneName))
                            boneName = mesh.GeometryData.BoneName;

                        if (!string.IsNullOrEmpty(boneName) && _preAnimBoneTransforms.TryGetValue(boneName, out var origTransform))
                        {
                            mesh.GeometryData.BoneTransform = origTransform;
                            UpdateMeshRenderingWithBoneTransform(mesh, origTransform);
                        }
                    }
                }
                return;
            }

            // Restore per-mesh bone transforms
            foreach (var kvp in _originalMeshBoneTransforms)
            {
                var mesh = kvp.Key;
                var origTransform = kvp.Value;

                mesh.GeometryData.BoneTransform = origTransform;
                UpdateMeshRenderingWithBoneTransform(mesh, origTransform);
            }

            _originalMeshBoneTransforms = null;
        }

        // Counts how many meshes in a ModelBin have bones that match the current GR2 skeleton
        // and will be animated during playback.
        private int CountAnimatableMeshes(ModelBinNode modelBin)
        {
            if (_currentAnimSkeleton == null) return 0;

            var gr2BoneNames = new HashSet<string>(
                _currentAnimSkeleton.Bones.Where(b => !string.IsNullOrEmpty(b.Name)).Select(b => b.Name),
                StringComparer.OrdinalIgnoreCase);

            SkeletonBlob? skelBlob = null;
            if (modelBin.Bundle != null)
                skelBlob = modelBin.Bundle.Blobs.OfType<SkeletonBlob>().FirstOrDefault();

            int count = 0;
            foreach (var mesh in modelBin.Children.OfType<MeshNode>())
            {
                if (mesh.GeometryData == null) continue;

                string? boneName = null;
                short boneIdx = mesh.GeometryData.BoneIndex;
                if (skelBlob != null && boneIdx >= 0 && boneIdx < skelBlob.Bones.Count)
                    boneName = skelBlob.Bones[boneIdx].Name;

                if (string.IsNullOrEmpty(boneName))
                    boneName = mesh.GeometryData.BoneName;

                if (!string.IsNullOrEmpty(boneName) && gr2BoneNames.Contains(boneName))
                    count++;
            }

            return count;
        }

        // Updates the Bone Matching Details expander with per-bone matching info
        // between ModelBin skeleton, GR2 skeleton, animation tracks, and mesh linkage.
        private void UpdateBoneMatchDetails()
        {
            if (BoneMatchExpander == null) return;

            var modelBins = ViewModel.GetAllModelBinNodes();
            var firstModelBin = modelBins.FirstOrDefault();

            if (firstModelBin == null && _currentAnimation == null)
            {
                BoneMatchExpander.Visibility = Visibility.Collapsed;
                return;
            }

            BoneMatchExpander.Visibility = Visibility.Visible;

            var entries = ViewModel.ComputeBoneMatchEntriesWithAnim(
                firstModelBin!, _currentAnimSkeleton!, _currentAnimation!);

            // Populate the list
            BoneMatchListView.ItemsSource = entries;

            // Summary text
            int totalBones = entries.Count;
            int fullMatch = entries.Count(e => e.InModelBinSkeleton && e.InGr2Skeleton);
            int mbOnly = entries.Count(e => e.InModelBinSkeleton && !e.InGr2Skeleton);
            int gr2Only = entries.Count(e => !e.InModelBinSkeleton && e.InGr2Skeleton);
            int withTracks = entries.Count(e => e.InAnimTrack);
            int withMeshes = entries.Count(e => e.LinkedMeshCount > 0);
            int animatable = entries.Count(e => e.InAnimTrack && e.InModelBinSkeleton && e.LinkedMeshCount > 0);

            if (BoneMatchSummaryText != null)
            {
                BoneMatchSummaryText.Text =
                    $"{totalBones} unique bones | {fullMatch} matched (MB+GR2) | " +
                    $"{mbOnly} ModelBin-only | {gr2Only} GR2-only\n" +
                    $"{withTracks} with anim tracks | {withMeshes} linked to meshes | " +
                    $"{animatable} will animate meshes";
            }
        }

        private void UpdateAnimTimeDisplay()
        {
            if (AnimTimeText != null)
            {
                float speed = (float)(AnimSpeedSlider?.Value ?? 1.0);
                string speedStr = speed != 1.0f ? $" @{speed:F1}x" : "";

                string extra = "";
                if (_currentAnimation != null)
                {
                    var parts = new List<string>();
                    if (_currentAnimation.TimeStep > 0)
                        parts.Add($"Step:{_currentAnimation.TimeStep:F4}");
                    if (_currentAnimation.Oversampling > 0)
                        parts.Add($"OS:{_currentAnimation.Oversampling:F0}");
                    if (parts.Count > 0)
                        extra = $" ({string.Join(" ", parts)})";
                }

                AnimTimeText.Text = $"{_animCurrentTime:F2}s / {_animDuration:F2}s{speedStr}{extra}";
            }
        }

        private int CountModelBinBoneMatches(ModelBinNode modelBin, GrannySkeleton? skeleton)
        {
            if (skeleton == null || skeleton.Bones.Count == 0) return 0;

            var modelBinBoneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mesh in modelBin.Children.OfType<MeshNode>())
            {
                string? boneName = mesh.GeometryData?.BoneName;
                if (string.IsNullOrEmpty(boneName) && mesh.GeometryData?.SourceBone != null)
                {
                    boneName = mesh.GeometryData.SourceBone.Name;
                }
                if (!string.IsNullOrEmpty(boneName))
                {
                    modelBinBoneNames.Add(boneName);
                }
            }

            if (modelBin.Bundle != null)
            {
                var skelBlob = modelBin.Bundle.Blobs.OfType<SkeletonBlob>().FirstOrDefault();
                if (skelBlob != null)
                {
                    foreach (var bone in skelBlob.Bones)
                    {
                        if (!string.IsNullOrEmpty(bone.Name))
                            modelBinBoneNames.Add(bone.Name);
                    }
                }
            }

            // Match against GR2 skeleton bones
            int matched = 0;
            foreach (var bone in skeleton.Bones)
            {
                if (!string.IsNullOrEmpty(bone.Name) && modelBinBoneNames.Contains(bone.Name))
                    matched++;
            }

            // Also check animation track names as an additional source for matching
            if (_currentAnimation != null)
            {
                foreach (var tg in _currentAnimation.TrackGroups)
                {
                    foreach (var tt in tg.TransformTracks)
                    {
                        if (!string.IsNullOrEmpty(tt.Name) && modelBinBoneNames.Contains(tt.Name))
                        {
                            // Only count if not already counted from skeleton
                            if (!skeleton.Bones.Any(b => string.Equals(b.Name, tt.Name, StringComparison.OrdinalIgnoreCase)))
                                matched++;
                        }
                    }
                }
            }

            return matched;
        }

        private void UpdateAnimBoneInfo()
        {
            if (AnimBoneInfoText == null) return;

            if (_currentAnimation == null)
            {
                UpdateSkeletonStatusText();
                return;
            }

            int totalTracks = _currentAnimation.TrackGroups.Sum(tg => tg.TransformTracks.Count);
            int tracksWithKeyframes = _currentAnimation.TrackGroups
                .SelectMany(tg => tg.TransformTracks)
                .Count(tt => tt.HasAnimationData);
            int matchedBones = 0;
            string skelSource = "";

            if (_currentAnimSkeleton != null)
            {
                var gr2Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var bone in _currentAnimSkeleton.Bones)
                {
                    if (!string.IsNullOrEmpty(bone.Name))
                        gr2Names.Add(bone.Name);
                }
                foreach (var tg in _currentAnimation.TrackGroups)
                {
                    foreach (var tt in tg.TransformTracks)
                    {
                        if (!string.IsNullOrEmpty(tt.Name))
                            gr2Names.Add(tt.Name);
                    }
                }

                foreach (var tg in _currentAnimation.TrackGroups)
                {
                    matchedBones += tg.TransformTracks.Count(tt =>
                        !string.IsNullOrEmpty(tt.Name) && gr2Names.Contains(tt.Name));
                }

                if (_currentAnimSkeletonNode?.Parent is GrannyFileNode gfn)
                {
                    skelSource = $" (from {System.IO.Path.GetFileName(gfn.FilePath)})";
                }
            }

            var parts = new List<string>();
            parts.Add($"Tracks: {totalTracks} ({tracksWithKeyframes} with keyframes)");
            parts.Add($"Matched: {matchedBones}/{_currentAnimSkeleton?.Bones.Count ?? 0}{skelSource}");

            // Also show ModelBin bone match info
            var modelBins = ViewModel.GetAllModelBinNodes();
            foreach (var mb in modelBins)
            {
                int matchCount = CountModelBinBoneMatches(mb, _currentAnimSkeleton);
                if (matchCount > 0)
                {
                    // Count meshes that will be animated
                    int animatedMeshes = CountAnimatableMeshes(mb);
                    parts.Add($"ModelBin '{mb.Name}': {matchCount} bones, {animatedMeshes} meshes linked");
                    break; // Show first match only to keep it compact
                }
            }

            AnimBoneInfoText.Text = string.Join(" | ", parts);
        }

        // Updates animation expander visibility based on loaded GrannyFile nodes.
        // Called from RefreshModelList.
        private void UpdateAnimationExpanderVisibility()
        {
            bool hasGranny = false;
            foreach (var root in ViewModel.Roots)
            {
                if (HasGrannyNodes(root))
                {
                    hasGranny = true;
                    break;
                }
            }

            if (AnimationExpander != null)
            {
                if (hasGranny)
                {
                    AnimationExpander.Visibility = Visibility.Visible;
                    PopulateAnimationSelector();
                }
                else
                {
                    AnimationExpander.Visibility = Visibility.Collapsed;
                    AnimationSelector.ItemsSource = null;
                }
            }
        }

        private bool HasGrannyNodes(IViewerNode node)
        {
            if (node is GrannyFileNode) return true;
            foreach (var child in node.Children)
                if (HasGrannyNodes(child)) return true;
            return false;
        }

        private void PopulateAnimationSelector()
        {
            var animNodes = new List<AnimationClipNode>();
            foreach (var root in ViewModel.Roots)
                CollectAnimNodes(root, animNodes);

            // Enhance display names with duration and track info
            foreach (var node in animNodes)
            {
                if (node.AnimationData != null)
                {
                    int trackCount = node.AnimationData.TrackGroups.Sum(tg => tg.TransformTracks.Count);
                    node.Name = $"{node.AnimationData.Name ?? "Unnamed"} ({node.AnimationData.Duration:F2}s, {trackCount} tracks)";
                }
            }

            AnimationSelector.ItemsSource = animNodes;
            if (animNodes.Count > 0)
                AnimationSelector.SelectedIndex = 0;

            UpdateSkeletonStatusText();
        }

        private void CollectAnimNodes(IViewerNode node, List<AnimationClipNode> list)
        {
            if (node is AnimationClipNode animNode)
                list.Add(animNode);
            foreach (var child in node.Children)
                CollectAnimNodes(child, list);
        }

        private void UpdateSkeletonStatusText()
        {
            if (AnimBoneInfoText == null) return;

            var allSkeletons = ViewModel.GetAllSkeletonNodes();
            var allGrannyFiles = ViewModel.GetAllGrannyFileNodes();
            int totalAnims = 0;
            foreach (var gfn in allGrannyFiles)
            {
                if (gfn.FileData != null)
                    totalAnims += gfn.FileData.Animations.Count;
            }

            var parts = new List<string>();
            parts.Add($"{allSkeletons.Count} skeleton(s)");
            parts.Add($"{totalAnims} animation(s)");

            // Check for bone matching with loaded Modelbins
            var modelBins = ViewModel.GetAllModelBinNodes();
            if (modelBins.Count > 0 && allSkeletons.Count > 0)
            {
                int matched = 0;
                foreach (var mb in modelBins)
                {
                    var match = ViewModel.FindBestSkeletonMatch(mb);
                    if (match.HasValue && match.Value.MatchCount > 0)
                        matched++;
                }
                if (matched > 0)
                    parts.Add($"{matched}/{modelBins.Count} modelbin(s) matched");
            }

            int gsfCount = allGrannyFiles.Count(g => g.IsGsf);
            if (gsfCount > 0)
                parts.Add($"{gsfCount} GSF(s)");

            AnimBoneInfoText.Text = string.Join(" | ", parts);

            UpdateLoadAssociatedFilesVisibility();
        }

        private void UpdateLoadAssociatedFilesVisibility()
        {
            var btn = this.FindName("LoadAssociatedFilesButton") as Button;
            if (btn == null) return;

            // Show button if any GSF has unloaded source file references
            bool hasUnloaded = false;
            var allGrannyFiles = ViewModel.GetAllGrannyFileNodes();
            var loadedPaths = new HashSet<string>(
                allGrannyFiles.Select(g => System.IO.Path.GetFileName(g.FilePath ?? "").ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);

            foreach (var gfn in allGrannyFiles)
            {
                if (gfn.FileData?.CharacterInfo == null) continue;
                foreach (var set in gfn.FileData.CharacterInfo.AnimationSets)
                {
                    foreach (var sfr in set.SourceFileReferences)
                    {
                        if (string.IsNullOrEmpty(sfr.SourceFilename)) continue;
                        string refName = System.IO.Path.GetFileName(sfr.SourceFilename).ToLowerInvariant();
                        if (!loadedPaths.Contains(refName))
                        {
                            // Check if file exists on disk
                            string? gsfDir = System.IO.Path.GetDirectoryName(gfn.FilePath);
                            if (gsfDir == null) continue;
                            string refPath = System.IO.Path.Combine(gsfDir, refName);
                            if (System.IO.File.Exists(refPath))
                            {
                                hasUnloaded = true;
                                break;
                            }
                        }
                    }
                    if (hasUnloaded) break;
                }
                if (hasUnloaded) break;
            }

            btn.Visibility = hasUnloaded ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void LoadAssociatedFiles_Click(object sender, RoutedEventArgs e)
        {
            var allGrannyFiles = ViewModel.GetAllGrannyFileNodes();
            var loadedPaths = new HashSet<string>(
                allGrannyFiles.Select(g => System.IO.Path.GetFullPath(g.FilePath ?? "")),
                StringComparer.OrdinalIgnoreCase);

            var toLoad = new List<string>();

            foreach (var gfn in allGrannyFiles)
            {
                if (gfn.FileData?.CharacterInfo == null) continue;
                string? gsfDir = System.IO.Path.GetDirectoryName(gfn.FilePath);

                foreach (var set in gfn.FileData.CharacterInfo.AnimationSets)
                {
                    foreach (var sfr in set.SourceFileReferences)
                    {
                        if (string.IsNullOrEmpty(sfr.SourceFilename)) continue;
                        string refName = System.IO.Path.GetFileName(sfr.SourceFilename);
                        if (gsfDir == null) continue;
                        string refPath = System.IO.Path.Combine(gsfDir, refName);
                        string fullPath = System.IO.Path.GetFullPath(refPath);

                        if (System.IO.File.Exists(refPath) && !loadedPaths.Contains(fullPath))
                        {
                            toLoad.Add(refPath);
                            loadedPaths.Add(fullPath);
                        }
                    }
                }

                // Also scan for any .gr2 files in the same directory
                try
                {
                    var scanDir = System.IO.Path.GetDirectoryName(gfn.FilePath);
                    if (scanDir != null)
                    foreach (var gr2 in System.IO.Directory.GetFiles(scanDir, "*.gr2"))
                    {
                        string fullGr2 = System.IO.Path.GetFullPath(gr2);
                        if (!loadedPaths.Contains(fullGr2))
                        {
                            toLoad.Add(gr2);
                            loadedPaths.Add(fullGr2);
                        }
                    }
                }
                catch { }
            }

            if (toLoad.Count > 0)
            {
                await ProcessDroppedFilesAsync(toLoad);
            }
        }

        // Bone Matching Details

        // Rebuilds the cached bone - mesh linkage map across all loaded ModelBins.
        private void RebuildBoneMeshLinkMap()
        {
            _boneMeshLinkMap = new Dictionary<string, List<MeshNode>>(StringComparer.OrdinalIgnoreCase);

            foreach (var mb in ViewModel.GetAllModelBinNodes())
            {
                SkeletonBlob? skelBlob = null;
                if (mb.Bundle != null)
                    skelBlob = mb.Bundle.Blobs.OfType<SkeletonBlob>().FirstOrDefault();

                foreach (var child in mb.Children)
                {
                    if (child is MeshNode mesh && mesh.GeometryData != null)
                    {
                        string? boneName = null;

                        short boneIdx = mesh.GeometryData.BoneIndex;
                        if (skelBlob != null && boneIdx >= 0 && boneIdx < skelBlob.Bones.Count)
                            boneName = skelBlob.Bones[boneIdx].Name;

                        if (string.IsNullOrEmpty(boneName))
                            boneName = mesh.GeometryData.BoneName;

                        if (string.IsNullOrEmpty(boneName) && mesh.GeometryData.SourceBone != null)
                            boneName = mesh.GeometryData.SourceBone.Name;

                        if (!string.IsNullOrEmpty(boneName))
                        {
                            if (!_boneMeshLinkMap.ContainsKey(boneName))
                                _boneMeshLinkMap[boneName] = new List<MeshNode>();
                            _boneMeshLinkMap[boneName].Add(mesh);
                        }
                    }
                }
            }
        }



    }
}
