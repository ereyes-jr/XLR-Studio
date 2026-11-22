#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ProjectXLR.Hardware;

public sealed class AudioEndpointItem
{
    public string Id { get; }
    public string DisplayName { get; }
    public bool IsGoXlrHardwareDirect { get; }
    public bool IsDefault { get; }

    public AudioEndpointItem(string id, string displayName, bool isHardwareDirect, bool isDefault)
    {
        Id = id;
        DisplayName = displayName;
        IsGoXlrHardwareDirect = isHardwareDirect;
        IsDefault = isDefault;
    }

    public override string ToString() => DisplayName;
}

public sealed class HeadphoneProfileSettings
{
    public string PresetName { get; set; } = "Harman"; // Harman, BassBoost, Gaming, VocalWarm, Flat
    public double SubBassDb { get; set; } = 3.5;
    public double WarmthDb { get; set; } = -0.5;
    public double PresenceDb { get; set; } = -1.5;
    public double AirDb { get; set; } = 1.0;
}

/// <summary>
/// Stereo parametric biquad equalizer filter chain for Sennheiser HD 560S & AudioQuest DragonFly.
/// </summary>
public sealed class HeadphoneEqualizerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly int _sampleRate;

    private sealed class Biquad
    {
        public double B0 = 1, B1 = 0, B2 = 0, A1 = 0, A2 = 0;
        public float X1, X2, Y1, Y2;

        public void Reset()
        {
            X1 = X2 = Y1 = Y2 = 0;
        }

        public float Process(float input)
        {
            double y = B0 * input + B1 * X1 + B2 * X2 - A1 * Y1 - A2 * Y2;
            X2 = X1;
            X1 = input;
            Y2 = Y1;
            Y1 = (float)y;
            return (float)y;
        }

        public void SetLowShelf(double freq, double gainDb, double sampleRate)
        {
            if (Math.Abs(gainDb) < 0.05)
            {
                B0 = 1; B1 = B2 = A1 = A2 = 0;
                return;
            }
            double A = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * freq / sampleRate;
            double cosW = Math.Cos(w0);
            double sinW = Math.Sin(w0);
            double alpha = sinW / 2.0 * Math.Sqrt(2.0);

            double a0 = (A + 1.0) + (A - 1.0) * cosW + 2.0 * Math.Sqrt(A) * alpha;
            B0 = (A * ((A + 1.0) - (A - 1.0) * cosW + 2.0 * Math.Sqrt(A) * alpha)) / a0;
            B1 = (2.0 * A * ((A - 1.0) - (A + 1.0) * cosW)) / a0;
            B2 = (A * ((A + 1.0) - (A - 1.0) * cosW - 2.0 * Math.Sqrt(A) * alpha)) / a0;
            A1 = (-2.0 * ((A - 1.0) + (A + 1.0) * cosW)) / a0;
            A2 = ((A + 1.0) + (A - 1.0) * cosW - 2.0 * Math.Sqrt(A) * alpha) / a0;
        }

        public void SetPeaking(double freq, double gainDb, double q, double sampleRate)
        {
            if (Math.Abs(gainDb) < 0.05)
            {
                B0 = 1; B1 = B2 = A1 = A2 = 0;
                return;
            }
            double A = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * freq / sampleRate;
            double cosW = Math.Cos(w0);
            double sinW = Math.Sin(w0);
            double alpha = sinW / (2.0 * q);

            double a0 = 1.0 + alpha / A;
            B0 = (1.0 + alpha * A) / a0;
            B1 = (-2.0 * cosW) / a0;
            B2 = (1.0 - alpha * A) / a0;
            A1 = (-2.0 * cosW) / a0;
            A2 = (1.0 - alpha / A) / a0;
        }

        public void SetHighShelf(double freq, double gainDb, double sampleRate)
        {
            if (Math.Abs(gainDb) < 0.05)
            {
                B0 = 1; B1 = B2 = A1 = A2 = 0;
                return;
            }
            double A = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * freq / sampleRate;
            double cosW = Math.Cos(w0);
            double sinW = Math.Sin(w0);
            double alpha = sinW / 2.0 * Math.Sqrt(2.0);

            double a0 = (A + 1.0) - (A - 1.0) * cosW + 2.0 * Math.Sqrt(A) * alpha;
            B0 = (A * ((A + 1.0) + (A - 1.0) * cosW + 2.0 * Math.Sqrt(A) * alpha)) / a0;
            B1 = (-2.0 * A * ((A - 1.0) + (A + 1.0) * cosW)) / a0;
            B2 = (A * ((A + 1.0) + (A - 1.0) * cosW - 2.0 * Math.Sqrt(A) * alpha)) / a0;
            A1 = (2.0 * ((A - 1.0) - (A + 1.0) * cosW)) / a0;
            A2 = ((A + 1.0) - (A - 1.0) * cosW - 2.0 * Math.Sqrt(A) * alpha) / a0;
        }
    }

    private readonly Biquad[] _leftFilters = [new(), new(), new(), new()];
    private readonly Biquad[] _rightFilters = [new(), new(), new(), new()];
    private readonly object _filterLock = new();
    private float _headroomGain = 1.0f;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public HeadphoneEqualizerSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _sampleRate = source.WaveFormat.SampleRate;
    }

    public void UpdateEq(double subBassDb, double warmthDb, double presenceDb, double airDb)
    {
        lock (_filterLock)
        {
            // Dynamically calculate required digital headroom based on peak positive boost + biquad overlap margin (prevents clipping and saturation pumping on loud gunshots/explosions)
            double maxBoost = Math.Max(0.0, Math.Max(subBassDb, Math.Max(warmthDb, Math.Max(presenceDb, airDb))));
            double headroomDb = maxBoost > 0 ? maxBoost + 2.5 : 0.0;
            _headroomGain = (float)Math.Pow(10.0, -headroomDb / 20.0);

            // Band 0: Sub-Bass Low Shelf (80 Hz)
            _leftFilters[0].SetLowShelf(80.0, subBassDb, _sampleRate);
            _rightFilters[0].SetLowShelf(80.0, subBassDb, _sampleRate);

            // Band 1: Warmth & Vocal Body Peaking (350 Hz, Q = 0.8)
            _leftFilters[1].SetPeaking(350.0, warmthDb, 0.8, _sampleRate);
            _rightFilters[1].SetPeaking(350.0, warmthDb, 0.8, _sampleRate);

            // Band 2: Clarity & FPS Footstep Presence (3200 Hz, Q = 1.0)
            _leftFilters[2].SetPeaking(3200.0, presenceDb, 1.0, _sampleRate);
            _rightFilters[2].SetPeaking(3200.0, presenceDb, 1.0, _sampleRate);

            // Band 3: Air & Sparkle High Shelf (8500 Hz)
            _leftFilters[3].SetHighShelf(8500.0, airDb, _sampleRate);
            _rightFilters[3].SetHighShelf(8500.0, airDb, _sampleRate);
        }
    }

    private static float SoftLimit(float sample)
    {
        const float threshold = 0.92f;
        const float margin = 0.075f;
        if (sample > threshold)
        {
            return threshold + margin * MathF.Tanh((sample - threshold) / margin);
        }
        if (sample < -threshold)
        {
            return -threshold + margin * MathF.Tanh((sample + threshold) / margin);
        }
        return sample;
    }

    public int Read(Span<float> buffer)
    {
        int read = _source.Read(buffer);
        if (read <= 0) return read;
        if (read < buffer.Length) buffer.Slice(read).Clear();

        lock (_filterLock)
        {
            float hr = _headroomGain;
            if (_channels == 1)
            {
                for (int i = 0; i < read; i++)
                {
                    float s = buffer[i] * hr;
                    for (int f = 0; f < 4; f++)
                    {
                        s = _leftFilters[f].Process(s);
                    }
                    buffer[i] = SoftLimit(s);
                }
            }
            else if (_channels >= 2)
            {
                for (int i = 0; i < read; i += _channels)
                {
                    float left = buffer[i] * hr;
                    float right = buffer[i + 1] * hr;

                    for (int f = 0; f < 4; f++)
                    {
                        left = _leftFilters[f].Process(left);
                        right = _rightFilters[f].Process(right);
                    }

                    buffer[i] = SoftLimit(left);
                    buffer[i + 1] = SoftLimit(right);
                }
            }
        }

        return read;
    }

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
}

