using FifineControl.Core.Dsp;

namespace FifineControl.Core.Tests;

public sealed class AudioDspProcessorTests
{
    private const int SampleRate = 48_000;

    [Fact]
    public void Process_AppliesDigitalGainAndReportsPeaks()
    {
        var processor = new AudioDspProcessor(SampleRate, 1, FlatSettings(6.0206f));
        var samples = new[] { -0.25f, 0.1f, 0.25f };

        processor.Process(samples);

        Assert.Equal(-0.5f, samples[0], precision: 4);
        Assert.Equal(0.5f, samples[2], precision: 4);
        Assert.Equal(0.25f, processor.PrePeak, precision: 4);
        Assert.Equal(0.5f, processor.PostPeak, precision: 4);
    }

    [Fact]
    public void Process_NoiseGateClosesBelowThreshold()
    {
        var settings = FlatSettings() with
        {
            NoiseGate = new NoiseGateSettings
            {
                ThresholdDb = -20,
                AttackMs = 0,
                ReleaseMs = 0
            }
        };
        var processor = new AudioDspProcessor(SampleRate, 1, settings);
        var samples = Enumerable.Repeat(0.001f, 32).ToArray();

        processor.Process(samples);

        Assert.All(samples, sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void Process_CompressorUsesThresholdAndRatio()
    {
        var settings = FlatSettings() with
        {
            Compressor = new CompressorSettings
            {
                ThresholdDb = -12,
                Ratio = 4,
                AttackMs = 0,
                ReleaseMs = 0
            }
        };
        var processor = new AudioDspProcessor(SampleRate, 1, settings);
        var samples = new[] { 1f };

        processor.Process(samples);

        Assert.Equal(MathF.Pow(10, -9f / 20), samples[0], precision: 4);
    }

    [Fact]
    public void Process_PeakingEqBoostsItsCenterFrequency()
    {
        var bands = new[]
        {
            new ParametricEqBandSettings { Name = "Low", FrequencyHz = 120, Q = 1, Bypassed = true },
            new ParametricEqBandSettings { Name = "Mid", FrequencyHz = 1_000, Q = 1, GainDb = 6 },
            new ParametricEqBandSettings { Name = "High", FrequencyHz = 8_000, Q = 1, Bypassed = true }
        };
        var processor = new AudioDspProcessor(SampleRate, 1, FlatSettings() with { EqualizerBands = bands });
        var samples = Enumerable.Range(0, SampleRate)
            .Select(index => 0.1f * MathF.Sin(2 * MathF.PI * 1_000 * index / SampleRate))
            .ToArray();
        var inputRms = Rms(samples.AsSpan(SampleRate / 2));

        processor.Process(samples);
        var outputRms = Rms(samples.AsSpan(SampleRate / 2));

        Assert.InRange(outputRms / inputRms, 1.9f, 2.1f);
    }

    [Fact]
    public void Process_AllBypassedLeavesSignalUnchanged()
    {
        var settings = FlatSettings() with { GainBypassed = true };
        var processor = new AudioDspProcessor(SampleRate, 2, settings);
        var samples = new[] { -0.8f, 0.4f, 0.2f, -0.1f };
        var expected = samples.ToArray();

        processor.Process(samples);

        Assert.Equal(expected, samples);
    }

    [Fact]
    public void Constructor_RejectsEqAboveNyquist()
    {
        var settings = FlatSettings() with
        {
            EqualizerBands =
            [
                new ParametricEqBandSettings { Name = "Invalid", FrequencyHz = 24_000 },
                BypassedBand("Mid", 1_000),
                BypassedBand("High", 8_000)
            ]
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioDspProcessor(SampleRate, 1, settings));
    }

    private static DspSettings FlatSettings(float gainDb = 0) => new()
    {
        DigitalGainDb = gainDb,
        NoiseGate = new NoiseGateSettings { Bypassed = true },
        Compressor = new CompressorSettings { Bypassed = true },
        EqualizerBands =
        [
            BypassedBand("Low", 120),
            BypassedBand("Mid", 1_000),
            BypassedBand("High", 8_000)
        ]
    };

    private static ParametricEqBandSettings BypassedBand(string name, float frequency) => new()
    {
        Name = name,
        FrequencyHz = frequency,
        Bypassed = true
    };

    private static float Rms(ReadOnlySpan<float> samples)
    {
        var sum = 0d;
        foreach (var sample in samples)
        {
            sum += sample * sample;
        }

        return (float)Math.Sqrt(sum / samples.Length);
    }
}
