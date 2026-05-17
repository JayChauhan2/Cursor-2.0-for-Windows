using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace Cursor2Windows;

public sealed class VoicePromptManager
{
    private readonly OverlayState _state;
    private readonly Dispatcher _dispatcher;
    private AudioRecorder? _recorder;
    private string? _recordingPath;
    private bool _isRunning;
    private string _draftPrompt = "";
    private readonly DispatcherTimer _typingTimer = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private string _typingAnswer = "";
    private int _typingIndex;

    public VoicePromptManager(OverlayState state, Dispatcher dispatcher)
    {
        _state = state;
        _dispatcher = dispatcher;
        _typingTimer.Tick += (_, _) => TypeTick();
    }

    public void Toggle()
    {
        if (_state.IsVisible || _isRunning) ClosePrompt();
        else Start();
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _typingTimer.Stop();
        _draftPrompt = "";
        _state.ResponseText = "";
        _state.UserText = "";
        _state.SubmittedText = "";
        _state.ShowsTetris = false;
        _state.AudioLevel = .12;
        _state.IsListening = true;
        _state.IsProcessing = false;
        _state.IsSending = false;
        _state.IsVisible = true;
        _state.Notify();

        try
        {
            _recorder = new AudioRecorder();
            _recorder.LevelChanged += level => _dispatcher.Invoke(() => { _state.AudioLevel = level; _state.Notify(); });
            _recordingPath = _recorder.Start();
        }
        catch
        {
            Fail("lemme use the mic first");
        }
    }

    public void StopRecordingForTyping()
    {
        if (!_isRunning || !_state.IsListening) return;
        _recordingPath = _recorder?.Stop();
        _recorder = null;
        _state.IsListening = false;
        _state.AudioLevel = 0;
        _state.Notify();
    }

    public void HandleEmptyEnter()
    {
        if (_isRunning)
        {
            if (_state.IsListening) SubmitVoiceCommand();
            return;
        }
        if (_state.IsVisible) Start();
    }

    public void SubmitVoiceCommand()
    {
        if (!_isRunning || !_state.IsListening) return;
        FinishRecording();
    }

    public void ExitTetrisToPrompt()
    {
        _state.ShowsTetris = false;
        _state.ResponseText = "";
        _state.UserText = "";
        _state.IsProcessing = false;
        _state.IsSending = false;
        _state.AudioLevel = 0;
        _state.Notify();
        if (!_isRunning) Start();
    }

    public void ProcessTypedCommand(string text)
    {
        _state.ResponseText = "";
        _state.SubmittedText = text;
        _state.IsProcessing = true;
        _state.IsSending = true;
        _state.Notify();
        DebugLog.Write($"typed submit prompt={Quote(text)}");
        _ = RunAnswer(text);
    }

