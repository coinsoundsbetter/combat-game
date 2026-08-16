using GLMFighter.Core;
using UnityEngine;

namespace GLMFighter.Runtime
{
    /// <summary>
    /// Static motion timeline configuration. At match setup, its tracks are
    /// expanded into deterministic CombatMoveData for the simulation.
    /// </summary>
    [CreateAssetMenu(menuName = "GLM Fighter/Runtime Motion Data (Cooked)")]
    public sealed class MotionDataDefinition : ScriptableObject
    {
        [SerializeField] private string motionId = "motion";
        [SerializeField] private int frameRate = BattleSimulation.FramesPerSecond;
        [SerializeField] private int totalFrames = 1;
        [SerializeField] private bool loop;
        [SerializeReference] private MotionTrackDefinition[] tracks = new MotionTrackDefinition[0];

        public string MotionId => motionId;
        public int FrameRate => frameRate <= 0 ? BattleSimulation.FramesPerSecond : frameRate;
        public int TotalFrames => Mathf.Max(1, totalFrames);
        public bool Loop => loop;
        public float DurationSeconds => TotalFrames / (float)FrameRate;
        public MotionTrackDefinition[] Tracks => tracks ?? new MotionTrackDefinition[0];

        public void Configure(string newMotionId, int newFrameRate, int newTotalFrames, bool newLoop)
        {
            motionId = string.IsNullOrEmpty(newMotionId) ? motionId : newMotionId;
            frameRate = newFrameRate <= 0 ? BattleSimulation.FramesPerSecond : newFrameRate;
            totalFrames = Mathf.Max(1, newTotalFrames);
            loop = newLoop;
        }

        public void SetTotalFrames(int value)
        {
            totalFrames = Mathf.Max(1, value);
        }

        public float FrameToSeconds(int frame)
        {
            return frame / (float)FrameRate;
        }

        public void SetTracks(MotionTrackDefinition[] definitions)
        {
            tracks = definitions ?? new MotionTrackDefinition[0];
        }

        public void AddTrack(MotionTrackDefinition track)
        {
            if (track == null)
            {
                return;
            }

            MotionTrackDefinition[] source = Tracks;
            MotionTrackDefinition[] next = new MotionTrackDefinition[source.Length + 1];
            for (int index = 0; index < source.Length; index++)
            {
                next[index] = source[index];
            }

            next[next.Length - 1] = track;
            tracks = next;
        }

