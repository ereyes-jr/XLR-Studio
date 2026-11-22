using System;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ProjectXLR.Hardware;

/// <summary>Measures the level apps actually receive from Chat Mic, after the GoXLR's processing and AI noise removal.</summary>
public sealed class ChatMicMeter : IDisposable
{
    public const double SilenceDb = -120.0;

    private readonly object _lock = new();
    private WasapiCapture? _capture;
    private double _sumSquares;
    private long _sampleCount;
    private float _peak;

    public bool IsRunning { get; private set; }

    /// <summary>Starts metering. Returns false if Chat Mic isn't available right now.</summary>
    public bool Start()
    {
        if (IsRunning) return true;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var mic = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .FirstOrDefault(d => d.FriendlyName.Contains("Chat Mic", StringComparison.OrdinalIgnoreCase));
            if (mic == null) return false;

            _capture = new WasapiCapture(mic, true, 20);
            if (_capture.WaveFormat.BitsPerSample != 32 || _capture.WaveFormat.AsStandardWaveFormat().Encoding != WaveFormatEncoding.IeeeFloat)
            {
                Stop();
                return false;
            }
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            IsRunning = true;
            return true;
        }
        catch
        {
            Stop();
            return false;
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e) => IsRunning = false;

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
        {
            for (int i = 0; i + 4 <= e.BytesRecorded; i += 4)
            {
                float sample = BitConverter.ToSingle(e.Buffer, i);
                _sumSquares += sample * sample;
                _sampleCount++;
                _peak = Math.Max(_peak, Math.Abs(sample));
            }
        }
    }

    /// <summary>Returns the RMS and peak level since the last call, in dBFS.</summary>
    public (double RmsDb, double PeakDb) TakeLevels()
    {
        lock (_lock)
        {
            double rms = _sampleCount > 0 ? Math.Sqrt(_sumSquares / _sampleCount) : 0;
            double peak = _peak;
            _sumSquares = 0;
            _sampleCount = 0;
            _peak = 0;
            return (ToDb(rms), ToDb(peak));
        }
    }

    private static double ToDb(double linear) => linear > 1e-6 ? 20.0 * Math.Log10(linear) : SilenceDb;

    public void Stop()
    {
        IsRunning = false;
        try
        {
            if (_capture != null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.StopRecording();
                _capture.Dispose();
            }
        }
        catch { }
        _capture = null;
    }

    public void Dispose() => Stop();
}
