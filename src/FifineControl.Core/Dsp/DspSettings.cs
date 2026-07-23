namespace FifineControl.Core.Dsp;

public sealed record DspSettings
{
    public bool GainBypassed { get; init; }
    public float DigitalGainDb { get; init; }
    public NoiseGateSettings NoiseGate { get; init; } = new();
    public CompressorSettings Compressor { get; init; } = new();
    public IReadOnlyList<ParametricEqBandSettings> EqualizerBands { get; init; } =
    [
        new() { Name = "Low", FrequencyHz = 120, Q = 0.8f },
        new() { Name = "Mid", FrequencyHz = 1_200, Q = 1.0f },
        new() { Name = "High", FrequencyHz = 8_000, Q = 0.8f }
    ];

    public void Validate(int sampleRate)
    {
        if (sampleRate is < 8_000 or > 384_000)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        if (DigitalGainDb is < -60 or > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(DigitalGainDb), "Digital gain must be between -60 and +30 dB.");
        }

        NoiseGate.Validate();
        Compressor.Validate();
        if (EqualizerBands.Count != 3)
        {
            throw new ArgumentException("Exactly three parametric EQ bands are required.", nameof(EqualizerBands));
        }

        foreach (var band in EqualizerBands)
        {
            band.Validate(sampleRate);
        }
    }
}

public sealed record NoiseGateSettings
{
    public bool Bypassed { get; init; }
    public float ThresholdDb { get; init; } = -48;
    public float AttackMs { get; init; } = 4;
    public float ReleaseMs { get; init; } = 120;

    public void Validate()
    {
        if (ThresholdDb is < -100 or > 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ThresholdDb));
        }

        ValidateTime(AttackMs, nameof(AttackMs));
        ValidateTime(ReleaseMs, nameof(ReleaseMs));
    }

    private static void ValidateTime(float value, string parameterName)
    {
        if (value is < 0 or > 5_000)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public sealed record CompressorSettings
{
    public bool Bypassed { get; init; }
    public float ThresholdDb { get; init; } = -16;
    public float Ratio { get; init; } = 3;
    public float AttackMs { get; init; } = 10;
    public float ReleaseMs { get; init; } = 100;
    public float MakeupGainDb { get; init; }

    public void Validate()
    {
        if (ThresholdDb is < -100 or > 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ThresholdDb));
        }

        if (Ratio is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(Ratio));
        }

        if (AttackMs is < 0 or > 5_000)
        {
            throw new ArgumentOutOfRangeException(nameof(AttackMs));
        }

        if (ReleaseMs is < 0 or > 5_000)
        {
            throw new ArgumentOutOfRangeException(nameof(ReleaseMs));
        }

        if (MakeupGainDb is < -30 or > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(MakeupGainDb));
        }
    }
}

public sealed record ParametricEqBandSettings
{
    public string Name { get; init; } = "Band";
    public bool Bypassed { get; init; }
    public float FrequencyHz { get; init; } = 1_000;
    public float Q { get; init; } = 1;
    public float GainDb { get; init; }

    public void Validate(int sampleRate)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("EQ band name is required.", nameof(Name));
        }

        if (FrequencyHz < 10 || FrequencyHz >= sampleRate / 2f)
        {
            throw new ArgumentOutOfRangeException(nameof(FrequencyHz), "EQ frequency must be below Nyquist.");
        }

        if (Q is < 0.1f or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(Q));
        }

        if (GainDb is < -24 or > 24)
        {
            throw new ArgumentOutOfRangeException(nameof(GainDb));
        }
    }
}
