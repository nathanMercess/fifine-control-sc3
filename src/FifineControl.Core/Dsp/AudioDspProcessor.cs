namespace FifineControl.Core.Dsp;

public sealed class AudioDspProcessor
{
    private const float MinimumLevel = 0.0000001f;
    private readonly object sync = new();
    private readonly int sampleRate;
    private readonly int channels;
    private DspSettings settings;
    private BiquadFilter[][] equalizers;
    private float gateEnvelope = 1;
    private float compressorGainDb;
    private float prePeak;
    private float postPeak;

    public AudioDspProcessor(int sampleRate, int channels, DspSettings settings)
    {
        if (channels is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(channels));
        }

        settings.Validate(sampleRate);
        this.sampleRate = sampleRate;
        this.channels = channels;
        this.settings = settings;
        equalizers = CreateEqualizers(settings);
    }

    public int SampleRate => sampleRate;
    public int Channels => channels;
    public float PrePeak => Volatile.Read(ref prePeak);
    public float PostPeak => Volatile.Read(ref postPeak);

    public void UpdateSettings(DspSettings updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        updated.Validate(sampleRate);
        lock (sync)
        {
            settings = updated;
            equalizers = CreateEqualizers(updated);
        }
    }

    public void Process(Span<float> samples)
    {
        if (samples.Length % channels != 0)
        {
            throw new ArgumentException("The interleaved sample count must be divisible by the channel count.", nameof(samples));
        }

        lock (sync)
        {
            var localPrePeak = 0f;
            var localPostPeak = 0f;
            var digitalGain = settings.GainBypassed ? 1 : DbToLinear(settings.DigitalGainDb);
            var makeupGain = settings.Compressor.Bypassed ? 1 : DbToLinear(settings.Compressor.MakeupGainDb);
            var gateAttack = SmoothingCoefficient(settings.NoiseGate.AttackMs, sampleRate);
            var gateRelease = SmoothingCoefficient(settings.NoiseGate.ReleaseMs, sampleRate);
            var compressorAttack = SmoothingCoefficient(settings.Compressor.AttackMs, sampleRate);
            var compressorRelease = SmoothingCoefficient(settings.Compressor.ReleaseMs, sampleRate);

            for (var frameStart = 0; frameStart < samples.Length; frameStart += channels)
            {
                var framePeak = 0f;
                for (var channel = 0; channel < channels; channel++)
                {
                    var absolute = MathF.Abs(samples[frameStart + channel]);
                    framePeak = MathF.Max(framePeak, absolute);
                    localPrePeak = MathF.Max(localPrePeak, absolute);
                }

                var gateGain = 1f;
                if (!settings.NoiseGate.Bypassed)
                {
                    var target = LinearToDb(framePeak) >= settings.NoiseGate.ThresholdDb ? 1f : 0f;
                    var coefficient = target > gateEnvelope ? gateAttack : gateRelease;
                    gateEnvelope = target + coefficient * (gateEnvelope - target);
                    gateGain = gateEnvelope;
                }

                var compressorGain = 1f;
                if (!settings.Compressor.Bypassed)
                {
                    var levelDb = LinearToDb(framePeak * digitalGain * gateGain);
                    var targetGainDb = levelDb > settings.Compressor.ThresholdDb
                        ? settings.Compressor.ThresholdDb +
                          ((levelDb - settings.Compressor.ThresholdDb) / settings.Compressor.Ratio) -
                          levelDb
                        : 0;
                    var coefficient = targetGainDb < compressorGainDb ? compressorAttack : compressorRelease;
                    compressorGainDb = targetGainDb + coefficient * (compressorGainDb - targetGainDb);
                    compressorGain = DbToLinear(compressorGainDb) * makeupGain;
                }

                for (var channel = 0; channel < channels; channel++)
                {
                    var value = samples[frameStart + channel] * digitalGain * gateGain * compressorGain;
                    for (var band = 0; band < settings.EqualizerBands.Count; band++)
                    {
                        if (!settings.EqualizerBands[band].Bypassed)
                        {
                            value = equalizers[band][channel].Transform(value);
                        }
                    }

                    value = Math.Clamp(value, -1, 1);
                    samples[frameStart + channel] = value;
                    localPostPeak = MathF.Max(localPostPeak, MathF.Abs(value));
                }
            }

            Volatile.Write(ref prePeak, localPrePeak);
            Volatile.Write(ref postPeak, localPostPeak);
        }
    }

    private BiquadFilter[][] CreateEqualizers(DspSettings source) => source.EqualizerBands
        .Select(band => Enumerable.Range(0, channels)
            .Select(_ => BiquadFilter.CreatePeaking(sampleRate, band))
            .ToArray())
        .ToArray();

    private static float SmoothingCoefficient(float milliseconds, int rate) => milliseconds <= 0
        ? 0
        : MathF.Exp(-1 / (milliseconds * 0.001f * rate));

    private static float DbToLinear(float decibels) => MathF.Pow(10, decibels / 20);
    private static float LinearToDb(float value) => 20 * MathF.Log10(MathF.Max(MathF.Abs(value), MinimumLevel));
}
