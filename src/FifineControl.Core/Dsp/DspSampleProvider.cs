using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace FifineControl.Core.Dsp;

public sealed class DspSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly AudioDspProcessor processor;

    public DspSampleProvider(IWaveProvider source, AudioDspProcessor processor)
        : this(source.ToSampleProvider(), processor)
    {
    }

    public DspSampleProvider(ISampleProvider source, AudioDspProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(processor);
        if (source.WaveFormat.SampleRate != processor.SampleRate || source.WaveFormat.Channels != processor.Channels)
        {
            throw new ArgumentException("Source format must match the DSP processor format.", nameof(source));
        }

        this.source = source;
        this.processor = processor;
    }

    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = source.Read(buffer, offset, count);
        processor.Process(buffer.AsSpan(offset, read));
        return read;
    }
}
