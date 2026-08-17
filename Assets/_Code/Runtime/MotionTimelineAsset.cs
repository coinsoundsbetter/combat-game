using GLMFighter.Core;
using UnityEngine;

namespace GLMFighter.Runtime
{
    /// <summary>
    /// Authoring motion timeline. Runtime consumers expand it into deterministic
    /// match-local frame data without modifying the asset.
    /// </summary>
    [CreateAssetMenu(menuName = "GLM Fighter/Motion Timeline", fileName = "MotionTimeline")]
    public sealed class MotionTimelineAsset : ScriptableObject
    {
        [SerializeField] private string timelineId = "timeline";
        [SerializeField] private int frameRate = BattleSimulation.FramesPerSecond;
        [SerializeField] private int totalFrames = 1;
        [SerializeField] private bool loop;
        [SerializeField] private AnimationClip sceneViewAnimation;
        [SerializeField] private FighterRoleDefinition fighterRole;
        [SerializeReference] private MotionTimelineTrackDefinition[] tracks = new MotionTimelineTrackDefinition[0];

        public string TimelineId => timelineId;
        public int FrameRate => frameRate <= 0 ? BattleSimulation.FramesPerSecond : frameRate;
        public int TotalFrames => Mathf.Max(1, totalFrames);
        public bool Loop => loop;
        public float DurationSeconds => TotalFrames / (float)FrameRate;
        public AnimationClip SceneViewAnimation => sceneViewAnimation;
        public FighterRoleDefinition FighterRole => fighterRole;
        public MotionTimelineTrackDefinition[] Tracks => tracks ?? new MotionTimelineTrackDefinition[0];

        public void Configure(string id, int rate, int frames, bool shouldLoop)
        {
            timelineId = string.IsNullOrEmpty(id) ? "timeline" : id;
            frameRate = rate <= 0 ? BattleSimulation.FramesPerSecond : rate;
            totalFrames = Mathf.Max(1, frames);
            loop = shouldLoop;
        }

        public void SetTimelineId(string value)
        {
            timelineId = string.IsNullOrEmpty(value) ? "timeline" : value;
        }

        public void SetFrameRate(int value)
        {
            frameRate = Mathf.Max(1, value);
        }

        public void SetTotalFrames(int value)
        {
            totalFrames = Mathf.Max(1, value);
        }

        public void SetLoop(bool value)
        {
            loop = value;
        }

        public float FrameToSeconds(int frame)
        {
            return frame / (float)FrameRate;
        }

        public void SetSceneViewAnimation(AnimationClip value)
        {
            sceneViewAnimation = value;
        }

        public void SetFighterRole(FighterRoleDefinition value)
        {
            fighterRole = value;
        }

        public MotionTimelineBodyTrackDefinition GetBodyTrack()
        {
            for (int index = 0; index < Tracks.Length; index++)
            {
                MotionTimelineBodyTrackDefinition track = Tracks[index] as MotionTimelineBodyTrackDefinition;
                if (track != null)
                {
                    return track;
                }
            }

            return null;
        }

        public bool SampleState(string stateId, int frame, bool defaultValue = false)
        {
            bool result = defaultValue;
            for (int index = 0; index < Tracks.Length; index++)
            {
                MotionTimelineStateTrackDefinition stateTrack =
                    Tracks[index] as MotionTimelineStateTrackDefinition;
                if (stateTrack != null)
                {
                    result = stateTrack.SampleState(stateId, frame, result);
                }
            }

            return result;
        }

