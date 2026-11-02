using NAudio.Dsp;
using NAudio.Wave;

namespace DynamicIsland;

/// <summary>
/// Listens to what the speakers are playing (WASAPI loopback) and reduces it to four
/// frequency band levels in 0..1 for the equalizer bars. Runs only while the bars are on screen.
/// </summary>
public sealed class AudioVisualizer : IDisposable
{
    const int FftExponent = 10;              // 1024-point FFT
    const int FftSize = 1 << FftExponent;
    static readonly double[] BandEdges = [40, 220, 900, 3500, 12000];

    readonly Complex[] _fft = new Complex[FftSize];
    readonly float[] _levels = new float[BandEdges.Length - 1];
    readonly float[] _gain = [0.02f, 0.02f, 0.02f, 0.02f];
    readonly object _gate = new();
    WasapiLoopbackCapture? _capture;
    int _filled;

    public bool IsRunning => _capture != null;

    public void Start()
    {
        if (_capture != null)
            return;
        try
        {
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnData;
            _capture.StartRecording();
        }
        catch
        {
            _capture = null; // no output device; the bars fall back to a gentle animation
        }
    }

    public void Stop()
    {
        if (_capture == null)
            return;
        try { _capture.StopRecording(); } catch { /* already stopped */ }
        _capture.Dispose();
        _capture = null;
        lock (_gate)
            Array.Clear(_levels);
    }

    /// <summary>Latest band levels, bass first.</summary>
    public float[] Levels
    {
        get
        {
            lock (_gate)
                return (float[])_levels.Clone();
        }
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        var format = _capture?.WaveFormat;
        if (format == null || format.Encoding != WaveFormatEncoding.IeeeFloat)
            return;

        var channels = format.Channels;
        var frames = e.BytesRecorded / 4 / channels;
        for (var i = 0; i < frames; i++)
        {
            float sample = 0;
            for (var c = 0; c < channels; c++)
                sample += BitConverter.ToSingle(e.Buffer, (i * channels + c) * 4);
            sample /= channels;

            _fft[_filled].X = (float)(sample * FastFourierTransform.HannWindow(_filled, FftSize));
            _fft[_filled].Y = 0;
            if (++_filled == FftSize)
            {
                FastFourierTransform.FFT(true, FftExponent, _fft);
                Analyze(format.SampleRate);
                _filled = 0;
            }
        }
    }

    void Analyze(int sampleRate)
    {
        var binHz = (double)sampleRate / FftSize;
        var levels = new float[_levels.Length];
        for (var band = 0; band < levels.Length; band++)
        {
            var from = Math.Max(1, (int)(BandEdges[band] / binHz));
            var to = Math.Min(FftSize / 2 - 1, (int)(BandEdges[band + 1] / binHz));
            double sum = 0;
            for (var bin = from; bin <= to; bin++)
                sum += Math.Sqrt(_fft[bin].X * _fft[bin].X + _fft[bin].Y * _fft[bin].Y);
            var magnitude = (float)(sum / Math.Max(1, to - from + 1));

            // Per-band automatic gain: follows peaks quickly, relaxes slowly, so quiet and loud songs both move.
            _gain[band] = Math.Max(magnitude, _gain[band] * 0.995f);
            _gain[band] = Math.Max(_gain[band], 0.0005f);
            levels[band] = Math.Clamp(magnitude / _gain[band], 0, 1);
        }

        lock (_gate)
            Array.Copy(levels, _levels, levels.Length);
    }

    public void Dispose() => Stop();
}
