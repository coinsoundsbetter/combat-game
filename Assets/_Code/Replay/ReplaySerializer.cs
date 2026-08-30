/*
using System;
using System.IO;
using _Code.Simulation;
using _Src.Test;

namespace _Code.Replay {
    /// <summary>
    /// 回放二进制格式。输入使用紧凑的 MoveX(sbyte) + Attack(byte) 编码。
    /// </summary>
    public static class ReplaySerializer {
        private const string Magic = "FGRP";
        private const int MaxPlayerCount = 8;
        private const int MaxFrameCount = 10 * 60 * 60 * 24;

        public static void Save(string path, MatchReplay replay) {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Replay path is required.", nameof(path));

            Validate(replay);

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream)) {
                writer.Write(Magic);
                writer.Write(replay.Header.FormatVersion);
                writer.Write(replay.Header.LogicVersion ?? string.Empty);
                writer.Write(replay.Header.TickRate);
                writer.Write(replay.Header.PlayerCount);

                for (var i = 0; i < replay.Header.PlayerCount; i++) {
                    writer.Write(replay.Header.InitialStates[i].PosX);
                    writer.Write(replay.Header.InitialStates[i].AttackCount);
                }

                writer.Write(replay.FinalFrame);
                writer.Write(replay.IsVerified);
                writer.Write(replay.Frames.Count);
                for (var frameIndex = 0; frameIndex < replay.Frames.Count; frameIndex++) {
                    var frame = replay.Frames[frameIndex];
                    writer.Write(frame.Frame);
                    for (var playerIndex = 0; playerIndex < frame.Inputs.Length; playerIndex++)
                        WriteInput(writer, frame.Inputs[playerIndex]);
                }

                writer.Write(replay.Checkpoints.Count);
                for (var i = 0; i < replay.Checkpoints.Count; i++) {
                    writer.Write(replay.Checkpoints[i].Frame);
                    writer.Write(replay.Checkpoints[i].Checksum);
                }
            }
        }

        public static MatchReplay Load(string path) {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Replay path is required.", nameof(path));

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream)) {
                if (reader.ReadString() != Magic)
                    throw new InvalidDataException("This file is not a fighter replay.");

                var header = new ReplayHeader {
                    FormatVersion = reader.ReadInt32(),
                    LogicVersion = reader.ReadString(),
                    TickRate = reader.ReadInt32(),
                    PlayerCount = reader.ReadInt32(),
                };

                if (header.FormatVersion != ReplayHeader.CurrentFormatVersion)
                    throw new InvalidDataException(
                        $"Unsupported replay format version: {header.FormatVersion}.");
                if (header.PlayerCount <= 0 || header.PlayerCount > MaxPlayerCount)
                    throw new InvalidDataException("Invalid replay player count.");

                header.InitialStates = new FighterState[header.PlayerCount];
                for (var i = 0; i < header.PlayerCount; i++) {
                    header.InitialStates[i].PosX = reader.ReadInt32();
                    header.InitialStates[i].AttackCount = reader.ReadInt32();
                }

                var replay = new MatchReplay {
                    Header = header,
                    FinalFrame = reader.ReadInt32(),
                    IsVerified = reader.ReadBoolean(),
                };

                var frameCount = reader.ReadInt32();
                if (frameCount < 0 || frameCount > MaxFrameCount)
                    throw new InvalidDataException("Invalid replay frame count.");

                for (var frameIndex = 0; frameIndex < frameCount; frameIndex++) {
                    var frame = new ReplayFrame {
                        Frame = reader.ReadInt32(),
                        Inputs = new FighterInput[header.PlayerCount],
                    };
                    for (var playerIndex = 0; playerIndex < header.PlayerCount; playerIndex++)
                        frame.Inputs[playerIndex] = ReadInput(reader);
                    replay.Frames.Add(frame);
                }

                var checkpointCount = reader.ReadInt32();
                if (checkpointCount < 0 || checkpointCount > frameCount)
                    throw new InvalidDataException("Invalid replay checkpoint count.");

                for (var i = 0; i < checkpointCount; i++) {
                    replay.Checkpoints.Add(new ReplayCheckpoint {
                        Frame = reader.ReadInt32(),
                        Checksum = reader.ReadInt32(),
                    });
                }

                Validate(replay);
                return replay;
            }
        }

        private static void WriteInput(BinaryWriter writer, FighterInput input) {
            if (input.MoveX < -1 || input.MoveX > 1)
                throw new InvalidDataException("Replay input MoveX must be between -1 and 1.");

            writer.Write((sbyte)input.MoveX);
            writer.Write(input.Attack);
        }

        private static FighterInput ReadInput(BinaryReader reader) {
            var moveX = reader.ReadSByte();
            if (moveX < -1 || moveX > 1)
                throw new InvalidDataException("Replay input MoveX is invalid.");

            return new FighterInput {
                MoveX = moveX,
                Attack = reader.ReadBoolean(),
            };
        }

        private static void Validate(MatchReplay replay) {
            if (replay == null || replay.Header == null)
                throw new ArgumentNullException(nameof(replay));
            if (replay.Header.FormatVersion != ReplayHeader.CurrentFormatVersion)
                throw new InvalidDataException("Replay format version is invalid.");
            if (replay.Header.TickRate <= 0 ||
                replay.Header.PlayerCount <= 0 ||
                replay.Header.PlayerCount > MaxPlayerCount ||
                replay.Header.InitialStates == null ||
                replay.Header.InitialStates.Length != replay.Header.PlayerCount)
                throw new InvalidDataException("Replay header is invalid.");
            if (replay.FinalFrame != replay.Frames.Count - 1)
                throw new InvalidDataException("Replay final frame is invalid.");

            for (var i = 0; i < replay.Frames.Count; i++) {
                var frame = replay.Frames[i];
                if (frame == null || frame.Frame != i || frame.Inputs == null ||
                    frame.Inputs.Length != replay.Header.PlayerCount)
                    throw new InvalidDataException("Replay frame sequence is invalid.");

                for (var playerIndex = 0; playerIndex < frame.Inputs.Length; playerIndex++) {
                    if (frame.Inputs[playerIndex].MoveX < -1 ||
                        frame.Inputs[playerIndex].MoveX > 1)
                        throw new InvalidDataException("Replay input MoveX is invalid.");
                }
            }

            for (var i = 0; i < replay.Checkpoints.Count; i++) {
                if (replay.Checkpoints[i].Frame < 0 ||
                    replay.Checkpoints[i].Frame > replay.FinalFrame)
                    throw new InvalidDataException("Replay checkpoint frame is invalid.");
            }
        }
    }
}
*/