        /// <summary>
        /// Expands the authoring timeline into match-local deterministic data.
        /// The asset itself is never modified during this conversion.
        /// </summary>
        public CombatMoveData BuildRuntimeMoveData(CombatMoveId moveId)
        {
            CombatFrameData[] runtimeFrames = new CombatFrameData[TotalFrames];
            CombatStateRange[] stateRanges = BuildStates();
            for (int frameIndex = 0; frameIndex < runtimeFrames.Length; frameIndex++)
            {
                Vector2 entityOffset = Vector2.zero;
                Vector2 bodyCenterOffset = Vector2.zero;
                Vector2 bodySizeOffset = Vector2.zero;

                for (int trackIndex = 0; trackIndex < Tracks.Length; trackIndex++)
                {
                    MotionTimelineBodyTrackDefinition bodyTrack =
                        Tracks[trackIndex] as MotionTimelineBodyTrackDefinition;
                    if (bodyTrack == null || !bodyTrack.ContainsFrame(frameIndex))
                    {
                        continue;
                    }

                    MotionTimelineBodyKey bodyState = bodyTrack.Evaluate(frameIndex);
                    entityOffset += bodyState.EntityOffset;
                    bodyCenterOffset += bodyState.BodyCenterOffset;
                    bodySizeOffset += bodyState.BodySizeOffset;
                }

                runtimeFrames[frameIndex] = new CombatFrameData
                {
                    Flags = CombatFrameFlags.None,
                    EntityOffset = new SimVector2(
                        SimMath.FromUnity(entityOffset.x),
                        SimMath.FromUnity(entityOffset.y)),
                    BoundsCenterOffset = new SimVector2(
                        SimMath.FromUnity(bodyCenterOffset.x),
                        SimMath.FromUnity(bodyCenterOffset.y)),
                    BoundsHalfSizeOffsetX = SimMath.FromUnity(bodySizeOffset.x * 0.5f),
                    BoundsHalfSizeOffsetY = SimMath.FromUnity(bodySizeOffset.y * 0.5f),
                    Boxes = BuildHitBoxes(frameIndex)
                };

                if (runtimeFrames[frameIndex].Boxes.Length > 0)
                {
                    runtimeFrames[frameIndex].Flags |= CombatFrameFlags.Active;
                }
            }

            return new CombatMoveData
            {
                MoveId = moveId,
                FrameRate = FrameRate,
                Loop = Loop,
                Frames = runtimeFrames,
                States = stateRanges
            };
        }

        public CombatMoveData ToCoreMoveData(CombatMoveId moveId)
        {
            return BuildRuntimeMoveData(moveId);
        }

        private CombatBox[] BuildHitBoxes(int frameIndex)
        {
            CombatBox[] result = new CombatBox[0];

            for (int trackIndex = 0; trackIndex < Tracks.Length; trackIndex++)
            {
                MotionTimelineHitBoxTrackDefinition hitBoxTrack =
                    Tracks[trackIndex] as MotionTimelineHitBoxTrackDefinition;
                if (hitBoxTrack == null || !hitBoxTrack.ContainsFrame(frameIndex))
                {
                    continue;
                }

                MotionTimelineHitBoxKey state = hitBoxTrack.Evaluate(frameIndex);
                if (!state.Active)
                {
                    continue;
                }

                result = AppendBox(result, new CombatBox
                {
                    Kind = CombatBoxKind.Hit,
                    LocalCenterX = SimMath.FromUnity(state.Center.x),
                    LocalCenterY = SimMath.FromUnity(state.Center.y),
                    HalfWidth = SimMath.FromUnity(Mathf.Abs(state.Size.x) * 0.5f),
                    HalfHeight = SimMath.FromUnity(Mathf.Abs(state.Size.y) * 0.5f),
                    Group = 0
                });
            }

            return result;
        }

        private CombatStateRange[] BuildStates()
        {
            CombatStateRange[] result = new CombatStateRange[0];
            for (int trackIndex = 0; trackIndex < Tracks.Length; trackIndex++)
            {
                MotionTimelineStateTrackDefinition stateTrack =
                    Tracks[trackIndex] as MotionTimelineStateTrackDefinition;
                if (stateTrack == null)
                {
                    continue;
                }

                int startFrame = Mathf.Clamp(stateTrack.StartFrame, 0, TotalFrames - 1);
                int endFrame = Mathf.Clamp(stateTrack.EndFrame, startFrame, TotalFrames - 1);
                MotionTimelineStateKey[] keys = stateTrack.Keys;
                int segmentStart = startFrame;
                MotionTimelineStateKey state = stateTrack.Evaluate(startFrame);
                for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                {
                    MotionTimelineStateKey key = keys[keyIndex];
                    if (key.Frame <= segmentStart || key.Frame > endFrame)
                    {
                        continue;
                    }

                    result = AppendStateIfValid(
                        result,
                        state,
                        segmentStart,
                        key.Frame - 1);
                    state = key;
                    segmentStart = key.Frame;
                }

                result = AppendStateIfValid(result, state, segmentStart, endFrame);
            }

            return result;
        }

