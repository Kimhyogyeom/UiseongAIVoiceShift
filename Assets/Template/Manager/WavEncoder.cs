using System.IO;
using UnityEngine;

// AudioClip → WAV(16-bit PCM) 파일 저장. ffmpeg가 mp4 합성 시 오디오 트랙으로 사용.
public static class WavEncoder
{
    public static void Save(AudioClip clip, string filePath)
    {
        if (clip == null)
        {
            Debug.LogError("[WAV] clip이 null");
            return;
        }

        var samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using (var fs = File.Create(filePath))
        using (var bw = new BinaryWriter(fs))
        {
            WriteHeader(bw, clip.channels, clip.frequency, samples.Length);
            WriteSamples(bw, samples);
        }

        Debug.Log($"[WAV] saved {filePath} samples={samples.Length} ch={clip.channels} rate={clip.frequency}");
    }

    static void WriteHeader(BinaryWriter bw, int channels, int sampleRate, int totalSamples)
    {
        int byteRate = sampleRate * channels * 2;
        int dataSize = totalSamples * 2;

        bw.Write(new[] { 'R', 'I', 'F', 'F' });
        bw.Write(36 + dataSize);
        bw.Write(new[] { 'W', 'A', 'V', 'E' });

        bw.Write(new[] { 'f', 'm', 't', ' ' });
        bw.Write(16);                  // subchunk1 size
        bw.Write((short)1);            // PCM
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)(channels * 2));
        bw.Write((short)16);

        bw.Write(new[] { 'd', 'a', 't', 'a' });
        bw.Write(dataSize);
    }

    static void WriteSamples(BinaryWriter bw, float[] samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            float s = Mathf.Clamp(samples[i], -1f, 1f);
            bw.Write((short)(s * short.MaxValue));
        }
    }
}