/// <summary>
/// Resilient, thread-safe, equal-power crossfade jitter buffer designed specifically for bridging
/// two asynchronous WASAPI audio devices (e.g. TC-Helicon GoXLR Mini -> AudioQuest DragonFly Red).
/// Eliminates clock-drift stutters, DPC latency dropouts, and sample discontinuities.
/// </summary>
public sealed class AudioJitterBuffer : ISampleProvider
{
    private readonly float[] _ringBuffer;
    private readonly int _capacitySamples;
    private readonly int _channels;
    private readonly WaveFormat _waveFormat;
    private readonly object _lock = new();

    private int _readPos;
    private int _writePos;
    private int _availableSamples;

    private readonly int _targetCushionSamples;
    private readonly int _maxCushionSamples;
    private readonly int _preRollSamples;

    private bool _isPreRollComplete;
    private float _lastOutputSampleLeft;
    private float _lastOutputSampleRight;
    private const int FadeLengthSamples = 64; // ~1.3ms micro-fade for glitch-free underrun concealment

    public WaveFormat WaveFormat => _waveFormat;
    public bool IsPreRollComplete => _isPreRollComplete;
    public double BufferedDurationMs
    {
        get
        {
            lock (_lock)
            {
                return (_availableSamples / (double)_channels) * 1000.0 / _waveFormat.SampleRate;
            }
        }
    }

