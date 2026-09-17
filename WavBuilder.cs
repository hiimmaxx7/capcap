namespace Oculus;

/// <summary>
/// Synthesizes a small mono PCM16 WAV track: silence, with a short decaying
/// sine "tick" burst mixed in at every logged click/key timestamp. Fully
/// procedural, no audio assets, so the tool stays a single small executable.
/// </summary>
internal static class WavBuilder
{
    private const int SampleRate = 22050; // low rate is enough for a UI tick and keeps the file tiny

    public static void WriteEventTrack(string path, double durationSec, IEnumerable<(double TimeSec, InputSoundKind Kind)> events)
    {
        int totalSamples = Math.Max(1, (int)Math.Ceiling(durationSec * SampleRate));
        var samples = new short[totalSamples];

        foreach (var (timeSec, kind) in events)
        {
            MixTick(samples, timeSec, kind);
        }

        WritePcm16Wav(path, samples, SampleRate);
    }

    private static void MixTick(short[] samples, double startSec, InputSoundKind kind)
    {
        double freq = kind switch
        {
            InputSoundKind.Click => 1400.0,
            InputSoundKind.Scroll => 2200.0,
            _ => 900.0
        };
        double durationSec = kind switch
        {
            InputSoundKind.Click => 0.025,
            InputSoundKind.Scroll => 0.012,
            _ => 0.02
        };
        double amplitude = kind switch
        {
            InputSoundKind.Click => 22000.0,
            InputSoundKind.Scroll => 12000.0,
            _ => 16000.0
        };

        int startSample = (int)(startSec * SampleRate);
        int lengthSamples = (int)(durationSec * SampleRate);

        for (int i = 0; i < lengthSamples; i++)
        {
            int idx = startSample + i;
            if (idx < 0 || idx >= samples.Length) continue;

            double t = i / (double)SampleRate;
            double envelope = Math.Exp(-t / (durationSec / 4.0));
            double value = amplitude * envelope * Math.Sin(2 * Math.PI * freq * t);

            int mixed = samples[idx] + (int)value;
            samples[idx] = (short)Math.Clamp(mixed, short.MinValue, short.MaxValue);
        }
    }

    private static void WritePcm16Wav(string path, short[] samples, int sampleRate)
    {
        const int channels = 1;
        const int bitsPerSample = 16;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        int blockAlign = channels * bitsPerSample / 8;
        int dataSize = samples.Length * blockAlign;

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        bw.Write("RIFF"u8.ToArray());
        bw.Write(36 + dataSize);
        bw.Write("WAVE"u8.ToArray());

        bw.Write("fmt "u8.ToArray());
        bw.Write(16);
        bw.Write((short)1); // PCM
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)blockAlign);
        bw.Write((short)bitsPerSample);

        bw.Write("data"u8.ToArray());
        bw.Write(dataSize);
        foreach (var s in samples) bw.Write(s);
    }
}