        private static CombatStateRange[] AppendStateIfValid(
            CombatStateRange[] source,
            MotionTimelineStateKey state,
            int startFrame,
            int endFrame)
        {
            if (startFrame > endFrame || string.IsNullOrEmpty(state.StateId))
            {
                return source;
            }

            return AppendState(source, new CombatStateRange
            {
                StateId = state.StateId,
                StartFrame = startFrame,
                EndFrame = endFrame,
                Value = state.Value
            });
        }

        private static CombatStateRange[] AppendState(CombatStateRange[] source, CombatStateRange value)
        {
            CombatStateRange[] result = new CombatStateRange[source.Length + 1];
            for (int index = 0; index < source.Length; index++)
            {
                result[index] = source[index];
            }

            result[result.Length - 1] = value;
            return result;
        }

        private static CombatBox[] AppendBox(CombatBox[] source, CombatBox value)
        {
            CombatBox[] result = new CombatBox[source.Length + 1];
            for (int index = 0; index < source.Length; index++)
            {
                result[index] = source[index];
            }

            result[result.Length - 1] = value;
            return result;
        }

        public void SetTracks(MotionTimelineTrackDefinition[] values)
        {
            tracks = values ?? new MotionTimelineTrackDefinition[0];
        }

        public void AddTrack(MotionTimelineTrackDefinition track)
        {
            if (track == null)
            {
                return;
            }

            MotionTimelineTrackDefinition[] source = Tracks;
            MotionTimelineTrackDefinition[] next = new MotionTimelineTrackDefinition[source.Length + 1];
            for (int index = 0; index < source.Length; index++)
            {
                next[index] = source[index];
            }

            next[next.Length - 1] = track;
            tracks = next;
        }

        public void RemoveTrackAt(int index)
        {
            MotionTimelineTrackDefinition[] source = Tracks;
            if (index < 0 || index >= source.Length)
            {
                return;
            }

            MotionTimelineTrackDefinition[] next = new MotionTimelineTrackDefinition[source.Length - 1];
            int nextIndex = 0;
            for (int sourceIndex = 0; sourceIndex < source.Length; sourceIndex++)
            {
                if (sourceIndex != index)
                {
                    next[nextIndex] = source[sourceIndex];
                    nextIndex++;
                }
            }

            tracks = next;
        }

        private void OnValidate()
        {
            frameRate = Mathf.Max(1, frameRate);
            totalFrames = Mathf.Max(1, totalFrames);
            tracks = tracks ?? new MotionTimelineTrackDefinition[0];
        }
    }

    public enum MotionTimelineTrackType
    {
        Body,
        HitBox,
        State
    }

    public enum MotionTimelineInterpolationMode
    {
        Hold,
        Lerp
    }

    [System.Serializable]
    public abstract class MotionTimelineTrackDefinition
    {
        [SerializeField] private string trackId = "track";
        [SerializeField] private string displayName = "Track";
        [SerializeField] private int startFrame;
        [SerializeField] private int endFrame;

        public string TrackId { get { return trackId; } set { trackId = value; } }
        public string DisplayName { get { return displayName; } set { displayName = value; } }
        public int StartFrame { get { return startFrame; } set { startFrame = value; } }
        public int EndFrame { get { return endFrame; } set { endFrame = value; } }
        public abstract MotionTimelineTrackType Type { get; }

        public bool ContainsFrame(int frame)
        {
            return frame >= StartFrame && frame <= EndFrame;
        }

        public void SetRange(int start, int end, int frameCount)
        {
            int maxFrame = Mathf.Max(0, frameCount - 1);
            startFrame = Mathf.Clamp(start, 0, maxFrame);
            endFrame = Mathf.Clamp(end, startFrame, maxFrame);
        }
    }

