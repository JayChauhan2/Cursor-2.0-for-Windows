using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace Cursor2Windows;

public sealed class GlobalInputManager : IDisposable
{
    private readonly OverlayState _state;
    private readonly VoicePromptManager _voice;
    private readonly Dispatcher _dispatcher;
    private readonly Action<int, int> _moveOverlay;
    private readonly Action _quit;
    private readonly NativeMethods.HookProc _mouseProc;
    private readonly NativeMethods.HookProc _keyboardProc;
    private readonly List<double> _history = new();
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private int? _lastX;
    private int _lastDx;

    public GlobalInputManager(OverlayState state, VoicePromptManager voice, Dispatcher dispatcher, Action<int, int> moveOverlay, Action quit)
    {
        _state = state;
        _voice = voice;
        _dispatcher = dispatcher;
        _moveOverlay = moveOverlay;
        _quit = quit;
        _mouseProc = MouseHook;
        _keyboardProc = KeyboardHook;
    }

    public void Start()
    {
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var handle = NativeMethods.GetModuleHandle(module?.ModuleName);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, handle, 0);
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, handle, 0);
    }

    private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == NativeMethods.WM_MOUSEMOVE)
        {
            var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            _moveOverlay(data.pt.X, data.pt.Y);
            DetectWiggle(data.pt.X);
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void DetectWiggle(int x)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        if (_lastX.HasValue)
        {
            var dx = x - _lastX.Value;
            if (Math.Abs(dx) > 4)
            {
                if ((dx > 0 && _lastDx < 0) || (dx < 0 && _lastDx > 0))
                {
                    _history.Add(now);
                }
                _lastDx = dx;
            }
        }

        _lastX = x;
        _history.RemoveAll(t => now - t > .7);
        if (_history.Count >= 3)
        {
            _history.Clear();
            _dispatcher.Invoke(() =>
            {
                _state.CursorColor = OverlayState.Palette[Random.Shared.Next(OverlayState.Palette.Length)];
                _voice.Toggle();
            });
        }
    }

    private IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || (wParam.ToInt32() != NativeMethods.WM_KEYDOWN && wParam.ToInt32() != NativeMethods.WM_SYSKEYDOWN))
        {
            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
        var key = (int)data.vkCode;
        if (IsGlobalQuitChord(key))
        {
            _dispatcher.Invoke(_quit);
            return (IntPtr)1;
        }

        if (!_state.IsVisible)
        {
            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        _dispatcher.Invoke(() => HandleKey(key, data.scanCode));
        return (IntPtr)1;
    }

    private static bool IsGlobalQuitChord(int key)
    {
        var ctrl = (NativeMethods.GetAsyncKeyState(0x11) & 0x8000) != 0;
        var alt = (NativeMethods.GetAsyncKeyState(0x12) & 0x8000) != 0;
        return ctrl && alt && key == 0x51;
    }

    private void HandleKey(int key, uint scanCode)
    {
        if (_state.ShowsTetris)
        {
            if (key == 0x25) MiniTetrisView.Post(MiniTetrisAction.Left);
            else if (key == 0x27) MiniTetrisView.Post(MiniTetrisAction.Right);
            else if (key == 0x28) MiniTetrisView.Post(MiniTetrisAction.Down);
            else if (key == 0x26) MiniTetrisView.Post(MiniTetrisAction.Rotate);
            else if (key == 0x20 || key == 0x0D) MiniTetrisView.Post(MiniTetrisAction.Drop);
            else if (key == 0x52) MiniTetrisView.Post(MiniTetrisAction.Reset);
            else if (key == 0x1B) _voice.ExitTetrisToPrompt();
            return;
        }

        if (key == 0x0D)
        {
            var shift = (NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0;
            if (shift) _state.UserText += "\n";
            else
            {
                var text = _state.UserText.Trim();
                if (text.Length > 0)
                {
                    _state.UserText = "";
                    _voice.ProcessTypedCommand(text);
                }
                else _voice.HandleEmptyEnter();
            }
            _state.Notify();
            return;
        }

        if (key == 0x08)
        {
            if (_state.UserText.Length > 0) _state.UserText = _state.UserText[..^1];
            _state.Notify();
            return;
        }

        if (key == 0x1B)
        {
            _voice.Toggle();
            return;
        }

        var chars = TranslateKey((uint)key, scanCode);
        if (!string.IsNullOrEmpty(chars) && !char.IsControl(chars[0]))
        {
            _voice.StopRecordingForTyping();
            _state.UserText += chars;
            _state.Notify();
        }
    }

    private static string TranslateKey(uint key, uint scanCode)
    {
        var state = new byte[256];
        NativeMethods.GetKeyboardState(state);
        var buffer = new StringBuilder(8);
        var rc = NativeMethods.ToUnicode(key, scanCode, state, buffer, buffer.Capacity, 0);
        return rc > 0 ? buffer.ToString(0, rc) : "";
    }

    public void Dispose()
    {
        if (_mouseHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_mouseHook);
        if (_keyboardHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_keyboardHook);
    }
}