    public long TotalUnderruns { get; private set; }
    public long TotalTrims { get; private set; }

    public AudioJitterBuffer(WaveFormat waveFormat, int capacityMs = 500, int targetCushionMs = 80, int maxCushionMs = 175)
    {
        _waveFormat = waveFormat;
        _channels = Math.Max(1, waveFormat.Channels);
        int samplesPerSec = waveFormat.SampleRate * _channels;

        _capacitySamples = (int)(samplesPerSec * (capacityMs / 1000.0));
        _capacitySamples -= _capacitySamples % _channels;
        _ringBuffer = new float[_capacitySamples];

        _targetCushionSamples = (int)(samplesPerSec * (targetCushionMs / 1000.0));
        _targetCushionSamples -= _targetCushionSamples % _channels;

        _preRollSamples = (int)(samplesPerSec * 0.070); // 70ms pre-roll before starting playback
        _preRollSamples -= _preRollSamples % _channels;

        _maxCushionSamples = (int)(samplesPerSec * (maxCushionMs / 1000.0));
        _maxCushionSamples -= _maxCushionSamples % _channels;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _readPos = 0;
            _writePos = 0;
            _availableSamples = 0;
            _isPreRollComplete = false;
            _lastOutputSampleLeft = 0f;
            _lastOutputSampleRight = 0f;
        }
    }

    public void Write(ReadOnlySpan<byte> byteBuffer)
    {
        if (byteBuffer.IsEmpty) return;

        if (_waveFormat.Encoding == WaveFormatEncoding.IeeeFloat || _waveFormat.BitsPerSample == 32)
        {
            var floatSpan = MemoryMarshal.Cast<byte, float>(byteBuffer);
            Write(floatSpan);
        }
        else if (_waveFormat.BitsPerSample == 16)
        {
            var shortSpan = MemoryMarshal.Cast<byte, short>(byteBuffer);
            float[] temp = new float[shortSpan.Length];
            for (int i = 0; i < shortSpan.Length; i++)
            {
                temp[i] = shortSpan[i] / 32768.0f;
            }
            Write(temp.AsSpan());
        }
    }

    public void Write(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return;

        lock (_lock)
        {
            // If accumulated buffer exceeds maximum cushion, smooth trim excess with equal-power crossfade
            if (_availableSamples + samples.Length > _maxCushionSamples)
            {
                SmoothTrimLocked();
            }

            int samplesToWrite = Math.Min(samples.Length, _capacitySamples - _availableSamples);
            samplesToWrite -= samplesToWrite % _channels;

            int firstChunk = Math.Min(samplesToWrite, _capacitySamples - _writePos);
            samples.Slice(0, firstChunk).CopyTo(_ringBuffer.AsSpan(_writePos, firstChunk));
            _writePos = (_writePos + firstChunk) % _capacitySamples;

            int secondChunk = samplesToWrite - firstChunk;
            if (secondChunk > 0)
            {
                samples.Slice(firstChunk, secondChunk).CopyTo(_ringBuffer.AsSpan(_writePos, secondChunk));
                _writePos = (_writePos + secondChunk) % _capacitySamples;
            }

            _availableSamples += samplesToWrite;

            if (!_isPreRollComplete && _availableSamples >= _preRollSamples)
            {
                _isPreRollComplete = true;
            }
        }
    }

    private void SmoothTrimLocked()
    {
        int excess = _availableSamples - _targetCushionSamples;
        if (excess <= 0) return;
        excess -= excess % _channels;

        // Equal-power cross-fade 10ms (480 stereo frames = 960 floats for stereo 48k)
        int crossfadeFloats = Math.Min(960, _availableSamples - excess);
        crossfadeFloats -= crossfadeFloats % _channels;

        if (crossfadeFloats > _channels * 4)
        {
            int crossfadeFrames = crossfadeFloats / _channels;
            for (int frame = 0; frame < crossfadeFrames; frame++)
            {
                float t = (float)frame / crossfadeFrames;
                float gainA = MathF.Cos(t * MathF.PI * 0.5f);
                float gainB = MathF.Sin(t * MathF.PI * 0.5f);

                int posA = (_readPos + frame * _channels) % _capacitySamples;
                int posB = (_readPos + excess + frame * _channels) % _capacitySamples;

                for (int ch = 0; ch < _channels; ch++)
                {
                    int idxA = (posA + ch) % _capacitySamples;
                    int idxB = (posB + ch) % _capacitySamples;
                    _ringBuffer[idxB] = _ringBuffer[idxA] * gainA + _ringBuffer[idxB] * gainB;
                }
            }
        }

        _readPos = (_readPos + excess) % _capacitySamples;
        _availableSamples -= excess;
        TotalTrims++;
    }

    public int Read(Span<float> buffer)
    {
        if (buffer.IsEmpty) return 0;

        lock (_lock)
        {
            if (!_isPreRollComplete)
            {
                buffer.Clear();
                return buffer.Length;
            }

            int requested = buffer.Length;
            int available = _availableSamples;
            int toCopy = Math.Min(requested, available);
            toCopy -= toCopy % _channels;

            if (toCopy > 0)
            {
                int firstChunk = Math.Min(toCopy, _capacitySamples - _readPos);
                _ringBuffer.AsSpan(_readPos, firstChunk).CopyTo(buffer.Slice(0, firstChunk));
                _readPos = (_readPos + firstChunk) % _capacitySamples;

                int secondChunk = toCopy - firstChunk;
                if (secondChunk > 0)
                {
                    _ringBuffer.AsSpan(_readPos, secondChunk).CopyTo(buffer.Slice(firstChunk, secondChunk));
                    _readPos = (_readPos + secondChunk) % _capacitySamples;
                }

                _availableSamples -= toCopy;

                if (_channels >= 2 && toCopy >= 2)
                {
                    _lastOutputSampleLeft = buffer[toCopy - 2];
                    _lastOutputSampleRight = buffer[toCopy - 1];
                }
                else if (_channels == 1 && toCopy >= 1)
                {
                    _lastOutputSampleLeft = buffer[toCopy - 1];
                }
            }

            if (toCopy < requested)
            {
                TotalUnderruns++;
                int missing = requested - toCopy;
                var missingSlice = buffer.Slice(toCopy, missing);

                // Smooth micro-fade to silence over 1ms to eliminate square-wave edge / filter ringing
                int fadeFrames = Math.Min(FadeLengthSamples / _channels, missing / _channels);
                for (int f = 0; f < fadeFrames; f++)
                {
                    float t = (float)f / fadeFrames;
                    float fade = 0.5f * (1.0f + MathF.Cos(t * MathF.PI));
                    if (_channels >= 2)
                    {
                        missingSlice[f * _channels] = _lastOutputSampleLeft * fade;
                        missingSlice[f * _channels + 1] = _lastOutputSampleRight * fade;
                    }
                    else
                    {
                        missingSlice[f] = _lastOutputSampleLeft * fade;
                    }
                }

                int remaining = missing - fadeFrames * _channels;
                if (remaining > 0)
                {
                    missingSlice.Slice(fadeFrames * _channels).Clear();
                }

                _lastOutputSampleLeft = 0.0f;
                _lastOutputSampleRight = 0.0f;
            }

            return requested;
        }
    }

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
}