    [System.Serializable]
    public sealed class MotionTimelineBodyTrackDefinition : MotionTimelineTrackDefinition
    {
        [SerializeField] private Vector2 entityOffset;
        [SerializeField] private Vector2 bodyCenterOffset;
        [SerializeField] private Vector2 bodySizeOffset;
        [SerializeField] private MotionTimelineInterpolationMode interpolation;
        [SerializeField] private MotionTimelineBodyKey[] keys = new MotionTimelineBodyKey[0];

        public override MotionTimelineTrackType Type => MotionTimelineTrackType.Body;
        public Vector2 EntityOffset { get { return entityOffset; } set { entityOffset = value; } }
        public Vector2 BodyCenterOffset { get { return bodyCenterOffset; } set { bodyCenterOffset = value; } }
        public Vector2 BodySizeOffset { get { return bodySizeOffset; } set { bodySizeOffset = value; } }
        public MotionTimelineInterpolationMode Interpolation { get { return interpolation; } set { interpolation = value; } }
        public MotionTimelineBodyKey[] Keys
        {
            get { return keys ?? new MotionTimelineBodyKey[0]; }
            set { keys = value ?? new MotionTimelineBodyKey[0]; }
        }

        public MotionTimelineBodyKey Evaluate(int frame)
        {
            MotionTimelineBodyKey result = new MotionTimelineBodyKey
            {
                Frame = frame,
                EntityOffset = entityOffset,
                BodyCenterOffset = bodyCenterOffset,
                BodySizeOffset = bodySizeOffset
            };

            int latestKeyFrame = int.MinValue;
            MotionTimelineBodyKey nextKey = new MotionTimelineBodyKey();
            bool hasPreviousKey = false;
            bool hasNextKey = false;
            MotionTimelineBodyKey[] source = Keys;
            for (int index = 0; index < source.Length; index++)
            {
                if (source[index].Frame <= frame && source[index].Frame >= latestKeyFrame)
                {
                    result = source[index];
                    latestKeyFrame = source[index].Frame;
                    hasPreviousKey = true;
                }
                else if (source[index].Frame > frame &&
                         (!hasNextKey || source[index].Frame < nextKey.Frame))
                {
                    nextKey = source[index];
                    hasNextKey = true;
                }
            }

            if (interpolation == MotionTimelineInterpolationMode.Lerp &&
                hasPreviousKey && hasNextKey &&
                nextKey.Frame > result.Frame)
            {
                float t = Mathf.Clamp01((frame - result.Frame) / (float)(nextKey.Frame - result.Frame));
                result = new MotionTimelineBodyKey
                {
                    Frame = frame,
                    EntityOffset = Vector2.Lerp(result.EntityOffset, nextKey.EntityOffset, t),
                    BodyCenterOffset = Vector2.Lerp(result.BodyCenterOffset, nextKey.BodyCenterOffset, t),
                    BodySizeOffset = Vector2.Lerp(result.BodySizeOffset, nextKey.BodySizeOffset, t)
                };
            }

            return result;
        }

        public int FindKeyIndex(int frame)
        {
            for (int index = 0; index < Keys.Length; index++)
            {
                if (Keys[index].Frame == frame)
                {
                    return index;
                }
            }

            return -1;
        }

        public void SetKey(MotionTimelineBodyKey value)
        {
            int existingIndex = FindKeyIndex(value.Frame);
            if (existingIndex >= 0)
            {
                MotionTimelineBodyKey[] next = Keys;
                next[existingIndex] = value;
                Keys = next;
                return;
            }

            MotionTimelineBodyKey[] source = Keys;
            MotionTimelineBodyKey[] result = new MotionTimelineBodyKey[source.Length + 1];
            int insertIndex = 0;
            while (insertIndex < source.Length && source[insertIndex].Frame < value.Frame)
            {
                result[insertIndex] = source[insertIndex];
                insertIndex++;
            }

            result[insertIndex] = value;
            for (int sourceIndex = insertIndex; sourceIndex < source.Length; sourceIndex++)
            {
                result[sourceIndex + 1] = source[sourceIndex];
            }

            Keys = result;
        }

        public void RemoveKeyAt(int index)
        {
            if (index < 0 || index >= Keys.Length)
            {
                return;
            }

            MotionTimelineBodyKey[] source = Keys;
            MotionTimelineBodyKey[] result = new MotionTimelineBodyKey[source.Length - 1];
            int resultIndex = 0;
            for (int sourceIndex = 0; sourceIndex < source.Length; sourceIndex++)
            {
                if (sourceIndex != index)
                {
                    result[resultIndex] = source[sourceIndex];
                    resultIndex++;
                }
            }

            Keys = result;
        }
    }