    private void FinishRecording()
    {
        _recordingPath = _recorder?.Stop();
        _recorder = null;
        _state.IsListening = false;
        _state.IsProcessing = true;
        _state.AudioLevel = .18;
        _state.Notify();

        if (string.IsNullOrWhiteSpace(_recordingPath))
        {
            Fail("heard jack shit");
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var transcript = await new GroqClient().Transcribe(_recordingPath);
                TryDelete(_recordingPath);
                var cleaned = CleanedTranscript(transcript);
                if (ShouldClose(cleaned))
                {
                    _dispatcher.Invoke(ClosePrompt);
                    return;
                }
                if (cleaned.Length == 0)
                {
                    _dispatcher.Invoke(() => Fail("heard jack shit"));
                    return;
                }
                var command = ExtractSendCommand(cleaned);
                var prompt = PromptForSending(command.ShouldSend ? command.Message : cleaned);
                if (prompt.Length == 0)
                {
                    _dispatcher.Invoke(() => Fail("heard jack shit"));
                    return;
                }
                _dispatcher.Invoke(() =>
                {
                    _state.SubmittedText = prompt;
                    _state.Notify();
                });
                DebugLog.Write($"voice submit prompt={Quote(prompt)} transcript={Quote(cleaned)}");
                await RunAnswer(prompt);
            }
            catch (Exception ex)
            {
                _dispatcher.Invoke(() => Fail(BroErrorMessage.Text(ex)));
            }
        });
    }

    private async Task RunAnswer(string prompt)
    {
        try
        {
            DebugLog.Write($"answer start prompt={Quote(prompt)}");
            var answer = await Answer(prompt);
            DebugLog.Write($"answer done prompt={Quote(prompt)} answer={Quote(answer)}");
            _dispatcher.Invoke(() =>
            {
                _state.IsProcessing = false;
                _state.IsSending = false;
                _state.AudioLevel = 0;
                Type(answer);
            });
        }
        catch (Exception ex)
        {
            DebugLog.Write($"answer failed prompt={Quote(prompt)} error={Quote(ex.Message)}");
            _dispatcher.Invoke(() => Fail(BroErrorMessage.Text(ex)));
        }
    }

    private async Task<string> Answer(string prompt)
    {
        if (Regex.IsMatch(prompt, @"\btetris\b", RegexOptions.IgnoreCase))
        {
            DebugLog.Write($"route=tetris prompt={Quote(prompt)}");
            _dispatcher.Invoke(() =>
            {
                _state.ResponseText = "";
                _state.UserText = "";
                _state.IsProcessing = false;
                _state.IsSending = false;
                _state.AudioLevel = 0;
                _state.ShowsTetris = true;
                _state.Notify();
            });
            return "";
        }

        if (Regex.IsMatch(prompt, @"\b(click|tap|press|select)\b", RegexOptions.IgnoreCase))
        {
            DebugLog.Write($"route=click prompt={Quote(prompt)}");
            return await ClickOnScreen(prompt);
        }

        if (ExplicitLocation(prompt) is { } location)
        {
            DebugLog.Write($"route=location prompt={Quote(prompt)} location={Quote(location)}");
            MemoryStore.Shared.SetHomeLocation(location);
            var answer = $"got you, i'll remember {location.ToLowerInvariant()}";
            MemoryStore.Shared.Save(prompt, answer);
            return answer;
        }

        DebugLog.Write($"route=normal prompt={Quote(prompt)}");
        return await AnswerNormally(prompt);
    }

    private async Task<string> AnswerNormally(string prompt)
    {
        var client = new GroqClient();
        var memory = MemoryStore.Shared.Context();
        var searchQuery = await client.SearchQuery(prompt, memory);
        DebugLog.Write($"search query prompt={Quote(prompt)} query={Quote(searchQuery ?? "no")}");
        var search = searchQuery is null ? null : await new TavilyClient().SearchContext(searchQuery);
        var answer = await client.Chat(prompt, memory, search);
        MemoryStore.Shared.Save(prompt, answer);
        return answer;
    }

    private async Task<string> ClickOnScreen(string prompt)
    {
        var wasVisible = _state.IsVisible;
        _dispatcher.Invoke(() => { _state.IsVisible = false; _state.Notify(); });
        await Task.Delay(160);
        var snapshot = ComputerUseController.CaptureMainDisplay();
        if (wasVisible) _dispatcher.Invoke(() => { _state.IsVisible = true; _state.Notify(); });
        var target = await new GroqClient().LocateClickTarget(prompt, snapshot);
        ComputerUseController.Click(target, snapshot);
        return "clicked";
    }

    private string PromptForSending(string text)
    {
        if (!string.IsNullOrWhiteSpace(text)) _draftPrompt = string.Join(" ", new[] { _draftPrompt, text }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        var prompt = Regex.Replace(_draftPrompt, @"\s+", " ").Trim();
        _draftPrompt = "";
        return prompt;
    }

    private void Type(string answer)
    {
        _state.ResponseText = "";
        _typingAnswer = answer;
        _typingIndex = 0;
        if (answer.Length == 0) _isRunning = false;
        else _typingTimer.Start();
        _state.Notify();
    }

    private void TypeTick()
    {
        if (_typingIndex < _typingAnswer.Length)
        {
            _state.ResponseText += _typingAnswer[_typingIndex++];
            _state.Notify();
        }
        else
        {
            _typingTimer.Stop();
            _isRunning = false;
        }
    }

    private void Fail(string message)
    {
        _recorder?.Dispose();
        _recorder = null;
        _typingTimer.Stop();
        _draftPrompt = "";
        _state.IsListening = false;
        _state.IsProcessing = false;
        _state.AudioLevel = 0;
        _state.IsSending = false;
        _state.ShowsTetris = false;
        _state.ResponseText = message;
        _isRunning = false;
        _state.Notify();
    }

    private void ClosePrompt()
    {
        _recorder?.Dispose();
        _recorder = null;
        _typingTimer.Stop();
        _draftPrompt = "";
        _state.ResponseText = "";
        _state.UserText = "";
        _state.SubmittedText = "";
        _state.IsListening = false;
        _state.IsProcessing = false;
        _state.AudioLevel = 0;
        _state.IsSending = false;
        _state.ShowsTetris = false;
        _state.IsVisible = false;
        _isRunning = false;
        _state.Notify();
    }

    private static string CleanedTranscript(string transcript)
    {
        var cleaned = Regex.Replace(transcript, @"(?i)\b(thank\s+you|thanks|thank\s+you\s+very\s+much|thanks\s+for\s+watching)\b[.!?,;:\s]*", "");
        return Regex.Replace(cleaned, @"\s+", " ").Trim().Trim('.', ',', '!', '?', ';', ':');
    }

    private static (string Message, bool ShouldSend) ExtractSendCommand(string transcript)
    {
        var match = Regex.Match(transcript, @"\bsend\s+message\b", RegexOptions.IgnoreCase);
        if (!match.Success) return (transcript, false);
        var message = Regex.Replace(transcript.Remove(match.Index, match.Length), @"\s+", " ").Trim().Trim('.', ',', '!', '?', ';', ':');
        return (message, true);
    }

    private static bool ShouldClose(string transcript)
    {
        var words = Regex.Split(transcript.ToLowerInvariant(), @"[^a-z0-9]+").Where(w => w.Length > 0).ToArray();
        return words.SequenceEqual(new[] { "bye" }) || words.SequenceEqual(new[] { "goodbye" }) || words.SequenceEqual(new[] { "bye", "bye" });
    }

    private static string? ExplicitLocation(string prompt)
    {
        var match = Regex.Match(prompt, @"(?i)\b(?:my location is|i(?:'|’)?m in|i am in|i live in)\s+(.+)$");
        if (!match.Success) match = Regex.Match(prompt, @"(?i)\b(?:remember|set)\s+(?:my\s+)?location\s+(?:as|to)\s+(.+)$");
        return match.Success ? CleanLocation(match.Groups[1].Value) : null;
    }

    private static string? LikelyLocation(string prompt)
    {
        var cleaned = CleanLocation(prompt);
        if (cleaned.Length is 0 or > 80) return null;
        if (Regex.IsMatch(cleaned, @"\b(weather|forecast|temperature|what|how|why|when|where|who|click|tap|press|select)\b", RegexOptions.IgnoreCase)) return null;
        return Regex.IsMatch(cleaned, @"[A-Za-z]{2,}") ? cleaned : null;
    }

    private static string CleanLocation(string text) => Regex.Replace(text, @"\s+", " ").Trim().Trim('.', ',', '!', '?', ';', ':');

    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

    private static void TryDelete(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path)) try { File.Delete(path); } catch { }
    }
}

public static class BroErrorMessage
{
    public static string Text(Exception error)
    {
        var message = error.Message.ToLowerInvariant();
        if (message.Contains("groq key") || message.Contains("api key")) return "drop the Groq key in first";
        if (message.Contains("tavily")) return "bro Tavily got weird";
        if (message.Contains("timed")) return "Groq is slow as hell";
        if (message.Contains("where to click")) return "bro idk where to click";
        if (message.Contains("mic") || message.Contains("audio")) return "lemme use the mic first";
        if (error is HttpRequestException http && http.StatusCode is { } status)
        {
            var code = (int)status;
            if (code == 401) return "your Groq key is wrong";
            if (code == 403) return "Groq said no access";
            if (code == 429) return "rate limit slapped us";
            if (code >= 500) return "Groq server is cooked";
        }
        return "something ate shit";
    }
}