public sealed class AudioMonitorService : IDisposable
{
    public const string HardwareDirectId = "GOXLR_HARDWARE_35MM";
    public const string WindowsDefaultId = "WINDOWS_DEFAULT_RENDER";

    private WasapiCapture? _capture;
    private WasapiOut? _renderer;
    private AudioJitterBuffer? _jitterBuffer;
    private readonly object _lock = new();
    private bool _isMonitoring;
    private string _currentDeviceId = WindowsDefaultId;
    private int _startEpoch;
    private HeadphoneProfileSettings _headphoneProfile = new();
    private string _activeCaptureDeviceName = "None";
    private string _activeRenderDeviceName = "None";
    private bool _sidetoneEnabled = true;
    private float _lastVolumePercent = 100.0f;
    private bool _isMicCheckActive = false;

    public bool IsMonitoring => _isMonitoring;
    public string CurrentDeviceId => _currentDeviceId;
    public HeadphoneProfileSettings HeadphoneProfile => _headphoneProfile;
    public string ActiveCaptureDeviceName => _activeCaptureDeviceName;
    public string ActiveRenderDeviceName => _activeRenderDeviceName;
    public bool SidetoneEnabled => _sidetoneEnabled;
    public bool IsMicCheckActive => _isMicCheckActive;

    public void SetMicCheckActive(bool active)
    {
        lock (_lock)
        {
            _isMicCheckActive = active;
        }
    }

