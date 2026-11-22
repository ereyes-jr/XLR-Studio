using System;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ProjectXLR.Hardware;

/// <summary>
/// Plays Chat Mic (the signal apps receive, after AI noise removal) into the user's headphones.
/// Headphones on a USB DAC can't receive the GoXLR's hardware sidetone, so this is what makes
/// "Hear myself" and "Test what others hear" audible there.
/// </summary>
public sealed class MicMonitorEngine : IDisposable
{
    private const int CaptureBufferMs = 10;
    private const int RenderLatencyMs = 20;
    private const int MaxBufferedMs = 80; // The two devices run on separate clocks, so drift is trimmed here

    private readonly object _lock = new();
    private WasapiCapture? _capture;
    private WasapiOut? _renderer;
    private BufferedWaveProvider? _buffer;
    private VolumeSampleProvider? _volume;
    private float _pendingVolume = 1f;

    public bool IsRunning { get; private set; }
    public string CaptureDeviceName { get; private set; } = string.Empty;
    public string RenderDeviceName { get; private set; } = string.Empty;
    public long CapturedBytes { get; private set; }
    public float PeakSample { get; private set; }

    public float Volume
    {
        set
        {
            lock (_lock)
            {
                _pendingVolume = Math.Clamp(value, 0f, 1f);
                if (_volume != null) _volume.Volume = _pendingVolume;
            }
        }
    }

    /// <summary>Starts monitoring. Returns null on success, otherwise a message for the user.</summary>
    public string? Start()
    {
        lock (_lock)
        {
            if (IsRunning) return null;
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var mic = FindChatMic(enumerator);
                if (mic == null) return "Couldn't find Chat Mic. Check that it's turned on in Windows sound settings.";
                var output = FindHeadphones(enumerator);
                if (output == null) return "Couldn't find your headphones in Windows sound settings.";

                CaptureDeviceName = mic.FriendlyName;
                RenderDeviceName = output.FriendlyName;
                CapturedBytes = 0;
                PeakSample = 0;

                _capture = new WasapiCapture(mic, true, CaptureBufferMs);
                _buffer = new BufferedWaveProvider(_capture.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    ReadFully = true
                };
                _volume = new VolumeSampleProvider(_buffer.ToSampleProvider()) { Volume = _pendingVolume };
                _renderer = new WasapiOut(output, AudioClientShareMode.Shared, true, RenderLatencyMs);
                _renderer.Init(_volume);

                _capture.DataAvailable += OnDataAvailable;
                _capture.StartRecording();
                _renderer.Play();
                IsRunning = true;
                return null;
            }
            catch (Exception ex)
            {
                StopInternal();
                return $"Couldn't start listening to your mic: {ex.Message}";
            }
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var buffer = _buffer;
        if (buffer == null || e.BytesRecorded == 0) return;

        CapturedBytes += e.BytesRecorded;
        if (buffer.WaveFormat.BitsPerSample == 32 && buffer.WaveFormat.AsStandardWaveFormat().Encoding == WaveFormatEncoding.IeeeFloat)
        {
            for (int i = 0; i + 4 <= e.BytesRecorded; i += 4)
            {
                float sample = Math.Abs(BitConverter.ToSingle(e.Buffer, i));
                if (sample > PeakSample) PeakSample = sample;
            }
        }

        if (buffer.BufferedDuration.TotalMilliseconds > MaxBufferedMs)
        {
            buffer.ClearBuffer();
        }
        buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
    }

    public void Stop()
    {
        lock (_lock)
        {
            StopInternal();
        }
    }

    private void StopInternal()
    {
        IsRunning = false;
        try
        {
            if (_capture != null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.StopRecording();
                _capture.Dispose();
            }
        }
        catch { }
        try
        {
            _renderer?.Stop();
            _renderer?.Dispose();
        }
        catch { }
        _capture = null;
        _renderer = null;
        _buffer = null;
        _volume = null;
    }

    private static MMDevice? FindChatMic(MMDeviceEnumerator enumerator) =>
        enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .FirstOrDefault(d => d.FriendlyName.Contains("Chat Mic", StringComparison.OrdinalIgnoreCase));

    /// <summary>The Windows default output, unless that's a GoXLR channel, which would loop back through the mixer.</summary>
    private static MMDevice? FindHeadphones(MMDeviceEnumerator enumerator)
    {
        var outputs = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
        string? defaultId = enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID
            : null;

        return outputs.FirstOrDefault(d => d.ID == defaultId && !IsGoXlr(d))
            ?? outputs.FirstOrDefault(d => !IsGoXlr(d));
    }

    private static bool IsGoXlr(MMDevice device) => device.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase);

    public void Dispose() => Stop();
}
