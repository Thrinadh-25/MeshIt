using System.IO;
using NAudio.Wave;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Records and plays back voice messages using NAudio.
/// Records 16kHz mono 16-bit PCM → WAV byte array.
/// </summary>
public class AudioService : IDisposable
{
    private WaveInEvent? _waveIn;
    private MemoryStream? _audioStream;
    private WaveFileWriter? _waveWriter;
    private bool _isRecording;

    public bool IsRecording => _isRecording;

    /// <summary>Raised on each audio chunk for visual level metering.</summary>
    public event Action<float>? AudioLevelChanged;

    /// <summary>Start recording audio from default mic.</summary>
    public void StartRecording()
    {
        if (_isRecording) return;

        _audioStream = new MemoryStream();
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1) // 16kHz, 16-bit, mono
        };

        _waveWriter = new WaveFileWriter(_audioStream, _waveIn.WaveFormat);

        _waveIn.DataAvailable += (_, e) =>
        {
            _waveWriter.Write(e.Buffer, 0, e.BytesRecorded);

            // Compute RMS level for metering
            float max = 0;
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                var sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                var abs = Math.Abs(sample / 32768f);
                if (abs > max) max = abs;
            }
            AudioLevelChanged?.Invoke(max);
        };

        _waveIn.StartRecording();
        _isRecording = true;
        Log.Information("Audio recording started");
    }

    /// <summary>Stop recording and return the WAV byte array.</summary>
    public byte[] StopRecording()
    {
        if (!_isRecording) return Array.Empty<byte>();

        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;

        _waveWriter?.Flush();
        _waveWriter?.Dispose();
        _waveWriter = null;

        var data = _audioStream?.ToArray() ?? Array.Empty<byte>();
        _audioStream?.Dispose();
        _audioStream = null;
        _isRecording = false;

        Log.Information("Audio recording stopped ({Size} bytes)", data.Length);
        return data;
    }

    /// <summary>Play a WAV byte array through the default output device.</summary>
    public async Task PlayAudioAsync(byte[] wavData)
    {
        try
        {
            using var ms = new MemoryStream(wavData);
            using var reader = new WaveFileReader(ms);
            using var waveOut = new WaveOutEvent();

            waveOut.Init(reader);
            waveOut.Play();

            while (waveOut.PlaybackState == PlaybackState.Playing)
                await Task.Delay(100);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Audio playback failed");
        }
    }

    public void Dispose()
    {
        _waveIn?.Dispose();
        _waveWriter?.Dispose();
        _audioStream?.Dispose();
    }
}
