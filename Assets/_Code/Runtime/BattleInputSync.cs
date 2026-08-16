using System;
using System.Collections.Generic;
using GLMFighter.Core;
using GLMFighter.Network;

namespace GLMFighter.Runtime
{
    /// <summary>
    /// Owns delayed local input, remote input history, redundancy, and the
    /// decision of whether a network simulation frame can advance.
    /// </summary>
    public sealed class BattleInputSync
    {
        private readonly Dictionary<int, FighterInput> _localInputs = new Dictionary<int, FighterInput>();
        private readonly Dictionary<int, FighterInput> _remoteInputs = new Dictionary<int, FighterInput>();
        private readonly List<InputFrameData> _sendBuffer = new List<InputFrameData>(16);

        public BattleInputSync(int inputDelayFrames, int inputRedundancyFrames)
        {
            InputDelayFrames = inputDelayFrames;
            InputRedundancyFrames = inputRedundancyFrames;
        }

        public int InputDelayFrames { get; }
        public int InputRedundancyFrames { get; }
        public int LocalLatestInputFrame { get; private set; } = -1;
        public int RemoteLatestInputFrame { get; private set; } = -1;
        public bool WaitingForRemoteInput { get; private set; }

        public void Reset()
        {
            _localInputs.Clear();
            _remoteInputs.Clear();
            _sendBuffer.Clear();
            LocalLatestInputFrame = -1;
            RemoteLatestInputFrame = -1;
            WaitingForRemoteInput = false;
        }

        public void SeedNeutralFrames()
        {
            for (int frame = 0; frame < InputDelayFrames; frame++)
            {
                _localInputs[frame] = FighterInput.Neutral;
                _remoteInputs[frame] = FighterInput.Neutral;
            }
        }

        public int CaptureLocalInput(
            int simulationFrame,
            FighterInput input,
            Action<IList<InputFrameData>> send)
        {
            int targetInputFrame = simulationFrame + InputDelayFrames;
            _localInputs[targetInputFrame] = input;
            LocalLatestInputFrame = targetInputFrame;

            _sendBuffer.Clear();
            int firstFrame = targetInputFrame - InputRedundancyFrames + 1;

            for (int frame = firstFrame; frame <= targetInputFrame; frame++)
            {
                FighterInput bufferedInput;
                if (_localInputs.TryGetValue(frame, out bufferedInput))
                {
                    _sendBuffer.Add(new InputFrameData(frame, bufferedInput));
                }
            }

            if (send != null)
            {
                send(_sendBuffer);
            }

            return targetInputFrame;
        }

        public bool TryGetInputsForFrame(
            int frame,
            int assignedPlayerIndex,
            out FighterInput playerOneInput,
            out FighterInput playerTwoInput)
        {
            FighterInput localInput;
            FighterInput remoteInput;
            bool hasLocalInput = _localInputs.TryGetValue(frame, out localInput);
            bool hasRemoteInput = _remoteInputs.TryGetValue(frame, out remoteInput);

            if (!hasLocalInput || !hasRemoteInput)
            {
                WaitingForRemoteInput = !hasRemoteInput;
                playerOneInput = FighterInput.Neutral;
                playerTwoInput = FighterInput.Neutral;
                return false;
            }

            WaitingForRemoteInput = false;
            playerOneInput = assignedPlayerIndex == 0 ? localInput : remoteInput;
            playerTwoInput = assignedPlayerIndex == 1 ? localInput : remoteInput;
            return true;
        }

        public void StoreRemoteInputBundle(InputFrameData[] inputs)
        {
            if (inputs == null)
            {
                return;
            }

            for (int index = 0; index < inputs.Length; index++)
            {
                StoreRemoteInput(inputs[index].Frame, inputs[index].Input);
            }
        }

        public void StoreRemoteInput(int frame, FighterInput input)
        {
            _remoteInputs[frame] = input;

            if (frame > RemoteLatestInputFrame)
            {
                RemoteLatestInputFrame = frame;
            }
        }

        public void Prune(int beforeFrame)
        {
            RemoveBefore(_localInputs, beforeFrame);
            RemoveBefore(_remoteInputs, beforeFrame);
        }

        private static void RemoveBefore(Dictionary<int, FighterInput> inputs, int beforeFrame)
        {
            List<int> framesToRemove = null;

            foreach (int frame in inputs.Keys)
            {
                if (frame < beforeFrame)
                {
                    if (framesToRemove == null)
                    {
                        framesToRemove = new List<int>();
                    }

                    framesToRemove.Add(frame);
                }
            }

            if (framesToRemove == null)
            {
                return;
            }

            for (int index = 0; index < framesToRemove.Count; index++)
            {
                inputs.Remove(framesToRemove[index]);
            }
        }
    }
}
