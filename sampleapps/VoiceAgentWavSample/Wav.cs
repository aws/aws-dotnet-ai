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

        int sampleRate = 16000;
        int offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var chunkId = Encoding.ASCII.GetString(bytes, offset, 4);
            int chunkSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            int chunkData = offset + 8;

            if (chunkId == "fmt ")
            {
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(chunkData + 4, 4));
            }
            else if (chunkId == "data")
            {
                int length = Math.Min(chunkSize, bytes.Length - chunkData);
                return (bytes.AsSpan(chunkData, length).ToArray(), sampleRate);
            }

            offset = chunkData + chunkSize + (chunkSize % 2); // chunks are word-aligned
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