    public static List<AudioEndpointItem> GetAvailablePlaybackDevices()
    {
        return new List<AudioEndpointItem>
        {
            new(HardwareDirectId, "Direct Output / External DAC (0ms Native Latency - Recommended)", true, true)
        };
    }

    public void ApplyHeadphoneProfile(string preset, double subBassDb, double warmthDb, double presenceDb, double airDb)
    {
        lock (_lock)
        {
            _headphoneProfile.PresetName = preset;
            _headphoneProfile.SubBassDb = subBassDb;
            _headphoneProfile.WarmthDb = warmthDb;
            _headphoneProfile.PresenceDb = presenceDb;
            _headphoneProfile.AirDb = airDb;
        }
    }

    public void UpdateSidetone(bool sidetoneEnabled)
    {
        lock (_lock)
        {
            _sidetoneEnabled = sidetoneEnabled;
        }
    }

    public void StartMonitoring(string targetEndpointId, float volumePercent, bool sidetoneEnabled = true)
    {
        _sidetoneEnabled = sidetoneEnabled;
        _lastVolumePercent = volumePercent;
        Interlocked.Increment(ref _startEpoch);

        // In 0ms Native Direct mode, software loopback is permanently bypassed.
        // Games and Windows output directly to the DAC (DragonFly Red) with 0ms added delay.
        lock (_lock)
        {
            StopMonitoringInternal();
            _currentDeviceId = HardwareDirectId;
            _isMonitoring = false;
        }
    }

    public void SetVolume(float volumePercent)
    {
    }

    private void StopMonitoringInternal()
    {
        try
        {
            if (_capture != null)
            {
                _capture.StopRecording();
                _capture.Dispose();
                _capture = null;
            }
        }
        catch { }

        try
        {
            if (_renderer != null)
            {
                _renderer.Stop();
                _renderer.Dispose();
                _renderer = null;
            }
        }
        catch { }

        _jitterBuffer?.Clear();
        _jitterBuffer = null;
        _isMonitoring = false;
        _activeCaptureDeviceName = "None";
        _activeRenderDeviceName = "None";
    }

    public void StopMonitoring()
    {
        Interlocked.Increment(ref _startEpoch);
        lock (_lock)
        {
            StopMonitoringInternal();
        }
    }

    public void Dispose()
    {
        StopMonitoring();
    }
}
