using System.Text;

namespace Capcap;

/// <summary>
/// Builds the click/key/scroll effects track: a mono PCM16 WAV of silence with the recorded
/// samples from <see cref="InputSoundBank"/> mixed in at every logged input timestamp.
/// Written in one-second blocks so a long recording doesn't need the whole track in memory.
/// </summary>
internal static class WavBuilder
{
    private const int SampleRate = InputSoundBank.SampleRate;

    // Every wheel notch fires its own event and the scroll sample already holds a few ticks,
    // so while one is still playing, further notches don't restart it on top of itself.
    private const double ScrollRetriggerSec = 0.15;

    public static void WriteEventTrack(string path, double durationSec, IEnumerable<(double TimeSec, InputSoundKind Kind)> events)
    {
        long totalSamples = Math.Max(1, (long)Math.Ceiling(durationSec * SampleRate));
        var hits = ScheduleClips(events);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        WriteHeader(bw, totalSamples);

        var mix = new float[SampleRate];
        var active = new List<(long Start, float[] Clip)>();
        int next = 0;

        for (long blockStart = 0; blockStart < totalSamples; blockStart += mix.Length)
        {
            int len = (int)Math.Min(mix.Length, totalSamples - blockStart);
            long blockEnd = blockStart + len;
            Array.Clear(mix, 0, len);

            while (next < hits.Count && hits[next].Start < blockEnd) active.Add(hits[next++]);

            for (int a = active.Count - 1; a >= 0; a--)
            {
                var (start, clip) = active[a];
                long from = Math.Max(start, blockStart);
                long to = Math.Min(start + clip.Length, blockEnd);
                for (long t = from; t < to; t++) mix[t - blockStart] += clip[t - start];
                if (start + clip.Length <= blockEnd) active.RemoveAt(a);
            }

            for (int i = 0; i < len; i++)
            {
                float v = Math.Max(-1f, Math.Min(1f, mix[i]));
                bw.Write((short)(v * short.MaxValue));
            }
        }
    }

    /// <summary>Turns input events into (start sample, clip) pairs, sorted by start.</summary>
    private static List<(long Start, float[] Clip)> ScheduleClips(IEnumerable<(double TimeSec, InputSoundKind Kind)> events)
    {
        var keys = InputSoundBank.Keys;
        var rng = new Random(12345);
        int lastKey = -1;
        double lastScroll = double.NegativeInfinity;
        var hits = new List<(long, float[])>();

        foreach (var (timeSec, kind) in events.OrderBy(e => e.TimeSec))
        {
            float[] clip;
            switch (kind)
            {
                case InputSoundKind.Click:
                    clip = InputSoundBank.Click;
                    break;
                case InputSoundKind.Scroll:
                    if (timeSec - lastScroll < ScrollRetriggerSec) continue;
                    lastScroll = timeSec;
                    clip = InputSoundBank.Scroll;
                    break;
                default:
                    // Rotate through the individual keystrokes, never the same one twice in a row.
                    int k = rng.Next(keys.Length);
                    if (keys.Length > 1 && k == lastKey) k = (k + 1) % keys.Length;
                    lastKey = k;
                    clip = keys[k];
                    break;
            }
            if (clip.Length > 0) hits.Add(((long)(timeSec * SampleRate), clip));
        }
        return hits;
    }

    private static void WriteHeader(BinaryWriter bw, long totalSamples)
    {
        const int channels = 1;
        const int bitsPerSample = 16;
        int byteRate = SampleRate * channels * bitsPerSample / 8;
        int blockAlign = channels * bitsPerSample / 8;
        int dataSize = (int)Math.Min(int.MaxValue - 36, totalSamples * blockAlign);

        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataSize);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));

        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1); // PCM
        bw.Write((short)channels);
        bw.Write(SampleRate);
        bw.Write(byteRate);
        bw.Write((short)blockAlign);
        bw.Write((short)bitsPerSample);

        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);
    }
}