        public void RemoveTrackAt(int index)
        {
            MotionTrackDefinition[] source = Tracks;
            if (index < 0 || index >= source.Length)
            {
                return;
            }

            MotionTrackDefinition[] next = new MotionTrackDefinition[source.Length - 1];
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

        public MotionHitBoxTrackDefinition GetHitBoxTrack()
        {
            for (int index = 0; index < Tracks.Length; index++)
            {
                MotionHitBoxTrackDefinition track = Tracks[index] as MotionHitBoxTrackDefinition;
                if (track != null)
                {
                    return track;
                }
            }

            return null;
        }

        public MotionBodyTrackDefinition GetBodyTrack()
        {
            for (int index = 0; index < Tracks.Length; index++)
            {
                MotionBodyTrackDefinition track = Tracks[index] as MotionBodyTrackDefinition;
                if (track != null)
                {
                    return track;
                }
            }

            return null;
        }

        public MotionBodyState EvaluateBodyState(int frameIndex)
        {
            int frame = Mathf.Clamp(frameIndex, 0, TotalFrames - 1);
            MotionBodyState result = MotionBodyState.Default;

            for (int index = 0; index < Tracks.Length; index++)
            {
                MotionBodyTrackDefinition track = Tracks[index] as MotionBodyTrackDefinition;
                if (track != null)
                {
                    result = track.Evaluate(frame, TotalFrames, result);
                }
            }

            return result;
        }

        /// <summary>
        /// Creates match-local deterministic data. The resulting value can be
        /// shared by all rollback simulations and never writes back to the SO.
        /// </summary>
        public CombatMoveData BuildRuntimeMoveData(CombatMoveId moveId)
        {
            CombatFrameData[] runtimeFrames = new CombatFrameData[TotalFrames];

            for (int frameIndex = 0; frameIndex < runtimeFrames.Length; frameIndex++)
            {
                MotionBodyState body = EvaluateBodyState(frameIndex);
                CombatBox[] boxes = BuildHitBoxes(frameIndex);
                runtimeFrames[frameIndex] = new CombatFrameData
                {
                    Flags = boxes.Length > 0 ? CombatFrameFlags.Active : CombatFrameFlags.None,
                    EntityOffset = new SimVector2(
                        SimMath.FromUnity(body.EntityOffset.x),
                        SimMath.FromUnity(body.EntityOffset.y)),
                    BoundsHalfSizeOffsetX = SimMath.FromUnity(body.BoundsSizeOffset.x * 0.5f),
                    BoundsHalfSizeOffsetY = SimMath.FromUnity(body.BoundsSizeOffset.y * 0.5f),
                    Boxes = boxes
                };
            }

            return new CombatMoveData
            {
                MoveId = moveId,
                FrameRate = FrameRate,
                Loop = Loop,
                Frames = runtimeFrames
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
                MotionHitBoxTrackDefinition track = Tracks[trackIndex] as MotionHitBoxTrackDefinition;
                if (track == null)
                {
                    continue;
                }

                MotionHitBoxClipDefinition[] clips = track.Clips;
                if (clips.Length > 0)
                {
                    for (int clipIndex = 0; clipIndex < clips.Length; clipIndex++)
                    {
                        MotionHitBoxClipKey key;
                        if (!clips[clipIndex].TryEvaluate(frameIndex, TotalFrames, out key))
                        {
                            continue;
                        }

                        result = AppendBox(result, new CombatBox
                        {
                            Kind = CombatBoxKind.Hit,
                            LocalCenterX = SimMath.FromUnity(key.Center.x),
                            LocalCenterY = SimMath.FromUnity(key.Center.y),
                            HalfWidth = SimMath.FromUnity(key.Size.x * 0.5f),
                            HalfHeight = SimMath.FromUnity(key.Size.y * 0.5f),
                            Group = key.Group
                        });
                    }

                    continue;
                }

                MotionHitBoxKey[] keys = track.Keys;
                for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                {
                    MotionHitBoxKey key = keys[keyIndex].Normalized(TotalFrames);
                    if (!key.ContainsFrame(frameIndex))
                    {
                        continue;
                    }

                    result = AppendBox(result, new CombatBox
                    {
                        Kind = CombatBoxKind.Hit,
                        LocalCenterX = SimMath.FromUnity(key.Center.x),
                        LocalCenterY = SimMath.FromUnity(key.Center.y),
                        HalfWidth = SimMath.FromUnity(key.Size.x * 0.5f),
                        HalfHeight = SimMath.FromUnity(key.Size.y * 0.5f),
                        Group = key.Group
                    });
                }
            }

            return result;
        }

        private void OnValidate()
        {
            frameRate = Mathf.Max(1, frameRate);
            totalFrames = Mathf.Max(1, totalFrames);
            tracks = tracks ?? new MotionTrackDefinition[0];
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
    }

    public enum MotionTrackType
    {
        HitBox,
        Body,
        Effect,
        Sound,
        Custom
    }

    [System.Serializable]
    public abstract class MotionTrackDefinition
    {
        [SerializeField] private string trackId = "track";
        [SerializeField] private string displayName = "Track";

        public string TrackId { get { return trackId; } set { trackId = value; } }
        public string DisplayName { get { return displayName; } set { displayName = value; } }
        public abstract MotionTrackType Type { get; }
    }

    [System.Serializable]
    public sealed class MotionHitBoxTrackDefinition : MotionTrackDefinition
    {
        [SerializeField] private MotionHitBoxClipDefinition[] clips = new MotionHitBoxClipDefinition[0];
        // Legacy flat keys are kept so older assets can still be read and cooked.
        [SerializeField] private MotionHitBoxKey[] keys = new MotionHitBoxKey[0];

        public override MotionTrackType Type => MotionTrackType.HitBox;
        public MotionHitBoxClipDefinition[] Clips
        {
            get { return clips ?? new MotionHitBoxClipDefinition[0]; }
            set { clips = value ?? new MotionHitBoxClipDefinition[0]; }
        }

        public MotionHitBoxKey[] Keys { get { return keys ?? new MotionHitBoxKey[0]; } set { keys = value ?? new MotionHitBoxKey[0]; } }

        public int AddClip(string label, int startFrame, int endFrame, Vector2 center, Vector2 size)
        {
            int id = 1;
            for (int index = 0; index < Clips.Length; index++)
            {
                id = Mathf.Max(id, Clips[index].Id + 1);
            }

            MotionHitBoxClipDefinition clip = new MotionHitBoxClipDefinition
            {
                Id = id,
                Label = string.IsNullOrEmpty(label) ? "HitBox Clip " + id : label,
                StartFrame = startFrame,
                EndFrame = Mathf.Max(startFrame, endFrame)
            };
            MotionHitBoxClipKey firstKey = new MotionHitBoxClipKey
            {
                Frame = startFrame,
                Center = center,
                Size = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y))
            };
            MotionHitBoxClipKey lastKey = new MotionHitBoxClipKey
            {
                Frame = Mathf.Max(startFrame, endFrame),
                Center = center,
                Size = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y))
            };
            clip.SetKeys(startFrame == endFrame ? new[] { firstKey } : new[] { firstKey, lastKey });