    [System.Serializable]
    public struct MotionTimelineBodyKey
    {
        public int Frame;
        public Vector2 EntityOffset;
        public Vector2 BodyCenterOffset;
        public Vector2 BodySizeOffset;
    }

    [System.Serializable]
    public sealed class MotionTimelineHitBoxTrackDefinition : MotionTimelineTrackDefinition
    {
        [SerializeField] private Vector2 center;
        [SerializeField] private Vector2 size = new Vector2(0.7f, 0.42f);
        [SerializeField] private bool active = true;
        [SerializeField] private MotionTimelineInterpolationMode interpolation;
        [SerializeField] private MotionTimelineHitBoxKey[] keys = new MotionTimelineHitBoxKey[0];

        public override MotionTimelineTrackType Type => MotionTimelineTrackType.HitBox;
        public Vector2 Center { get { return center; } set { center = value; } }
        public Vector2 Size { get { return size; } set { size = value; } }
        public bool Active { get { return active; } set { active = value; } }
        public MotionTimelineInterpolationMode Interpolation { get { return interpolation; } set { interpolation = value; } }
        public MotionTimelineHitBoxKey[] Keys
        {
            get { return keys ?? new MotionTimelineHitBoxKey[0]; }
            set { keys = value ?? new MotionTimelineHitBoxKey[0]; }
        }

        public MotionTimelineHitBoxKey Evaluate(int frame)
        {
            MotionTimelineHitBoxKey result = new MotionTimelineHitBoxKey
            {
                Frame = frame,
                Center = center,
                Size = size,
                Active = active
            };

            int latestKeyFrame = int.MinValue;
            MotionTimelineHitBoxKey nextKey = new MotionTimelineHitBoxKey();
            bool hasPreviousKey = false;
            bool hasNextKey = false;
            MotionTimelineHitBoxKey[] source = Keys;
            for (int index = 0; index < source.Length; index++)
            {
                if (source[index].Frame <= frame && source[index].Frame >= latestKeyFrame)
                {
                    result = source[index];
                    latestKeyFrame = source[index].Frame;
                    hasPreviousKey = true;
                }
                else if (source[index].Frame > frame &&
                         (!hasNextKey || source[index].Frame < nextKey.Frame))
                {
                    nextKey = source[index];
                    hasNextKey = true;
                }
            }

            if (interpolation == MotionTimelineInterpolationMode.Lerp &&
                hasPreviousKey && hasNextKey &&
                nextKey.Frame > result.Frame)
            {
                float t = Mathf.Clamp01((frame - result.Frame) / (float)(nextKey.Frame - result.Frame));
                result = new MotionTimelineHitBoxKey
                {
                    Frame = frame,
                    Center = Vector2.Lerp(result.Center, nextKey.Center, t),
                    Size = Vector2.Lerp(result.Size, nextKey.Size, t),
                    Active = result.Active
                };
            }

            return result;
        }

        public int FindKeyIndex(int frame)
        {
            for (int index = 0; index < Keys.Length; index++)
            {
                if (Keys[index].Frame == frame)
                {
                    return index;
                }
            }

            return -1;
        }

        public void SetKey(MotionTimelineHitBoxKey value)
        {
            int existingIndex = FindKeyIndex(value.Frame);
            if (existingIndex >= 0)
            {
                MotionTimelineHitBoxKey[] next = Keys;
                next[existingIndex] = value;
                Keys = next;
                return;
            }

            MotionTimelineHitBoxKey[] source = Keys;
            MotionTimelineHitBoxKey[] result = new MotionTimelineHitBoxKey[source.Length + 1];
            int insertIndex = 0;
            while (insertIndex < source.Length && source[insertIndex].Frame < value.Frame)
            {
                result[insertIndex] = source[insertIndex];
                insertIndex++;
            }

            result[insertIndex] = value;
            for (int sourceIndex = insertIndex; sourceIndex < source.Length; sourceIndex++)
            {
                result[sourceIndex + 1] = source[sourceIndex];
            }

            Keys = result;
        }

