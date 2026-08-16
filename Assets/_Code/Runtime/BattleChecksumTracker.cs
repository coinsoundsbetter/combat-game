using System;
using System.Collections.Generic;

namespace GLMFighter.Runtime
{
    /// <summary>
    /// Tracks periodic local/remote checksums and reports the first mismatch.
    /// It detects desync; it does not attempt to correct it.
    /// </summary>
    public sealed class BattleChecksumTracker
    {
        private readonly Dictionary<int, int> _localChecksums = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _remoteChecksums = new Dictionary<int, int>();

        public int LastChecksumFrame { get; private set; } = -1;
        public int LastLocalChecksum { get; private set; }
        public int LastRemoteChecksum { get; private set; }
        public int DesyncFrame { get; private set; } = -1;

        public void Reset()
        {
            _localChecksums.Clear();
            _remoteChecksums.Clear();
            LastChecksumFrame = -1;
            LastLocalChecksum = 0;
            LastRemoteChecksum = 0;
            DesyncFrame = -1;
        }

        public bool ShouldSend(int frame, int interval)
        {
            return interval > 0 && frame != 0 && frame % interval == 0;
        }

        public void StoreLocal(int frame, int checksum, Action<int, int> send)
        {
            _localChecksums[frame] = checksum;
            LastChecksumFrame = frame;
            LastLocalChecksum = checksum;

            if (send != null)
            {
                send(frame, checksum);
            }

            CompareIfReady(frame);
        }

        public void StoreRemote(int frame, int checksum)
        {
            _remoteChecksums[frame] = checksum;
            LastRemoteChecksum = checksum;
            CompareIfReady(frame);
        }

        public void Prune(int beforeFrame)
        {
            RemoveBefore(_localChecksums, beforeFrame);
            RemoveBefore(_remoteChecksums, beforeFrame);
        }

        private void CompareIfReady(int frame)
        {
            int localChecksum;
            int remoteChecksum;

            if (!_localChecksums.TryGetValue(frame, out localChecksum) ||
                !_remoteChecksums.TryGetValue(frame, out remoteChecksum))
            {
                return;
            }

            if (localChecksum != remoteChecksum && DesyncFrame < 0)
            {
                DesyncFrame = frame;
            }
        }

        private static void RemoveBefore(Dictionary<int, int> values, int beforeFrame)
        {
            List<int> framesToRemove = null;

            foreach (int frame in values.Keys)
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
                values.Remove(framesToRemove[index]);
            }
        }
    }
}
