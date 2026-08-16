using GLMFighter.Core;
using GLMFighter.Runtime;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace GLMFighter.EditorTools
{
    /// <summary>
    /// Editor for a MotionTimelineAsset. A row is one independent sampled data
    /// track; the row range is the track's active range.
    /// </summary>
    public sealed class MotionDataEditorWindow : EditorWindow
    {
        private MotionTimelineAsset timeline;
        private AnimationClip previewAnimation;
        private FighterRoleDefinition fighterRole;
        private GameObject previewRoot;
        private GameObject previewInstance;

        private int currentFrame;
        private int selectedTrack = -1;
        private bool isPlaying;
        private bool loopPreview = true;
        private bool showInactiveHitBoxes = true;
        private float playbackSpeed = 1f;
        private double lastPlaybackTime;
        private Vector2 scroll;
        private Rect timelineFrameArea;
        private float timelineZoom = 1f;
        private float timelineScroll;

        [MenuItem("GLM Fighter/Motion Timeline Editor")]
        public static void Open()
        {
            GetWindow<MotionDataEditorWindow>("Motion Timeline");
        }

        [OnOpenAsset(1)]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            MotionTimelineAsset asset = EditorUtility.InstanceIDToObject(instanceId) as MotionTimelineAsset;
            if (asset == null)
            {
                return false;
            }

            MotionDataEditorWindow window = GetWindow<MotionDataEditorWindow>("Motion Timeline");
            window.SetTimeline(asset);
            window.Focus();
            return true;
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            SceneView.duringSceneGui += OnSceneGui;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            SceneView.duringSceneGui -= OnSceneGui;
            DestroyPreview();

            if (AnimationMode.InAnimationMode())
            {
                AnimationMode.StopAnimationMode();
            }
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawAssetSection();

            if (timeline == null)
            {
                EditorGUILayout.HelpBox("Create or assign a MotionTimeline asset to start authoring.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            currentFrame = Mathf.Clamp(currentFrame, 0, timeline.TotalFrames - 1);
            selectedTrack = Mathf.Clamp(selectedTrack, -1, timeline.Tracks.Length - 1);

            EditorGUILayout.Space(6f);
            DrawPlaybackSection();
            EditorGUILayout.Space(6f);
            DrawTimeline();
            HandleTimelineShortcuts();
            EditorGUILayout.Space(8f);

            DrawSelectedTrackInspector();

            EditorGUILayout.EndScrollView();
        }

        private void DrawAssetSection()
        {
            EditorGUILayout.LabelField("Motion Timeline", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                MotionTimelineAsset nextTimeline = (MotionTimelineAsset)EditorGUILayout.ObjectField(
                    "Timeline Asset", timeline, typeof(MotionTimelineAsset), false);
                if (nextTimeline != timeline)
                {
                    SetTimeline(nextTimeline);
                }

                if (GUILayout.Button("Create", GUILayout.Width(72f)))
                {
                    CreateTimelineAsset();
                }
            }

            if (timeline == null)
            {
                return;
            }

            EditorGUI.BeginChangeCheck();
            string timelineId = EditorGUILayout.TextField("Timeline Id", timeline.TimelineId);
            int frameRate = EditorGUILayout.IntField("Frame Rate", timeline.FrameRate);
            int totalFrames = EditorGUILayout.IntField("Total Frames", timeline.TotalFrames);
            bool loop = EditorGUILayout.Toggle("Loop", timeline.Loop);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(timeline, "Edit Timeline Settings");
                timeline.SetTimelineId(timelineId);
                timeline.SetFrameRate(frameRate);
                timeline.SetTotalFrames(totalFrames);
                timeline.SetLoop(loop);
                currentFrame = Mathf.Clamp(currentFrame, 0, timeline.TotalFrames - 1);
                ClampTrackRanges();
                MarkDirty();
            }

            AnimationClip nextAnimation = (AnimationClip)EditorGUILayout.ObjectField(
                "SceneView Animation", previewAnimation, typeof(AnimationClip), false);
            if (nextAnimation != previewAnimation)
            {
                previewAnimation = nextAnimation;
                timeline.SetSceneViewAnimation(nextAnimation);
                MarkDirty();
                SamplePreview();
            }

            if (previewAnimation != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Animation Frames", GetPreviewClipFrameCount().ToString(), EditorStyles.miniLabel);
                    if (GUILayout.Button("Match Animation Length", GUILayout.Width(150f)))
                    {
                        MatchAnimationLength();
                    }
                }
            }

            FighterRoleDefinition nextRole = (FighterRoleDefinition)EditorGUILayout.ObjectField(
                "Fighter Role", fighterRole, typeof(FighterRoleDefinition), false);
            if (nextRole != fighterRole)
            {
                fighterRole = nextRole;
                timeline.SetFighterRole(nextRole);
                MarkDirty();
                SpawnPreview();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Respawn Preview"))
                {
                    SpawnPreview();
                }

                if (GUILayout.Button("Clear Preview"))
                {
                    DestroyPreview();
                }

                if (GUILayout.Button("Save Timeline", GUILayout.Width(112f)))
                {
                    EditorUtility.SetDirty(timeline);
                    AssetDatabase.SaveAssets();
                }
            }
        }

        private void DrawPlaybackSection()
        {
            EditorGUILayout.LabelField("Playback", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(isPlaying ? "Pause" : "Play", GUILayout.Width(68f)))
                {
                    isPlaying = !isPlaying;
                    lastPlaybackTime = EditorApplication.timeSinceStartup;
                }

                if (GUILayout.Button("Prev", GUILayout.Width(52f)))
                {
                    SetCurrentFrame(currentFrame - 1);
                }

                if (GUILayout.Button("Next", GUILayout.Width(52f)))
                {
                    SetCurrentFrame(currentFrame + 1);
                }

                int nextFrame = EditorGUILayout.IntSlider("Frame", currentFrame, 0, timeline.TotalFrames - 1);
                if (nextFrame != currentFrame)
                {
                    SetCurrentFrame(nextFrame);
                }

                loopPreview = EditorGUILayout.Toggle("Loop", loopPreview, GUILayout.Width(92f));
                playbackSpeed = Mathf.Clamp(EditorGUILayout.FloatField("Speed", playbackSpeed), 0.1f, 4f);
            }

            showInactiveHitBoxes = EditorGUILayout.Toggle("Show Inactive HitBoxes", showInactiveHitBoxes);
        }

        private void DrawTimeline()
        {
            MotionTimelineTrackDefinition[] tracks = timeline.Tracks;
            const float labelWidth = 170f;
            const float headerHeight = 24f;
            const float trackHeight = 30f;
            Rect rect = GUILayoutUtility.GetRect(
                10f,
                headerHeight + Mathf.Max(1, tracks.Length) * trackHeight + 4f,
                GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.11f, 0.11f, 0.11f, 1f));

            // The frame area covers the header and every track row so clicking
            // any grid line can scrub the playhead.
            timelineFrameArea = new Rect(
                rect.x + labelWidth,
                rect.y,
                Mathf.Max(1f, rect.width - labelWidth),
                rect.height);
            int frameCount = timeline.TotalFrames;
            float baseFrameWidth = Mathf.Max(4f, timelineFrameArea.width / frameCount);
            float frameWidth = baseFrameWidth * timelineZoom;
            float contentWidth = Mathf.Max(timelineFrameArea.width, frameCount * frameWidth);
            float maxScroll = Mathf.Max(0f, contentWidth - timelineFrameArea.width);
            timelineScroll = Mathf.Clamp(timelineScroll, 0f, maxScroll);

            EditorGUI.LabelField(
                new Rect(rect.x + 5f, rect.y + 4f, labelWidth - 8f, 18f),
                "Timeline Tracks",
                EditorStyles.miniLabel);

            for (int index = 0; index < tracks.Length; index++)
            {
                MotionTimelineTrackDefinition track = tracks[index];
                if (track == null)
                {
                    continue;
                }

                Rect row = new Rect(
                    rect.x,
                    rect.y + headerHeight + index * trackHeight + 2f,
                    rect.width,
                    trackHeight - 3f);
                bool selected = index == selectedTrack;
                EditorGUI.DrawRect(row, selected
                    ? new Color(0.2f, 0.28f, 0.34f, 1f)
                    : new Color(0.08f, 0.08f, 0.08f, 1f));

                string label = GetTrackTypeName(track.Type) + " / " + track.DisplayName;
                EditorGUI.LabelField(new Rect(row.x + 5f, row.y + 5f, labelWidth - 8f, 18f), label, EditorStyles.miniLabel);
            }

            GUI.BeginClip(timelineFrameArea);
            for (int frame = 0; frame < frameCount; frame++)
            {
                float frameX = frame * frameWidth - timelineScroll;
                if (frameX + frameWidth < 0f || frameX > timelineFrameArea.width)
                {
                    continue;
                }

                EditorGUI.DrawRect(
                    new Rect(frameX, 0f, Mathf.Max(1f, frameWidth - 1f), timelineFrameArea.height),
                    frame % 5 == 0
                        ? new Color(0.24f, 0.24f, 0.24f, 1f)
                        : new Color(0.18f, 0.18f, 0.18f, 1f));
            }

            for (int index = 0; index < tracks.Length; index++)
            {
                MotionTimelineTrackDefinition track = tracks[index];
                if (track == null)
                {
                    continue;
                }

                bool selected = index == selectedTrack;
                Rect row = new Rect(
                    0f,
                    headerHeight + index * trackHeight + 2f - timelineFrameArea.y + rect.y,
                    timelineFrameArea.width,
                    trackHeight - 3f);
                int start = Mathf.Clamp(track.StartFrame, 0, frameCount - 1);
                int end = Mathf.Clamp(track.EndFrame, start, frameCount - 1);
                Rect activeRect = new Rect(
                    start * frameWidth - timelineScroll,
                    row.y + 5f,
                    Mathf.Max(1f, (end - start + 1) * frameWidth - 1f),
                    row.height - 10f);
                MotionTimelineBodyTrackDefinition bodyTrack = track as MotionTimelineBodyTrackDefinition;
                if (bodyTrack != null)
                {
                    // Body Track range is only a subtle background. The actual
                    // sampled states are represented by the key markers below.
                    EditorGUI.DrawRect(
                        activeRect,
                        selected
                            ? new Color(0.22f, 0.2f, 0.28f, 1f)
                            : new Color(0.14f, 0.13f, 0.18f, 1f));

                    MotionTimelineBodyKey[] keys = bodyTrack.Keys;
                    for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                    {
                        float keyX = keys[keyIndex].Frame * frameWidth - timelineScroll;
                        EditorGUI.DrawRect(
                            new Rect(
                                keyX,
                                row.y + 4f,
                                Mathf.Max(3f, frameWidth * 0.75f),
                                row.height - 8f),
                            keys[keyIndex].Frame == currentFrame && selected
                                ? Color.white
                                : selected
                                ? new Color(1f, 0.72f, 0.2f, 1f)
                                : new Color(0.72f, 0.52f, 1f, 1f));
                    }
                }
                else if (track.Type == MotionTimelineTrackType.HitBox)
                {
                    MotionTimelineHitBoxTrackDefinition hitBoxTrack =
                        (MotionTimelineHitBoxTrackDefinition)track;
                    EditorGUI.DrawRect(
                        activeRect,
                        selected
                            ? new Color(0.3f, 0.18f, 0.2f, 1f)
                            : new Color(0.2f, 0.12f, 0.14f, 1f));

                    MotionTimelineHitBoxKey[] keys = hitBoxTrack.Keys;
                    for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                    {
                        float keyX = keys[keyIndex].Frame * frameWidth - timelineScroll;
                        EditorGUI.DrawRect(
                            new Rect(
                                keyX,
                                row.y + 4f,
                                Mathf.Max(3f, frameWidth * 0.75f),
                                row.height - 8f),
                            keys[keyIndex].Frame == currentFrame && selected
                                ? Color.white
                                : selected
                                ? new Color(1f, 0.48f, 0.2f, 1f)
                                : new Color(0.8f, 0.28f, 0.24f, 1f));
                    }
                }
                else if (track.Type == MotionTimelineTrackType.State)
                {
                    MotionTimelineStateTrackDefinition stateTrack =
                        (MotionTimelineStateTrackDefinition)track;
                    MotionTimelineStateKey state = stateTrack.Evaluate(currentFrame);
                    EditorGUI.DrawRect(
                        activeRect,
                        state.Value
                            ? new Color(0.18f, 0.42f, 0.22f, selected ? 1f : 0.85f)
                            : new Color(0.38f, 0.38f, 0.38f, selected ? 1f : 0.75f));

                    MotionTimelineStateKey[] keys = stateTrack.Keys;
                    for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                    {
                        float keyX = keys[keyIndex].Frame * frameWidth - timelineScroll;
                        EditorGUI.DrawRect(
                            new Rect(
                                keyX,
                                row.y + 4f,
                                Mathf.Max(3f, frameWidth * 0.75f),
                                row.height - 8f),
                            keys[keyIndex].Frame == currentFrame && selected
                                ? Color.white
                                : new Color(0.62f, 0.95f, 0.62f, selected ? 1f : 0.95f));
                    }
                }
                else
                {
                    EditorGUI.DrawRect(activeRect, GetTrackColor(track, selected));
                }
            }

            EditorGUI.DrawRect(
                new Rect(
                    currentFrame * frameWidth - timelineScroll,
                    0f,
                    2f,
                    timelineFrameArea.height),
                new Color(1f, 1f, 1f, 0.75f));
            GUI.EndClip();

            if (contentWidth <= timelineFrameArea.width + 0.5f)
            {
                timelineScroll = 0f;
            }

            // Keep the scrollbar in the layout even when the content currently
            // fits. This prevents the inspector from jumping when zoom changes.
            float scrollbarRange = Mathf.Max(contentWidth, timelineFrameArea.width + 1f);
            float nextScroll = GUILayout.HorizontalScrollbar(
                timelineScroll,
                timelineFrameArea.width,
                0f,
                scrollbarRange,
                GUILayout.ExpandWidth(true));
            if (contentWidth > timelineFrameArea.width + 0.5f &&
                !Mathf.Approximately(nextScroll, timelineScroll))
            {
                timelineScroll = nextScroll;
                Repaint();
            }

            Event evt = Event.current;
            if (evt.type == EventType.ScrollWheel &&
                timelineFrameArea.Contains(evt.mousePosition) &&
                (evt.control || evt.command))
            {
                float oldFrameWidth = frameWidth;
                float frameAtMouse =
                    (evt.mousePosition.x - timelineFrameArea.x + timelineScroll) / oldFrameWidth;
                float zoomFactor = evt.delta.y > 0f ? 0.9f : 1.1f;
                timelineZoom = Mathf.Clamp(timelineZoom * zoomFactor, 1f, 8f);

                float newFrameWidth = baseFrameWidth * timelineZoom;
                timelineScroll = frameAtMouse * newFrameWidth -
                                 (evt.mousePosition.x - timelineFrameArea.x);
                float newContentWidth = Mathf.Max(timelineFrameArea.width, frameCount * newFrameWidth);
                timelineScroll = Mathf.Clamp(
                    timelineScroll,
                    0f,
                    Mathf.Max(0f, newContentWidth - timelineFrameArea.width));
                evt.Use();
                Repaint();
                return;
            }

            bool isTimelineScrub = (evt.type == EventType.MouseDown || evt.type == EventType.MouseDrag) &&
                                   evt.button == 0 &&
                                   timelineFrameArea.Contains(evt.mousePosition);
            if (isTimelineScrub)
            {
                int rowIndex = Mathf.FloorToInt((evt.mousePosition.y - rect.y - headerHeight) / trackHeight);
                if (rowIndex >= 0 && rowIndex < tracks.Length)
                {
                    SelectTrack(rowIndex);
                }

                int frame = Mathf.Clamp(
                    Mathf.RoundToInt(
                        (evt.mousePosition.x - timelineFrameArea.x + timelineScroll) / frameWidth),
                    0,
                    frameCount - 1);
                SetCurrentFrame(frame);
                evt.Use();
            }
            else if (evt.type == EventType.MouseDown && evt.button == 1 && rect.Contains(evt.mousePosition))
            {
                int rowIndex = Mathf.FloorToInt((evt.mousePosition.y - rect.y - headerHeight) / trackHeight);
                int frame = timelineFrameArea.Contains(evt.mousePosition)
                    ? Mathf.Clamp(
                        Mathf.RoundToInt(
                            (evt.mousePosition.x - timelineFrameArea.x + timelineScroll) / frameWidth),
                        0,
                        frameCount - 1)
                    : currentFrame;

                GenericMenu menu = new GenericMenu();
                if (rowIndex >= 0 && rowIndex < tracks.Length && tracks[rowIndex] != null)
                {
                    MotionTimelineTrackDefinition clickedTrack = tracks[rowIndex];
                    menu.AddItem(
                        new GUIContent("Select " + GetTrackTypeName(clickedTrack.Type) + " / " + clickedTrack.DisplayName),
                        rowIndex == selectedTrack,
                        () => SelectTrack(rowIndex));
                    menu.AddItem(
                        new GUIContent("Delete Track"),
                        false,
                        () => DeleteTrack(rowIndex));
                    menu.AddSeparator(string.Empty);
                }

                menu.AddItem(
                    new GUIContent("Add Body Track"),
                    false,
                    () => AddTrack(MotionTimelineTrackType.Body, frame));
                menu.AddItem(
                    new GUIContent("Add HitBox Track"),
                    false,
                    () => AddTrack(MotionTimelineTrackType.HitBox, frame));
                menu.AddItem(
                    new GUIContent("Add State Track"),
                    false,
                    () => AddTrack(MotionTimelineTrackType.State, frame));
                menu.ShowAsContext();
                evt.Use();
            }
        }

        private static string GetTrackTypeName(MotionTimelineTrackType type)
        {
            switch (type)
            {
                case MotionTimelineTrackType.Body:
                    return "Body Track";
                case MotionTimelineTrackType.HitBox:
                    return "HitBox Track";
                default:
                    return "State Track";
            }
        }

        private void DrawSelectedTrackInspector()
        {
            EditorGUILayout.LabelField("Common", EditorStyles.boldLabel);
            if (selectedTrack < 0 || selectedTrack >= timeline.Tracks.Length || timeline.Tracks[selectedTrack] == null)
            {
                EditorGUILayout.HelpBox(
                    timeline.Tracks.Length == 0
                        ? "Right-click the timeline to add a Body Track, HitBox Track, or State Track."
                        : "Select a track directly from the timeline.",
                    MessageType.Info);
                return;
            }

            MotionTimelineTrackDefinition track = timeline.Tracks[selectedTrack];
            string displayName = EditorGUILayout.TextField("Name", track.DisplayName);
            string trackId = EditorGUILayout.TextField("Track Id", track.TrackId);
            int maxFrame = timeline.TotalFrames - 1;
            int currentStart = Mathf.Clamp(track.StartFrame, 0, maxFrame);
            int currentEnd = Mathf.Clamp(track.EndFrame, currentStart, maxFrame);
            int start = EditorGUILayout.IntSlider("Start Frame", currentStart, 0, maxFrame);
            int end = EditorGUILayout.IntSlider("End Frame", currentEnd, 0, maxFrame);

            bool startChanged = start != currentStart;
            bool endChanged = end != currentEnd;
            if (startChanged && !endChanged)
            {
                // Keep End stable when the user drags Start past it.
                start = Mathf.Min(start, currentEnd);
            }
            else if (endChanged && !startChanged)
            {
                // Keep Start stable when the user drags End below it.
                end = Mathf.Max(end, currentStart);
            }
            else if (start > end)
            {
                end = start;
            }

            if (displayName != track.DisplayName || trackId != track.TrackId ||
                start != track.StartFrame || end != track.EndFrame)
            {
                Undo.RecordObject(timeline, "Edit Timeline Track");
                track.DisplayName = displayName;
                track.TrackId = trackId;
                track.SetRange(start, end, timeline.TotalFrames);
                MarkDirty();
            }

            EditorGUILayout.LabelField("Type", track.Type.ToString());
            EditorGUILayout.Space(6f);
            if (track.Type == MotionTimelineTrackType.Body)
            {
                DrawBodyTrackInspector((MotionTimelineBodyTrackDefinition)track);
            }
            else if (track.Type == MotionTimelineTrackType.HitBox)
            {
                DrawHitBoxTrackInspector((MotionTimelineHitBoxTrackDefinition)track);
            }
            else
            {
                DrawStateTrackInspector((MotionTimelineStateTrackDefinition)track);
            }

            EditorGUILayout.Space(8f);
        }

        private void DrawBodyTrackInspector(MotionTimelineBodyTrackDefinition track)
        {
            EditorGUILayout.LabelField("Custom Data", EditorStyles.boldLabel);
            MotionTimelineInterpolationMode interpolation =
                (MotionTimelineInterpolationMode)EditorGUILayout.EnumPopup("Interpolation", track.Interpolation);
            if (interpolation != track.Interpolation)
            {
                Undo.RecordObject(timeline, "Edit Body Track Interpolation");
                track.Interpolation = interpolation;
                MarkDirty();
                SceneView.RepaintAll();
            }

            if (!track.ContainsFrame(currentFrame))
            {
                EditorGUILayout.HelpBox(
                    "Current Frame is outside this Track's active range. The values below are the inherited Body state.",
                    MessageType.Info);
            }

            int keyIndex = track.FindKeyIndex(currentFrame);
            MotionTimelineBodyKey state = track.Evaluate(currentFrame);
            EditorGUILayout.LabelField(
                "State at Frame " + currentFrame + (keyIndex >= 0 ? " (Key Frame)" : " (Inherited)"),
                EditorStyles.miniLabel);
            if (keyIndex < 0)
            {
                EditorGUILayout.HelpBox(
                    "This frame inherits the last Body state. Editing a field will create a Key Frame here.",
                    MessageType.None);
            }

            Vector2 centerOffset = EditorGUILayout.Vector2Field("Body Center Offset", state.BodyCenterOffset);
            Vector2 sizeOffset = EditorGUILayout.Vector2Field("Body Size Offset", state.BodySizeOffset);
            if (centerOffset != state.BodyCenterOffset || sizeOffset != state.BodySizeOffset)
            {
                Undo.RecordObject(timeline, "Edit Body Track Data");
                state.Frame = currentFrame;
                state.BodyCenterOffset = centerOffset;
                state.BodySizeOffset = sizeOffset;
                ExpandTrackToFrame(track, currentFrame);
                track.SetKey(state);
                MarkDirty();
                SceneView.RepaintAll();
            }

            EditorGUI.BeginDisabledGroup(keyIndex >= 0);
            if (GUILayout.Button("Add Key Frame") && keyIndex < 0)
            {
                AddBodyKeyFrame(track);
            }
            EditorGUI.EndDisabledGroup();

            if (keyIndex >= 0 && GUILayout.Button("Remove Key Frame"))
            {
                Undo.RecordObject(timeline, "Remove Body Key Frame");
                track.RemoveKeyAt(keyIndex);
                MarkDirty();
                SceneView.RepaintAll();
            }
        }

        private void DrawHitBoxTrackInspector(MotionTimelineHitBoxTrackDefinition track)
        {
            EditorGUILayout.LabelField("Custom Data", EditorStyles.boldLabel);
            MotionTimelineInterpolationMode interpolation =
                (MotionTimelineInterpolationMode)EditorGUILayout.EnumPopup("Interpolation", track.Interpolation);
            if (interpolation != track.Interpolation)
            {
                Undo.RecordObject(timeline, "Edit HitBox Track Interpolation");
                track.Interpolation = interpolation;
                MarkDirty();
                SceneView.RepaintAll();
            }

            if (!track.ContainsFrame(currentFrame))
            {
                EditorGUILayout.HelpBox(
                    "Current Frame is outside this Track's active range. The values below are the inherited HitBox state.",
                    MessageType.Info);
            }

            int keyIndex = track.FindKeyIndex(currentFrame);
            MotionTimelineHitBoxKey state = track.Evaluate(currentFrame);
            EditorGUILayout.LabelField(
                "State at Frame " + currentFrame + (keyIndex >= 0 ? " (Key Frame)" : " (Inherited)"),
                EditorStyles.miniLabel);
            if (keyIndex < 0)
            {
                EditorGUILayout.HelpBox(
                    "This frame inherits the last HitBox state. Editing a field will create a Key Frame here.",
                    MessageType.None);
            }

            bool active = EditorGUILayout.Toggle("Active", state.Active);
            Vector2 center = EditorGUILayout.Vector2Field("Center", state.Center);
            Vector2 size = NonZero(EditorGUILayout.Vector2Field("Size", state.Size));
            if (active != state.Active || center != state.Center || size != state.Size)
            {
                Undo.RecordObject(timeline, "Edit HitBox Track Data");
                state.Frame = currentFrame;
                state.Active = active;
                state.Center = center;
                state.Size = size;
                ExpandTrackToFrame(track, currentFrame);
                track.SetKey(state);
                MarkDirty();
                SceneView.RepaintAll();
            }

            EditorGUI.BeginDisabledGroup(keyIndex >= 0);
            if (GUILayout.Button("Add Key Frame") && keyIndex < 0)
            {
                AddHitBoxKeyFrame(track);
            }
            EditorGUI.EndDisabledGroup();

            if (keyIndex >= 0 && GUILayout.Button("Remove Key Frame"))
            {
                Undo.RecordObject(timeline, "Remove HitBox Key Frame");
                track.RemoveKeyAt(keyIndex);
                MarkDirty();
                SceneView.RepaintAll();
            }
        }

        private void DrawStateTrackInspector(MotionTimelineStateTrackDefinition track)
        {
            EditorGUILayout.LabelField("Custom Data", EditorStyles.boldLabel);
            if (!track.ContainsFrame(currentFrame))
            {
                EditorGUILayout.HelpBox(
                    "Current Frame is outside this Track's active range. The values below are inherited.",
                    MessageType.Info);
            }

            int keyIndex = track.FindKeyIndex(currentFrame);
            MotionTimelineStateKey state = track.Evaluate(currentFrame);
            EditorGUILayout.LabelField(
                "State at Frame " + currentFrame + (keyIndex >= 0 ? " (Key Frame)" : " (Inherited)"),
                EditorStyles.miniLabel);
            if (keyIndex < 0)
            {
                EditorGUILayout.HelpBox(
                    "This frame inherits the previous State value. Editing a field will create a Key Frame here.",
                    MessageType.None);
            }

            string stateId = EditorGUILayout.TextField("State Id", state.StateId);
            bool value = EditorGUILayout.Toggle("Value", state.Value);
            if (stateId != state.StateId || value != state.Value)
            {
                Undo.RecordObject(timeline, "Edit State Track Data");
                state.Frame = currentFrame;
                state.StateId = stateId;
                state.Value = value;
                ExpandTrackToFrame(track, currentFrame);
                track.SetKey(state);
                MarkDirty();
                SceneView.RepaintAll();
            }

            EditorGUI.BeginDisabledGroup(keyIndex >= 0);
            if (GUILayout.Button("Add Key Frame") && keyIndex < 0)
            {
                AddStateKeyFrame(track);
            }
            EditorGUI.EndDisabledGroup();

            if (keyIndex >= 0 && GUILayout.Button("Remove Key Frame"))
            {
                Undo.RecordObject(timeline, "Remove State Key Frame");
                track.RemoveKeyAt(keyIndex);
                MarkDirty();
                SceneView.RepaintAll();
            }
        }

        private void AddStateKeyFrame(MotionTimelineStateTrackDefinition track)
        {
            if (track == null)
            {
                return;
            }

            MotionTimelineStateKey state = track.Evaluate(currentFrame);
            state.Frame = currentFrame;
            Undo.RecordObject(timeline, "Add State Key Frame");
            ExpandTrackToFrame(track, currentFrame);
            track.SetKey(state);
            MarkDirty();
            SceneView.RepaintAll();
        }

        private void AddTrack(MotionTimelineTrackType type, int initialFrame)
        {
            MotionTimelineTrackDefinition track;
            string prefix;
            if (type == MotionTimelineTrackType.Body)
            {
                track = new MotionTimelineBodyTrackDefinition();
                prefix = "Body";
            }
            else if (type == MotionTimelineTrackType.HitBox)
            {
                track = new MotionTimelineHitBoxTrackDefinition();
                prefix = "HitBox";
            }
            else
            {
                track = new MotionTimelineStateTrackDefinition();
                prefix = "State";
            }

            track.TrackId = prefix.ToLowerInvariant() + (GetTrackCount(type) + 1);
            track.DisplayName = prefix + (GetTrackCount(type) + 1);
            track.SetRange(initialFrame, initialFrame, timeline.TotalFrames);
            Undo.RecordObject(timeline, "Add Timeline Track");
            timeline.AddTrack(track);
            selectedTrack = timeline.Tracks.Length - 1;
            MarkDirty();
        }

        private void AddBodyKeyFrame(MotionTimelineBodyTrackDefinition track)
        {
            if (track == null)
            {
                return;
            }

            MotionTimelineBodyKey state = track.Evaluate(currentFrame);
            state.Frame = currentFrame;
            Undo.RecordObject(timeline, "Add Body Key Frame");
            ExpandTrackToFrame(track, currentFrame);
            track.SetKey(state);
            MarkDirty();
            SceneView.RepaintAll();
        }

        private void AddHitBoxKeyFrame(MotionTimelineHitBoxTrackDefinition track)
        {
            if (track == null)
            {
                return;
            }

            MotionTimelineHitBoxKey state = track.Evaluate(currentFrame);
            state.Frame = currentFrame;
            Undo.RecordObject(timeline, "Add HitBox Key Frame");
            ExpandTrackToFrame(track, currentFrame);
            track.SetKey(state);
            MarkDirty();
            SceneView.RepaintAll();
        }

        private static void ExpandTrackToFrame(MotionTimelineTrackDefinition track, int frame)
        {
            if (frame < track.StartFrame)
            {
                track.StartFrame = frame;
            }
            else if (frame > track.EndFrame)
            {
                track.EndFrame = frame;
            }
        }

        private int GetTrackCount(MotionTimelineTrackType type)
        {
            int count = 0;
            for (int index = 0; index < timeline.Tracks.Length; index++)
            {
                if (timeline.Tracks[index] != null && timeline.Tracks[index].Type == type)
                {
                    count++;
                }
            }

            return count;
        }

        private void SelectTrack(int index)
        {
            selectedTrack = index;
            Repaint();
        }

        private void DeleteTrack(int index)
        {
            if (timeline == null || index < 0 || index >= timeline.Tracks.Length)
            {
                return;
            }

            Undo.RecordObject(timeline, "Delete Timeline Track");
            timeline.RemoveTrackAt(index);
            selectedTrack = -1;
            MarkDirty();
        }

        private void HandleTimelineShortcuts()
        {
            Event evt = Event.current;
            if (evt.type != EventType.KeyDown)
            {
                return;
            }

            if (evt.keyCode == KeyCode.LeftArrow || evt.keyCode == KeyCode.RightArrow)
            {
                SetCurrentFrame(currentFrame + (evt.keyCode == KeyCode.LeftArrow ? -1 : 1));
                evt.Use();
                return;
            }

        }

        private void OnSceneGui(SceneView sceneView)
        {
            if (timeline == null)
            {
                return;
            }

            Matrix4x4 matrix = previewRoot == null ? Matrix4x4.identity : previewRoot.transform.localToWorldMatrix;
            using (new Handles.DrawingScope(matrix))
            {
                Handles.color = new Color(1f, 1f, 1f, 0.6f);
                Handles.DrawLine(Vector3.left * 0.08f, Vector3.right * 0.08f);
                Handles.DrawLine(Vector3.down * 0.08f, Vector3.up * 0.08f);

                Vector2 totalBodyCenterOffset = Vector2.zero;
                Vector2 totalBodySizeOffset = Vector2.zero;
                for (int index = 0; index < timeline.Tracks.Length; index++)
                {
                    MotionTimelineBodyTrackDefinition bodyTrack = timeline.Tracks[index] as MotionTimelineBodyTrackDefinition;
                    if (bodyTrack != null && bodyTrack.ContainsFrame(currentFrame))
                    {
                        MotionTimelineBodyKey bodyState = bodyTrack.Evaluate(currentFrame);
                        totalBodyCenterOffset += bodyState.BodyCenterOffset;
                        totalBodySizeOffset += bodyState.BodySizeOffset;
                    }
                }

                if (fighterRole != null)
                {
                    Vector2 baseSize = fighterRole.StandingHurtBoxSize;
                    Vector2 bodySize = new Vector2(
                        Mathf.Max(0.01f, baseSize.x + totalBodySizeOffset.x),
                        Mathf.Max(0.01f, baseSize.y + totalBodySizeOffset.y));
                    Vector2 bodyCenter = totalBodyCenterOffset + new Vector2(0f, baseSize.y * 0.5f);
                    Handles.color = new Color(0.35f, 0.95f, 0.45f, 0.8f);
                    Handles.DrawWireCube(
                        new Vector3(bodyCenter.x, bodyCenter.y, 0f),
                        new Vector3(bodySize.x, bodySize.y, 0.04f));
                }

                for (int index = 0; index < timeline.Tracks.Length; index++)
                {
                    MotionTimelineHitBoxTrackDefinition hitBoxTrack = timeline.Tracks[index] as MotionTimelineHitBoxTrackDefinition;
                    if (hitBoxTrack == null)
                    {
                        continue;
                    }

                    MotionTimelineHitBoxKey hitBoxState = hitBoxTrack.Evaluate(currentFrame);
                    bool active = hitBoxTrack.ContainsFrame(currentFrame) && hitBoxState.Active;
                    if (!active && !showInactiveHitBoxes)
                    {
                        continue;
                    }

                    bool selected = index == selectedTrack;
                    Handles.color = selected
                        ? new Color(1f, 0.48f, 0.2f, active ? 1f : 0.45f)
                        : new Color(0.9f, 0.2f, 0.16f, active ? 1f : 0.3f);
                    Vector2 center = hitBoxState.Center + totalBodyCenterOffset;
                    Handles.DrawWireCube(
                        new Vector3(center.x, center.y, 0f),
                        new Vector3(hitBoxState.Size.x, hitBoxState.Size.y, 0.04f));
                }
            }
        }

        private void OnEditorUpdate()
        {
            if (!isPlaying || timeline == null || timeline.TotalFrames <= 0)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            double frameDuration = 1.0 / Mathf.Max(1f, timeline.FrameRate * playbackSpeed);
            if (lastPlaybackTime <= 0.0)
            {
                lastPlaybackTime = now;
            }

            int steps = Mathf.FloorToInt((float)((now - lastPlaybackTime) / frameDuration));
            if (steps <= 0)
            {
                return;
            }

            lastPlaybackTime += steps * frameDuration;
            int nextFrame = currentFrame + steps;
            if (nextFrame >= timeline.TotalFrames)
            {
                if (loopPreview)
                {
                    nextFrame %= timeline.TotalFrames;
                }
                else
                {
                    nextFrame = timeline.TotalFrames - 1;
                    isPlaying = false;
                }
            }

            SetCurrentFrame(nextFrame);
        }

        private void SetCurrentFrame(int frame)
        {
            if (timeline == null || timeline.TotalFrames <= 0)
            {
                currentFrame = 0;
                return;
            }

            currentFrame = loopPreview
                ? Mod(frame, timeline.TotalFrames)
                : Mathf.Clamp(frame, 0, timeline.TotalFrames - 1);
            SamplePreview();
            Repaint();
            SceneView.RepaintAll();
        }

        private void SetTimeline(MotionTimelineAsset nextTimeline)
        {
            timeline = nextTimeline;
            previewAnimation = timeline == null ? null : timeline.SceneViewAnimation;
            fighterRole = timeline == null ? null : timeline.FighterRole;
            currentFrame = 0;
            selectedTrack = -1;
            timelineZoom = 1f;
            timelineScroll = 0f;
            ClampTrackRanges();
            SpawnPreview();
        }

        private void CreateTimelineAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Motion Timeline",
                "MotionTimeline",
                "asset",
                "Choose a location for the MotionTimeline asset.");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            MotionTimelineAsset asset = CreateInstance<MotionTimelineAsset>();
            asset.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            SetTimeline(asset);
        }

        private void ClampTrackRanges()
        {
            if (timeline == null)
            {
                return;
            }

            for (int index = 0; index < timeline.Tracks.Length; index++)
            {
                MotionTimelineTrackDefinition track = timeline.Tracks[index];
                if (track != null)
                {
                    track.SetRange(track.StartFrame, track.EndFrame, timeline.TotalFrames);
                }
            }
        }

        private void SpawnPreview()
        {
            DestroyPreview();
            GameObject source = fighterRole == null ? null : fighterRole.Prefab;
            if (source == null)
            {
                return;
            }

            previewRoot = new GameObject(source.name + " Motion Timeline Preview") { hideFlags = HideFlags.DontSave };
            previewInstance = PrefabUtility.IsPartOfPrefabAsset(source)
                ? PrefabUtility.InstantiatePrefab(source) as GameObject
                : Instantiate(source);
            if (previewInstance == null)
            {
                DestroyPreview();
                return;
            }

            previewInstance.name = source.name + " Motion Timeline Preview Instance";
            previewInstance.hideFlags = HideFlags.DontSave;
            previewInstance.transform.SetParent(previewRoot.transform, false);
            ApplyPreviewFacingRight();
            MatchRuntimeCamera();
            SamplePreview();
        }

        private void DestroyPreview()
        {
            if (previewRoot != null)
            {
                DestroyImmediate(previewRoot);
            }
            else if (previewInstance != null)
            {
                DestroyImmediate(previewInstance);
            }

            previewRoot = null;
            previewInstance = null;
        }

        private void SamplePreview()
        {
            if (timeline == null || previewInstance == null || previewAnimation == null)
            {
                SceneView.RepaintAll();
                return;
            }

            float time = Mathf.Clamp(timeline.FrameToSeconds(currentFrame), 0f, previewAnimation.length);
            Animator animator = previewInstance.GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                if (!AnimationMode.InAnimationMode())
                {
                    AnimationMode.StartAnimationMode();
                }

                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(animator.gameObject, previewAnimation, time);
                AnimationMode.EndSampling();
            }
            else
            {
                previewAnimation.SampleAnimation(previewInstance, time);
            }

            ApplyPreviewFacingRight();
            SceneView.RepaintAll();
        }

        private void ApplyPreviewFacingRight()
        {
            if (previewInstance == null)
            {
                return;
            }

            FighterAvatar avatar = previewInstance.GetComponentInChildren<FighterAvatar>(true);
            if (avatar != null)
            {
                avatar.RequiredVisualRoot.localRotation = avatar.FacingRightRotation;
                return;
            }

            Transform visualRoot = previewInstance.transform.Find("VisualRoot");
            if (visualRoot != null)
            {
                visualRoot.localRotation = Quaternion.Euler(0f, 90f, 0f);
                return;
            }

            previewInstance.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        }

        private int GetPreviewClipFrameCount()
        {
            return previewAnimation == null || timeline == null
                ? 1
                : Mathf.Max(1, Mathf.RoundToInt(previewAnimation.length * timeline.FrameRate));
        }

        private void MatchAnimationLength()
        {
            if (timeline == null || previewAnimation == null)
            {
                return;
            }

            Undo.RecordObject(timeline, "Match Timeline To Animation");
            timeline.SetTotalFrames(GetPreviewClipFrameCount());
            ClampTrackRanges();
            MarkDirty();
        }

        private static Color GetTrackColor(MotionTimelineTrackDefinition track, bool selected)
        {
            if (track.Type == MotionTimelineTrackType.Body)
            {
                return selected
                    ? new Color(0.72f, 0.52f, 1f, 1f)
                    : new Color(0.52f, 0.38f, 0.84f, 0.9f);
            }

            if (track.Type == MotionTimelineTrackType.State)
            {
                return selected
                    ? new Color(0.35f, 0.75f, 0.38f, 1f)
                    : new Color(0.24f, 0.52f, 0.27f, 0.9f);
            }

            return selected
                ? new Color(1f, 0.48f, 0.2f, 1f)
                : new Color(0.82f, 0.2f, 0.16f, 0.9f);
        }

        private static Vector2 NonZero(Vector2 value)
        {
            return new Vector2(Mathf.Max(0.01f, Mathf.Abs(value.x)), Mathf.Max(0.01f, Mathf.Abs(value.y)));
        }

        private static int Mod(int value, int modulo)
        {
            int result = value % modulo;
            return result < 0 ? result + modulo : result;
        }

        private static void MatchRuntimeCamera()
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                return;
            }

            sceneView.in2DMode = true;
            sceneView.pivot = new Vector3(CombatCameraSettings.CenterX, CombatCameraSettings.CenterY, 0f);
            sceneView.size = CombatCameraSettings.OrthographicSize;
            sceneView.Repaint();
        }

        private void MarkDirty()
        {
            if (timeline != null)
            {
                EditorUtility.SetDirty(timeline);
            }

            Repaint();
        }
    }
}
