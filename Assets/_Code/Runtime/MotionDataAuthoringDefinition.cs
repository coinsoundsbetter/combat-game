using GLMFighter.Core;
using UnityEngine;

namespace GLMFighter.Runtime
{
    /// <summary>
    /// Editor-facing source asset for a motion. This asset is never consumed
    /// by the combat simulation directly; use the editor Cook command to
    /// generate the sibling MotionDataDefinition *_Cooked asset.
    /// </summary>
    [CreateAssetMenu(menuName = "GLM Fighter/Motion Data Authoring", fileName = "MotionData")]
    public sealed class MotionDataAuthoringDefinition : ScriptableObject
    {
        [SerializeField] private string motionId = "motion";
        [SerializeField] private int frameRate = BattleSimulation.FramesPerSecond;
        [SerializeField] private int totalFrames = 1;
        [SerializeField] private bool loop;
        [SerializeField] private AnimationClip previewClip;
        [SerializeField] private FighterRoleDefinition previewRole;
        [SerializeField] private MotionDataDefinition cookedMotionData;
        [SerializeReference] private MotionTrackDefinition[] tracks = new MotionTrackDefinition[0];

        public string MotionId => motionId;
        public int FrameRate => frameRate <= 0 ? BattleSimulation.FramesPerSecond : frameRate;
        public int TotalFrames => Mathf.Max(1, totalFrames);
        public bool Loop => loop;
        public float DurationSeconds => TotalFrames / (float)FrameRate;
        public AnimationClip PreviewClip => previewClip;
        public FighterRoleDefinition PreviewRole => previewRole;
        public MotionDataDefinition CookedMotionData => cookedMotionData;
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

        public void SetPreviewClip(AnimationClip value)
        {
            previewClip = value;
        }

        public void SetPreviewRole(FighterRoleDefinition value)
        {
            previewRole = value;
        }

        public void SetCookedMotionData(MotionDataDefinition value)
        {
            cookedMotionData = value;
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

        private void OnValidate()
        {
            frameRate = Mathf.Max(1, frameRate);
            totalFrames = Mathf.Max(1, totalFrames);
            tracks = tracks ?? new MotionTrackDefinition[0];
        }
    }
}