        public void RemoveKeyAt(int index)
        {
            if (index < 0 || index >= Keys.Length)
            {
                return;
            }

            MotionTimelineHitBoxKey[] source = Keys;
            MotionTimelineHitBoxKey[] result = new MotionTimelineHitBoxKey[source.Length - 1];
            int resultIndex = 0;
            for (int sourceIndex = 0; sourceIndex < source.Length; sourceIndex++)
            {
                if (sourceIndex != index)
                {
                    result[resultIndex] = source[sourceIndex];
                    resultIndex++;
                }
            }

            Keys = result;
        }
    }

    [System.Serializable]
    public struct MotionTimelineHitBoxKey
    {
        public int Frame;
        public Vector2 Center;
        public Vector2 Size;
        public bool Active;
    }

    [System.Serializable]
    public sealed class MotionTimelineStateTrackDefinition : MotionTimelineTrackDefinition
    {
        [SerializeField] private string stateId = "State";
        [SerializeField] private bool value = true;
        [SerializeField] private MotionTimelineStateKey[] keys = new MotionTimelineStateKey[0];

        public override MotionTimelineTrackType Type => MotionTimelineTrackType.State;
        public string StateId
        {
            get { return stateId; }
            set { stateId = value ?? string.Empty; }
        }

        public bool Value
        {
            get { return value; }
            set { this.value = value; }
        }

        public MotionTimelineStateKey[] Keys
        {
            get { return keys ?? new MotionTimelineStateKey[0]; }
            set { keys = value ?? new MotionTimelineStateKey[0]; }
        }

        public MotionTimelineStateKey Evaluate(int frame)
        {
            MotionTimelineStateKey result = new MotionTimelineStateKey
            {
                Frame = frame,
                StateId = stateId,
                Value = value
            };

            MotionTimelineStateKey[] source = Keys;
            for (int index = 0; index < source.Length; index++)
            {
                if (source[index].Frame <= frame)
                {
                    result = source[index];
                }
                else
                {
                    break;
                }
            }

            return result;
        }

        public int FindKeyIndex(int frame)
        {
            for (int index = 0; index < Keys.Length; index++)
            {
                if (Keys[index].Frame == frame)
                {
                    return index;
                }
            }

            return -1;
        }

        public void SetKey(MotionTimelineStateKey key)
        {
            int existingIndex = FindKeyIndex(key.Frame);
            if (existingIndex >= 0)
            {
                MotionTimelineStateKey[] next = Keys;
                next[existingIndex] = key;
                Keys = next;
                return;
            }

            MotionTimelineStateKey[] source = Keys;
            MotionTimelineStateKey[] result = new MotionTimelineStateKey[source.Length + 1];
            int insertIndex = 0;
            while (insertIndex < source.Length && source[insertIndex].Frame < key.Frame)
            {
                result[insertIndex] = source[insertIndex];
                insertIndex++;
            }

            result[insertIndex] = key;
            for (int sourceIndex = insertIndex; sourceIndex < source.Length; sourceIndex++)
            {
                result[sourceIndex + 1] = source[sourceIndex];
            }

            Keys = result;
        }

        public void RemoveKeyAt(int index)
        {
            if (index < 0 || index >= Keys.Length)
            {
                return;
            }

            MotionTimelineStateKey[] source = Keys;
            MotionTimelineStateKey[] result = new MotionTimelineStateKey[source.Length - 1];
            int resultIndex = 0;
            for (int sourceIndex = 0; sourceIndex < source.Length; sourceIndex++)
            {
                if (sourceIndex != index)
                {
                    result[resultIndex] = source[sourceIndex];
                    resultIndex++;
                }
            }

            Keys = result;
        }

        public bool SampleState(string stateId, int frame, bool defaultValue)
        {
            MotionTimelineStateKey state = Evaluate(frame);
            return ContainsFrame(frame) && state.StateId == stateId ? state.Value : defaultValue;
        }
    }

    [System.Serializable]
    public struct MotionTimelineStateKey
    {
        public int Frame;
        public string StateId;
        public bool Value;
    }
}
