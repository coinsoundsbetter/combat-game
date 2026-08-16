using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using GLMFighter.Runtime;

namespace GLMFighter.EditorTools
{
    /// <summary>
    /// Produces the runtime MotionDataDefinition beside an authoring asset.
    /// The track data is cloned so the two assets never share serialized
    /// managed-reference objects.
    /// </summary>
    public static class MotionDataCooker
    {
        [MenuItem("GLM Fighter/Motion Data/Cook Selected Authoring Asset")]
        private static void CookSelected()
        {
            Cook(Selection.activeObject as MotionDataAuthoringDefinition);
        }

        [MenuItem("GLM Fighter/Motion Data/Cook Selected Authoring Asset", true)]
        private static bool ValidateCookSelected()
        {
            return Selection.activeObject is MotionDataAuthoringDefinition;
        }

        public static MotionDataDefinition Cook(MotionDataAuthoringDefinition source)
        {
            if (source == null)
            {
                return null;
            }

            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath))
            {
                Debug.LogError("The MotionData authoring asset must be saved before it can be cooked.", source);
                return null;
            }

            string directory = Path.GetDirectoryName(sourcePath);
            string fileName = Path.GetFileNameWithoutExtension(sourcePath);
            string cookedPath = Path.Combine(directory ?? string.Empty, fileName + "_Cooked.asset").Replace('\\', '/');
            MotionDataDefinition cooked = AssetDatabase.LoadAssetAtPath<MotionDataDefinition>(cookedPath);

            if (cooked == null)
            {
                Object existingAsset = AssetDatabase.LoadMainAssetAtPath(cookedPath);
                if (existingAsset != null)
                {
                    Debug.LogError(
                        "Cannot cook MotionData because the output path is already used by another asset: " + cookedPath,
                        existingAsset);
                    return null;
                }

                cooked = ScriptableObject.CreateInstance<MotionDataDefinition>();
                cooked.name = fileName + "_Cooked";
                AssetDatabase.CreateAsset(cooked, cookedPath);
            }
            else
            {
                Undo.RegisterCompleteObjectUndo(cooked, "Cook MotionData");
            }

            cooked.Configure(source.MotionId, source.FrameRate, source.TotalFrames, source.Loop);
            cooked.SetTracks(CloneTracks(source.Tracks));
            source.SetCookedMotionData(cooked);

            EditorUtility.SetDirty(source);
            EditorUtility.SetDirty(cooked);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return cooked;
        }

        private static MotionTrackDefinition[] CloneTracks(MotionTrackDefinition[] source)
        {
            List<MotionTrackDefinition> result = new List<MotionTrackDefinition>();
            if (source == null)
            {
                return result.ToArray();
            }

            for (int index = 0; index < source.Length; index++)
            {
                MotionTrackDefinition track = source[index];
                if (track == null)
                {
                    continue;
                }

                MotionHitBoxTrackDefinition hitBox = track as MotionHitBoxTrackDefinition;
                if (hitBox != null)
                {
                    MotionHitBoxTrackDefinition copy = new MotionHitBoxTrackDefinition
                    {
                        TrackId = hitBox.TrackId,
                        DisplayName = hitBox.DisplayName,
                        Clips = CopyHitBoxClips(hitBox.Clips),
                        Keys = CopyHitBoxKeys(hitBox.Keys)
                    };
                    result.Add(copy);
                    continue;
                }

                MotionBodyTrackDefinition body = track as MotionBodyTrackDefinition;
                if (body != null)
                {
                    MotionBodyTrackDefinition copy = new MotionBodyTrackDefinition
                    {
                        TrackId = body.TrackId,
                        DisplayName = body.DisplayName,
                        Active = body.Active,
                        Lerp = body.Lerp,
                        Clips = CopyBodyClips(body.Clips),
                        Keys = CopyBodyKeys(body.Keys)
                    };
                    result.Add(copy);
                    continue;
                }

                MotionEffectTrackDefinition effect = track as MotionEffectTrackDefinition;
                if (effect != null)
                {
                    result.Add(new MotionEffectTrackDefinition
                    {
                        TrackId = effect.TrackId,
                        DisplayName = effect.DisplayName,
                        EffectId = effect.EffectId,
                        StartFrame = effect.StartFrame,
                        EndFrame = effect.EndFrame
                    });
                    continue;
                }

                Debug.LogWarning("Skipped unsupported MotionData track type: " + track.GetType().Name);
            }

            return result.ToArray();
        }

        private static MotionHitBoxKey[] CopyHitBoxKeys(MotionHitBoxKey[] source)
        {
            MotionHitBoxKey[] result = new MotionHitBoxKey[source == null ? 0 : source.Length];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = source[index];
            }

            return result;
        }

        private static MotionHitBoxClipDefinition[] CopyHitBoxClips(MotionHitBoxClipDefinition[] source)
        {
            MotionHitBoxClipDefinition[] result = new MotionHitBoxClipDefinition[source == null ? 0 : source.Length];
            for (int index = 0; index < result.Length; index++)
            {
                MotionHitBoxClipDefinition clip = source[index];
                MotionHitBoxClipDefinition copy = new MotionHitBoxClipDefinition
                {
                    Id = clip.Id,
                    Label = clip.Label,
                    StartFrame = clip.StartFrame,
                    EndFrame = clip.EndFrame
                };
                copy.SetKeys(CopyHitBoxClipKeys(clip.Keys));
                result[index] = copy;
            }

            return result;
        }

        private static MotionHitBoxClipKey[] CopyHitBoxClipKeys(MotionHitBoxClipKey[] source)
        {
            MotionHitBoxClipKey[] result = new MotionHitBoxClipKey[source == null ? 0 : source.Length];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = source[index];
            }

            return result;
        }

        private static MotionBodyKey[] CopyBodyKeys(MotionBodyKey[] source)
        {
            MotionBodyKey[] result = new MotionBodyKey[source == null ? 0 : source.Length];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = source[index];
            }

            return result;
        }

        private static MotionBodyClipDefinition[] CopyBodyClips(MotionBodyClipDefinition[] source)
        {
            MotionBodyClipDefinition[] result = new MotionBodyClipDefinition[source == null ? 0 : source.Length];
            for (int index = 0; index < result.Length; index++)
            {
                MotionBodyClipDefinition clip = source[index];
                MotionBodyClipDefinition copy = new MotionBodyClipDefinition
                {
                    Id = clip.Id,
                    Label = clip.Label,
                    StartFrame = clip.StartFrame,
                    EndFrame = clip.EndFrame
                };
                copy.SetKeys(CopyBodyClipKeys(clip.Keys));
                result[index] = copy;
            }

            return result;
        }

        private static MotionBodyClipKey[] CopyBodyClipKeys(MotionBodyClipKey[] source)
        {
            MotionBodyClipKey[] result = new MotionBodyClipKey[source == null ? 0 : source.Length];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = source[index];
            }

            return result;
        }
    }
}
