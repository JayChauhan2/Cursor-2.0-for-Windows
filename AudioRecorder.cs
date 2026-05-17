using NAudio.Wave;

namespace Cursor2Windows;

public sealed class AudioRecorder : IDisposable
{
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _path;
    public event Action<double>? LevelChanged;

    public string Start()
    {
        _path = Path.Combine(Path.GetTempPath(), $"cursor-voice-{Guid.NewGuid():N}.wav");
        _waveIn = new WaveInEvent { WaveFormat = new WaveFormat(44100, 1), BufferMilliseconds = 80 };
        _writer = new WaveFileWriter(_path, _waveIn.WaveFormat);
        _waveIn.DataAvailable += (_, e) =>
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
            LevelChanged?.Invoke(LevelFromBuffer(e.Buffer, e.BytesRecorded));
        };
        _waveIn.StartRecording();
        return _path;
    }

    public string? Stop()
    {
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;
        _writer?.Dispose();
        _writer = null;
        return _path;
    }

    private static double LevelFromBuffer(byte[] buffer, int bytes)
    {
        double sum = 0;
        var samples = bytes / 2;
        for (var i = 0; i < bytes; i += 2)
        {
            var sample = BitConverter.ToInt16(buffer, i) / 32768.0;
            sum += sample * sample;
        }
        return Math.Clamp(Math.Sqrt(sum / Math.Max(1, samples)) * 6, .04, 1);
    }

    public void Dispose() => Stop();
}
