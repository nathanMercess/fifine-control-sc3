namespace FifineControl.Core.Dsp;

internal sealed class BiquadFilter
{
    private readonly float b0;
    private readonly float b1;
    private readonly float b2;
    private readonly float a1;
    private readonly float a2;
    private float z1;
    private float z2;

    private BiquadFilter(float b0, float b1, float b2, float a1, float a2)
    {
        this.b0 = b0;
        this.b1 = b1;
        this.b2 = b2;
        this.a1 = a1;
        this.a2 = a2;
    }

    public static BiquadFilter CreatePeaking(int sampleRate, ParametricEqBandSettings settings)
    {
        var amplitude = MathF.Pow(10, settings.GainDb / 40);
        var omega = 2 * MathF.PI * settings.FrequencyHz / sampleRate;
        var alpha = MathF.Sin(omega) / (2 * settings.Q);
        var cosine = MathF.Cos(omega);
        var a0 = 1 + alpha / amplitude;

        return new BiquadFilter(
            (1 + alpha * amplitude) / a0,
            (-2 * cosine) / a0,
            (1 - alpha * amplitude) / a0,
            (-2 * cosine) / a0,
            (1 - alpha / amplitude) / a0);
    }

    public float Transform(float sample)
    {
        var output = b0 * sample + z1;
        z1 = b1 * sample - a1 * output + z2;
        z2 = b2 * sample - a2 * output;
        return output;
    }
}
