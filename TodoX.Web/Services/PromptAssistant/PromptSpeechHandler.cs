using Microsoft.JSInterop;

namespace TodoX.Web.Services.PromptAssistant;

/// <summary>
/// Shared browser Speech-to-Text handler (RVID-UI-002/003).
/// Single JS module (todoXSpeechInput), lang vi-VN, final-transcript-only,
/// reused by both main textarea mic and fullscreen dialog mic.
/// </summary>
public sealed class PromptSpeechHandler : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private readonly string _recognizerId;
    private readonly Func<string, Task> _onTranscript;
    private readonly Func<Task>? _onStateChanged;
    private readonly bool _wantsInterim;
    private DotNetObjectReference<PromptSpeechHandler>? _dotNetRef;
    private bool _disposed;
    private string _lastTranscript = string.Empty;

    public bool IsSupported { get; private set; }
    public PromptSpeechState State { get; private set; } = PromptSpeechState.Idle;
    public string? ErrorMessage { get; private set; }
    /// <summary>Live text being recognized (preview only, cleared once it becomes final).</summary>
    public string InterimTranscript { get; private set; } = string.Empty;

    public PromptSpeechHandler(IJSRuntime js, string recognizerId, Func<string, Task> onTranscript, Func<Task>? onStateChanged = null, bool interimPreview = false)
    {
        _wantsInterim = interimPreview;
        _js = js;
        _recognizerId = recognizerId;
        _onTranscript = onTranscript;
        _onStateChanged = onStateChanged;
    }

    public async Task InitializeAsync()
    {
        try { IsSupported = await _js.InvokeAsync<bool>("todoXSpeechInput.isSupported"); }
        catch { IsSupported = false; }
        if (_onStateChanged is not null) await _onStateChanged();
    }

    public async Task ToggleAsync()
    {
        if (State == PromptSpeechState.Listening)
        {
            await StopAsync();
            if (_onStateChanged is not null) await _onStateChanged();
            return;
        }
        if (State == PromptSpeechState.Error)
        {
            State = PromptSpeechState.Idle;
            ErrorMessage = null;
        }
        State = PromptSpeechState.RequestingPermission;
        if (_onStateChanged is not null) await _onStateChanged();
        try
        {
            _dotNetRef ??= DotNetObjectReference.Create(this);
            var started = await _js.InvokeAsync<bool>("todoXSpeechInput.start",
                _recognizerId, "vi-VN", _dotNetRef,
                nameof(OnSpeechFinalTranscriptAsync),
                nameof(OnSpeechErrorAsync),
                nameof(OnSpeechEndAsync),
                _wantsInterim ? nameof(OnSpeechInterimAsync) : null);
            if (started)
            {
                ErrorMessage = null;
                State = PromptSpeechState.Listening;
                _lastTranscript = string.Empty;
            }
            else
            {
                try { IsSupported = await _js.InvokeAsync<bool>("todoXSpeechInput.isSupported"); } catch { }
                State = PromptSpeechState.Idle;
                if (!IsSupported) ErrorMessage = "Trình duyệt hiện tại chưa hỗ trợ nhập nội dung bằng giọng nói.";
            }
        }
        catch { State = PromptSpeechState.Idle; }
        if (_onStateChanged is not null) await _onStateChanged();
    }

    public async Task StopAsync()
    {
        try { await _js.InvokeVoidAsync("todoXSpeechInput.stop", _recognizerId); } catch { }
        State = PromptSpeechState.Idle;
        _lastTranscript = string.Empty;
    }

    [JSInvokable]
    public async Task OnSpeechFinalTranscriptAsync(string transcript)
    {
        var trimmed = transcript?.Trim();
        if (string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, _lastTranscript, StringComparison.Ordinal)) return;
        _lastTranscript = trimmed;
        InterimTranscript = string.Empty;
        await _onTranscript(trimmed);
        if (_onStateChanged is not null) await _onStateChanged();
    }

    [JSInvokable]
    public async Task OnSpeechInterimAsync(string transcript)
    {
        InterimTranscript = transcript?.Trim() ?? string.Empty;
        if (_onStateChanged is not null) await _onStateChanged();
    }

    [JSInvokable]
    public async Task OnSpeechErrorAsync(string errorKind, string _)
    {
        _lastTranscript = string.Empty;
        State = PromptSpeechState.Idle;
        InterimTranscript = string.Empty;
        ErrorMessage = errorKind switch
        {
            "not-allowed" or "service-not-allowed" => "Không thể truy cập microphone. Vui lòng cấp quyền microphone cho trình duyệt và thử lại.",
            "no-speech" => "Không nghe thấy giọng nói. Hãy nói gần micro hơn rồi thử lại.",
            "audio-capture" => "Không tìm thấy microphone trên thiết bị.",
            "network" => "Nhận dạng giọng nói cần kết nối mạng. Vui lòng kiểm tra mạng và thử lại.",
            "aborted" => null,
            _ => "Nhập nội dung bằng giọng nói gặp lỗi. Vui lòng thử lại."
        };
        if (_onStateChanged is not null) await _onStateChanged();
    }

    [JSInvokable]
    public async Task OnSpeechEndAsync()
    {
        if (State == PromptSpeechState.Listening) State = PromptSpeechState.Idle;
        _lastTranscript = string.Empty;
        InterimTranscript = string.Empty;
        if (_onStateChanged is not null) await _onStateChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try { await _js.InvokeVoidAsync("todoXSpeechInput.dispose", _recognizerId); } catch { }
        _dotNetRef?.Dispose();
    }
}

public enum PromptSpeechState { Idle, RequestingPermission, Listening, Error }
