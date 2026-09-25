using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Capcap;

/// <summary>
/// The recorded click / typing / scroll samples embedded in the exe (Sounds\*.wav), decoded
/// once into mono float clips at <see cref="SampleRate"/>. Leading silence is trimmed so a
/// clip starts right on the logged input timestamp. The typing file is a whole burst of
/// keystrokes, so it's cut into one clip per keystroke and those are rotated through.
/// </summary>
internal static class InputSoundBank
{
    public const int SampleRate = 44100;

    private static readonly Lazy<float[]> ClickClip = new(() => Prepare(Load("click.wav"), 0.75f));
    private static readonly Lazy<float[]> ScrollClip = new(() => Prepare(Load("scroll.wav"), 0.6f));
    private static readonly Lazy<float[][]> KeyClips = new(() => SliceKeystrokes(Load("typing.wav"), 0.6f));

    public static float[] Click => ClickClip.Value;
    public static float[] Scroll => ScrollClip.Value;
    public static float[][] Keys => KeyClips.Value;

    private static float[] Load(string name)
    {
        using var stream = typeof(InputSoundBank).Assembly.GetManifestResourceStream("Capcap.Sounds." + name)
            ?? throw new InvalidOperationException($"Thiếu file âm thanh nhúng: {name}");
        using var reader = new WaveFileReader(stream);

        ISampleProvider source = reader.ToSampleProvider();
        if (source.WaveFormat.SampleRate != SampleRate)
        {
            source = new WdlResamplingSampleProvider(source, SampleRate);
        }

        int channels = source.WaveFormat.Channels;
        var mono = new List<float>();
        var buf = new float[SampleRate * channels];
        int read;
        while ((read = source.Read(buf, 0, buf.Length)) > 0)
        {
            for (int i = 0; i + channels <= read; i += channels)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++) sum += buf[i + c];
                mono.Add(sum / channels);
            }
        }
        return mono.ToArray();
    }

    /// <summary>Trims leading/trailing silence, fades the tail and scales to <paramref name="targetPeak"/>.</summary>
    private static float[] Prepare(float[] s, float targetPeak)
    {
        float peak = Peak(s, 0, s.Length);
        if (peak <= 0) return new float[0];

        int start = 0;
        while (start < s.Length && Math.Abs(s[start]) < peak * 0.05f) start++;
        start = Math.Max(0, start - Ms(2));

        int end = s.Length - 1;
        while (end > start && Math.Abs(s[end]) < peak * 0.01f) end--;
        end = Math.Min(s.Length, end + Ms(10));

        return Finish(s, start, end, targetPeak / peak);
    }

    private static float[][] SliceKeystrokes(float[] s, float targetPeak)
    {
        float peak = Peak(s, 0, s.Length);
        if (peak <= 0) return new[] { new float[0] };

        // Envelope in 10ms windows: a window crossing 35% of the file's peak starts a keystroke,
        // and the detector re-arms once the level falls back under 20%.
        int win = Ms(10);
        var onsets = new List<int>();
        bool armed = true;
        for (int w = 0; w < s.Length; w += win)
        {
            float level = Peak(s, w, Math.Min(s.Length, w + win));
            if (armed && level >= peak * 0.35f)
            {
                // Refine to the first sample of the attack, looking one window back.
                int i = Math.Max(0, w - win);
                while (i < w + win && i < s.Length && Math.Abs(s[i]) < peak * 0.15f) i++;
                int onset = Math.Max(0, i - Ms(2));
                if (onsets.Count == 0 || onset - onsets[onsets.Count - 1] >= Ms(40)) onsets.Add(onset);
                armed = false;
            }
            else if (level < peak * 0.2f)
            {
                armed = true;
            }
        }

        if (onsets.Count == 0) return new[] { Prepare(s, targetPeak) };

        var clips = new float[onsets.Count][];
        for (int k = 0; k < onsets.Count; k++)
        {
            int start = onsets[k];
            int end = Math.Min(start + Ms(150), k + 1 < onsets.Count ? onsets[k + 1] : s.Length);
            clips[k] = Finish(s, start, end, targetPeak / peak);
        }
        return clips;
    }

    private static float[] Finish(float[] s, int start, int end, float gain)
    {
        var clip = new float[Math.Max(0, end - start)];
        for (int i = 0; i < clip.Length; i++) clip[i] = s[start + i] * gain;

        int fade = Math.Min(clip.Length, Ms(8));
        for (int i = 0; i < fade; i++) clip[clip.Length - 1 - i] *= i / (float)fade;
        return clip;
    }

    private static float Peak(float[] s, int from, int to)
    {
        float p = 0;
        for (int i = from; i < to; i++) p = Math.Max(p, Math.Abs(s[i]));
        return p;
    }

    private static int Ms(double ms) => (int)(ms * SampleRate / 1000.0);
}