            MotionHitBoxClipDefinition[] next = new MotionHitBoxClipDefinition[Clips.Length + 1];
            for (int index = 0; index < Clips.Length; index++)
            {
                next[index] = Clips[index];
            }

            next[next.Length - 1] = clip;
            clips = next;
            return id;
        }

        public void RemoveClipAt(int index)
        {
            if (index < 0 || index >= Clips.Length)
            {
                return;
            }

            MotionHitBoxClipDefinition[] next = new MotionHitBoxClipDefinition[Clips.Length - 1];
            int nextIndex = 0;
            for (int sourceIndex = 0; sourceIndex < Clips.Length; sourceIndex++)
            {
                if (sourceIndex != index)
                {
                    next[nextIndex] = Clips[sourceIndex];
                    nextIndex++;
                }
            }

            clips = next;
        }

        public int AddKey(string label, int startFrame, int endFrame, Vector2 center, Vector2 size)
        {
            int id = 1;
            for (int index = 0; index < Keys.Length; index++)
            {
                id = Mathf.Max(id, Keys[index].Id + 1);
            }

            MotionHitBoxKey[] next = new MotionHitBoxKey[Keys.Length + 1];
            for (int index = 0; index < Keys.Length; index++)
            {
                next[index] = Keys[index];
            }

            next[next.Length - 1] = new MotionHitBoxKey
            {
                Id = id,
                Label = string.IsNullOrEmpty(label) ? "HitBox " + id : label,
                StartFrame = startFrame,
                EndFrame = endFrame,
                Center = center,
                Size = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y))
            };
            keys = next;
            return id;
        }

        public void RemoveKeyAt(int index)
        {
            if (index < 0 || index >= Keys.Length)
            {
                return;
            }

            MotionHitBoxKey[] next = new MotionHitBoxKey[Keys.Length - 1];
            int nextIndex = 0;
            for (int sourceIndex = 0; sourceIndex < Keys.Length; sourceIndex++)
            {
                if (sourceIndex != index)
                {
                    next[nextIndex] = Keys[sourceIndex];
                    nextIndex++;
                }
            }

            keys = next;
        }
    }

    [System.Serializable]
    public sealed class MotionHitBoxClipDefinition
    {
        [SerializeField] private int id;
        [SerializeField] private string label = "HitBox Clip";
        [SerializeField] private int startFrame;
        [SerializeField] private int endFrame;
        [SerializeField] private MotionHitBoxClipKey[] keys = new MotionHitBoxClipKey[0];

        public int Id { get { return id; } set { id = value; } }
        public string Label { get { return label; } set { label = value; } }
        public int StartFrame { get { return startFrame; } set { startFrame = value; } }
        public int EndFrame { get { return endFrame; } set { endFrame = value; } }
        public MotionHitBoxClipKey[] Keys { get { return keys ?? new MotionHitBoxClipKey[0]; } set { keys = value ?? new MotionHitBoxClipKey[0]; } }

        public void SetKeys(MotionHitBoxClipKey[] values)
        {
            keys = values ?? new MotionHitBoxClipKey[0];
        }

        public bool TryEvaluate(int frame, int frameCount, out MotionHitBoxClipKey result)
        {
            MotionHitBoxClipDefinition normalized = Normalized(frameCount);
            if (frame < normalized.StartFrame || frame > normalized.EndFrame || normalized.Keys.Length == 0)
            {
                result = new MotionHitBoxClipKey();
                return false;
            }

            MotionHitBoxClipKey previous = normalized.Keys[0].Normalized(frameCount);
            MotionHitBoxClipKey next = previous;
            bool hasPrevious = previous.Frame <= frame;
            bool hasNext = false;
            for (int index = 0; index < normalized.Keys.Length; index++)
            {
                MotionHitBoxClipKey key = normalized.Keys[index].Normalized(frameCount);
                if (key.Frame <= frame && (!hasPrevious || key.Frame >= previous.Frame))
                {
                    previous = key;
                    hasPrevious = true;
                }
                else if (key.Frame > frame && (!hasNext || key.Frame < next.Frame))
                {
                    next = key;
                    hasNext = true;
                }
            }

            if (!hasPrevious)
            {
                previous = normalized.Keys[0].Normalized(frameCount);
            }

            if (!hasNext || next.Frame <= previous.Frame)
            {
                result = previous;
                return true;
            }

            float t = Mathf.Clamp01((frame - previous.Frame) / (float)(next.Frame - previous.Frame));
            result = new MotionHitBoxClipKey
            {
                Frame = frame,
                Center = Vector2.Lerp(previous.Center, next.Center, t),
                Size = new Vector2(
                    Mathf.Abs(Mathf.Lerp(previous.Size.x, next.Size.x, t)),
                    Mathf.Abs(Mathf.Lerp(previous.Size.y, next.Size.y, t))),
                Group = previous.Group
            };
            return true;
        }

        public MotionHitBoxClipDefinition Normalized(int frameCount)
        {
            MotionHitBoxClipDefinition result = this;
            int maxFrame = Mathf.Max(0, frameCount - 1);
            result.StartFrame = Mathf.Clamp(result.StartFrame, 0, maxFrame);
            result.EndFrame = Mathf.Clamp(result.EndFrame, result.StartFrame, maxFrame);
            return result;
        }
    }

    [System.Serializable]
    public struct MotionHitBoxClipKey
    {
        public int Frame;
        public Vector2 Center;
        public Vector2 Size;
        public int Group;

        public MotionHitBoxClipKey Normalized(int frameCount)
        {
            MotionHitBoxClipKey result = this;
            result.Frame = Mathf.Clamp(result.Frame, 0, Mathf.Max(0, frameCount - 1));
            result.Size = new Vector2(Mathf.Abs(result.Size.x), Mathf.Abs(result.Size.y));
            return result;
        }
    }

    [System.Serializable]
    public struct MotionHitBoxKey
    {
        public int Id;
        public string Label;
        public int StartFrame;
        public int EndFrame;
        public Vector2 Center;
        public Vector2 Size;
        public int Group;

        public bool ContainsFrame(int frame)
        {
            return frame >= StartFrame && frame <= EndFrame;
        }

        public MotionHitBoxKey Normalized(int frameCount)
        {
            MotionHitBoxKey result = this;
            int maxFrame = Mathf.Max(0, frameCount - 1);
            result.StartFrame = Mathf.Clamp(result.StartFrame, 0, maxFrame);
            result.EndFrame = Mathf.Clamp(result.EndFrame, result.StartFrame, maxFrame);
            result.Size = new Vector2(Mathf.Abs(result.Size.x), Mathf.Abs(result.Size.y));
            return result;
        }
    }

    [System.Serializable]
    public sealed class MotionBodyTrackDefinition : MotionTrackDefinition
    {
        [SerializeField] private bool active = true;
        [SerializeField] private bool lerp;
        [SerializeField] private MotionBodyClipDefinition[] clips = new MotionBodyClipDefinition[0];
        // Legacy flat keys are kept so older assets can still be read and cooked.
        [SerializeField] private MotionBodyKey[] keys = new MotionBodyKey[0];

        public override MotionTrackType Type => MotionTrackType.Body;
        public bool Active { get { return active; } set { active = value; } }
        public bool Lerp { get { return lerp; } set { lerp = value; } }
        public MotionBodyClipDefinition[] Clips
        {
            get { return clips ?? new MotionBodyClipDefinition[0]; }
            set { clips = value ?? new MotionBodyClipDefinition[0]; }
        }

        public MotionBodyKey[] Keys { get { return keys ?? new MotionBodyKey[0]; } set { keys = value ?? new MotionBodyKey[0]; } }

        public int AddClip(string label, int startFrame, int endFrame)
        {
            int id = 1;
            for (int index = 0; index < Clips.Length; index++)
            {
                id = Mathf.Max(id, Clips[index].Id + 1);
            }

            int safeEndFrame = Mathf.Max(startFrame, endFrame);
            MotionBodyClipDefinition clip = new MotionBodyClipDefinition
            {
                Id = id,
                Label = string.IsNullOrEmpty(label) ? "Body Clip " + id : label,
                StartFrame = startFrame,
                EndFrame = safeEndFrame
            };
            MotionBodyClipKey firstKey = new MotionBodyClipKey { Frame = startFrame };
            MotionBodyClipKey lastKey = new MotionBodyClipKey { Frame = safeEndFrame };
            clip.SetKeys(startFrame == safeEndFrame ? new[] { firstKey } : new[] { firstKey, lastKey });

            MotionBodyClipDefinition[] next = new MotionBodyClipDefinition[Clips.Length + 1];
            for (int index = 0; index < Clips.Length; index++)
            {
                next[index] = Clips[index];
            }

            next[next.Length - 1] = clip;
            clips = next;
            return id;
        }

        public void RemoveClipAt(int index)
        {
            if (index < 0 || index >= Clips.Length)
            {
                return;
            }

            MotionBodyClipDefinition[] next = new MotionBodyClipDefinition[Clips.Length - 1];
            int nextIndex = 0;
            for (int sourceIndex = 0; sourceIndex < Clips.Length; sourceIndex++)
            {
                if (sourceIndex != index)
                {
                    next[nextIndex] = Clips[sourceIndex];
                    nextIndex++;
                }
            }

            clips = next;
        }

        public int AddKey(int startFrame, int endFrame)
        {
            MotionBodyKey[] next = new MotionBodyKey[Keys.Length + 1];
            for (int index = 0; index < Keys.Length; index++)
            {
                next[index] = Keys[index];
            }

            next[next.Length - 1] = new MotionBodyKey
            {
                StartFrame = startFrame,
                EndFrame = endFrame,
                BoundsSizeOffset = Vector2.zero
            };
            keys = next;
            return next.Length - 1;
        }

        public void RemoveKeyAt(int index)
        {
            if (index < 0 || index >= Keys.Length)
            {
                return;
            }

            MotionBodyKey[] next = new MotionBodyKey[Keys.Length - 1];
            int nextIndex = 0;
            for (int sourceIndex = 0; sourceIndex < Keys.Length; sourceIndex++)
            {
                if (sourceIndex != index)
                {
                    next[nextIndex] = Keys[sourceIndex];
                    nextIndex++;
                }
            }

            keys = next;
        }

        public MotionBodyState Evaluate(int frameIndex, int frameCount, MotionBodyState fallback)
        {
            if (!Active)
            {
                return fallback;
            }

            MotionBodyClipDefinition[] clipDefinitions = Clips;
            if (clipDefinitions.Length > 0)
            {
                MotionBodyState clipResult = fallback;
                bool hasClipResult = false;
                for (int index = 0; index < clipDefinitions.Length; index++)
                {
                    MotionBodyState value;
                    if (clipDefinitions[index].TryEvaluate(frameIndex, frameCount, Lerp, out value))
                    {
                        clipResult = value;
                        hasClipResult = true;
                    }
                }

                return hasClipResult ? clipResult : fallback;
            }

            MotionBodyState result = fallback;
            MotionBodyKey previousKey = new MotionBodyKey();
            MotionBodyKey nextKey = new MotionBodyKey();
            bool hasPrevious = false;
            bool hasNext = false;
            for (int index = 0; index < Keys.Length; index++)
            {
                MotionBodyKey key = Keys[index].Normalized(frameCount);
                if (key.StartFrame <= frameIndex &&
                    (!hasPrevious || key.StartFrame >= previousKey.StartFrame))
                {
                    previousKey = key;
                    hasPrevious = true;
                }
                else if (key.StartFrame > frameIndex &&
                         (!hasNext || key.StartFrame < nextKey.StartFrame))
                {
                    nextKey = key;
                    hasNext = true;
                }
            }

            if (!hasPrevious)
            {
                return result;
            }

            MotionBodyState previousState = previousKey.ToState();
            if (!Lerp || !hasNext || nextKey.StartFrame <= previousKey.StartFrame)
            {
                return previousState;
            }

            float t = Mathf.Clamp01(
                (frameIndex - previousKey.StartFrame) /
                (float)(nextKey.StartFrame - previousKey.StartFrame));
            MotionBodyState nextState = nextKey.ToState();
            return new MotionBodyState
            {
                EntityOffset = Vector2.Lerp(previousState.EntityOffset, nextState.EntityOffset, t),
                BoundsSizeOffset = Vector2.Lerp(previousState.BoundsSizeOffset, nextState.BoundsSizeOffset, t)
            };
        }
    }

    [System.Serializable]
    public sealed class MotionBodyClipDefinition
    {
        [SerializeField] private int id;
        [SerializeField] private string label = "Body Clip";
        [SerializeField] private int startFrame;
        [SerializeField] private int endFrame;
        [SerializeField] private MotionBodyClipKey[] keys = new MotionBodyClipKey[0];

        public int Id { get { return id; } set { id = value; } }
        public string Label { get { return label; } set { label = value; } }
        public int StartFrame { get { return startFrame; } set { startFrame = value; } }
        public int EndFrame { get { return endFrame; } set { endFrame = value; } }
        public MotionBodyClipKey[] Keys { get { return keys ?? new MotionBodyClipKey[0]; } set { keys = value ?? new MotionBodyClipKey[0]; } }

        public void SetKeys(MotionBodyClipKey[] values)
        {
            keys = values ?? new MotionBodyClipKey[0];
        }

        public bool TryEvaluate(int frame, int frameCount, bool shouldLerp, out MotionBodyState result)
        {
            MotionBodyClipDefinition normalized = Normalized(frameCount);
            if (frame < normalized.StartFrame || frame > normalized.EndFrame || normalized.Keys.Length == 0)
            {
                result = MotionBodyState.Default;
                return false;
            }

            MotionBodyClipKey previous = normalized.Keys[0].Normalized(frameCount);
            MotionBodyClipKey next = previous;
            bool hasPrevious = previous.Frame <= frame;
            bool hasNext = false;
            for (int index = 0; index < normalized.Keys.Length; index++)
            {
                MotionBodyClipKey key = normalized.Keys[index].Normalized(frameCount);
                if (key.Frame <= frame && (!hasPrevious || key.Frame >= previous.Frame))
                {
                    previous = key;
                    hasPrevious = true;
                }
                else if (key.Frame > frame && (!hasNext || key.Frame < next.Frame))
                {
                    next = key;
                    hasNext = true;
                }
            }

            if (!hasPrevious)
            {
                previous = normalized.Keys[0].Normalized(frameCount);
            }

            MotionBodyState previousState = previous.ToState();
            if (!shouldLerp || !hasNext || next.Frame <= previous.Frame)
            {
                result = previousState;
                return true;
            }

            float t = Mathf.Clamp01((frame - previous.Frame) / (float)(next.Frame - previous.Frame));
            MotionBodyState nextState = next.ToState();
            result = new MotionBodyState
            {
                EntityOffset = Vector2.Lerp(previousState.EntityOffset, nextState.EntityOffset, t),
                BoundsSizeOffset = Vector2.Lerp(previousState.BoundsSizeOffset, nextState.BoundsSizeOffset, t)
            };
            return true;
        }

        public MotionBodyClipDefinition Normalized(int frameCount)
        {
            MotionBodyClipDefinition result = this;
            int maxFrame = Mathf.Max(0, frameCount - 1);
            result.StartFrame = Mathf.Clamp(result.StartFrame, 0, maxFrame);
            result.EndFrame = Mathf.Clamp(result.EndFrame, result.StartFrame, maxFrame);
            return result;
        }
    }

    [System.Serializable]
    public struct MotionBodyClipKey
    {
        public int Frame;
        public Vector2 EntityOffset;
        public Vector2 BoundsSizeOffset;

        public MotionBodyClipKey Normalized(int frameCount)
        {
            MotionBodyClipKey result = this;
            result.Frame = Mathf.Clamp(result.Frame, 0, Mathf.Max(0, frameCount - 1));
            return result;
        }

        public MotionBodyState ToState()
        {
            return new MotionBodyState
            {
                EntityOffset = EntityOffset,
                BoundsSizeOffset = BoundsSizeOffset
            };
        }
    }

    [System.Serializable]
    public struct MotionBodyKey
    {
        public int StartFrame;
        public int EndFrame;
        public Vector2 EntityOffset;
        public Vector2 BoundsSizeOffset;

        public bool ContainsFrame(int frame)
        {
            return frame >= StartFrame && frame <= EndFrame;
        }

        public MotionBodyKey Normalized(int frameCount)
        {
            MotionBodyKey result = this;
            int maxFrame = Mathf.Max(0, frameCount - 1);
            result.StartFrame = Mathf.Clamp(result.StartFrame, 0, maxFrame);
            result.EndFrame = Mathf.Clamp(result.EndFrame, result.StartFrame, maxFrame);
            return result;
        }

        public MotionBodyState ToState()
        {
            return new MotionBodyState
            {
                EntityOffset = EntityOffset,
                BoundsSizeOffset = BoundsSizeOffset
            };
        }
    }

    [System.Serializable]
    public struct MotionBodyState
    {
        public Vector2 EntityOffset;
        public Vector2 BoundsSizeOffset;

        public static MotionBodyState Default => new MotionBodyState
        {
            BoundsSizeOffset = Vector2.zero
        };
    }

    [System.Serializable]
    public sealed class MotionEffectTrackDefinition : MotionTrackDefinition
    {
        [SerializeField] private string effectId;
        [SerializeField] private int startFrame;
        [SerializeField] private int endFrame;

        public override MotionTrackType Type => MotionTrackType.Effect;
        public string EffectId { get { return effectId; } set { effectId = value; } }
        public int StartFrame { get { return startFrame; } set { startFrame = value; } }
        public int EndFrame { get { return endFrame; } set { endFrame = value; } }
    }
}
