// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using System.Text;

namespace VoiceAgentWavSample;

/// <summary>Minimal reader/writer for 16-bit signed little-endian mono PCM WAV files.</summary>
internal static class Wav
{
    /// <summary>Reads the PCM samples and sample rate from a WAV file.</summary>
    public static (byte[] Pcm, int SampleRateHertz) ReadPcm(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" ||
            Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
        {
            throw new InvalidDataException($"'{path}' is not a RIFF/WAVE file.");
        }

        int? sampleRate = null;
        bool fmtValidated = false;
        int offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var chunkId = Encoding.ASCII.GetString(bytes, offset, 4);
            // Chunk sizes are unsigned; reading them as signed Int32 would let a large value look negative
            // and stall or rewind the loop. Read unsigned and bounds-check against the remaining bytes.
            long chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            int chunkData = offset + 8;
            if (chunkData + chunkSize > bytes.Length)
            {
                throw new InvalidDataException(
                    $"'{path}' declares a '{chunkId.TrimEnd()}' chunk of {chunkSize} bytes that overruns the file.");
            }

            if (chunkId == "fmt ")
            {
                if (chunkSize < 16)
                {
                    throw new InvalidDataException($"'{path}' has a truncated fmt chunk.");
                }

                short audioFormat = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(chunkData, 2));
                short channels = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(chunkData + 2, 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(chunkData + 4, 4));
                short bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(chunkData + 14, 2));

                if (audioFormat != 1 || channels != 1 || bitsPerSample != 16)
                {
                    throw new InvalidDataException(
                        $"'{path}' must be 16-bit signed little-endian mono PCM (format=1, channels=1, bits=16), " +
                        $"but was format={audioFormat}, channels={channels}, bits={bitsPerSample}.");
                }

                fmtValidated = true;
            }
            else if (chunkId == "data")
            {
                if (!fmtValidated || sampleRate is null)
                {
                    throw new InvalidDataException($"'{path}' has a data chunk before a valid fmt chunk.");
                }

                return (bytes.AsSpan(chunkData, (int)chunkSize).ToArray(), sampleRate.Value);
            }

            offset = chunkData + (int)chunkSize + (int)(chunkSize % 2); // chunks are word-aligned
        }

        throw new InvalidDataException($"'{path}' has no data chunk.");
    }

    /// <summary>Writes PCM samples to a WAV file with a standard 16-bit mono header.</summary>
    public static void WritePcm(string path, ReadOnlySpan<byte> pcm, int sampleRateHertz)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        int byteRate = sampleRateHertz * channels * bitsPerSample / 8;
        short blockAlign = channels * bitsPerSample / 8;

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + pcm.Length);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);                 // PCM fmt chunk size
        writer.Write((short)1);           // audio format: PCM
        writer.Write(channels);
        writer.Write(sampleRateHertz);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);

        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(pcm.Length);
        writer.Write(pcm);
    }
}
