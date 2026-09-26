using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Services;

/// <summary>
/// Shared, thread-safe runtime state for the voice assistant: the current lifecycle state,
/// the token of the operation that currently owns the assistant, the response available for
/// repetition, and the command waiting for a confirmation answer.
/// <para>
/// This is a separate object from <see cref="VoiceAssistantService"/> on purpose. Handlers
/// need to inspect and steer the assistant, and routing that need through a dedicated session
/// is what keeps the dependency graph free of a cycle between the service and its own
/// handlers.
/// </para>
/// </summary>
public sealed class AssistantSession
{
    private readonly Lock _gate = new();

    private VoiceAssistantState _state = VoiceAssistantState.Idle;
    private CancellationTokenSource? _activeOperation;
    private string? _lastResponse;
    private VoiceCommand? _pendingCommand;

    /// <summary>Raised on every state transition.</summary>
    public event EventHandler<VoiceStateChangedEventArgs>? StateChanged;

    /// <summary>Gets the current lifecycle state.</summary>
    public VoiceAssistantState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>Gets the most recent response, or <see langword="null"/> when there is none.</summary>
    public string? LastResponse
    {
        get
        {
            lock (_gate)
            {
                return _lastResponse;
            }
        }
    }

    /// <summary>Gets the command held back awaiting a spoken confirmation, if any.</summary>
    public VoiceCommand? PendingCommand
    {
        get
        {
            lock (_gate)
            {
                return _pendingCommand;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether an operation currently owns the assistant. A second
    /// concurrent voice request is refused rather than interleaved.
    /// </summary>
    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                return _activeOperation is not null;
            }
        }
    }

    /// <summary>
    /// Gets a token that is cancelled when the user abandons the active operation. Callers
    /// link their own token to it so a spoken "cancel" interrupts a running AI request or
    /// search rather than waiting for it to finish.
    /// </summary>
    public CancellationToken ActiveOperationToken
    {
        get
        {
            lock (_gate)
            {
                return _activeOperation?.Token ?? CancellationToken.None;
            }
        }
    }

    /// <summary>Moves to a new state and notifies listeners when it actually changed.</summary>
    public void TransitionTo(VoiceAssistantState state)
    {
        VoiceAssistantState previous;

        lock (_gate)
        {
            if (_state == state)
            {
                return;
            }

            previous = _state;
            _state = state;
        }

        StateChanged?.Invoke(this, new VoiceStateChangedEventArgs(previous, state));
    }

    /// <summary>
    /// Claims exclusive ownership of the assistant for one operation.
    /// </summary>
    /// <param name="scope">
    /// Receives a disposable that releases the claim, or <see langword="null"/> when another
    /// operation already owns the assistant.
    /// </param>
    /// <returns><see langword="true"/> when the claim was granted.</returns>
    public bool TryBeginOperation(out IDisposable? scope)
    {
        lock (_gate)
        {
            if (_activeOperation is not null)
            {
                scope = null;
                return false;
            }

            var source = new CancellationTokenSource();
            _activeOperation = source;
            scope = new OperationScope(this, source);
            return true;
        }
    }

    /// <summary>Cancels the active operation, if there is one.</summary>
    public void CancelActiveOperation()
    {
        CancellationTokenSource? source;

        lock (_gate)
        {
            source = _activeOperation;
        }

        if (source is null || source.IsCancellationRequested)
        {
            return;
        }

        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The operation completed between the read and the cancel; nothing to do.
        }
    }

    /// <summary>Records a response so "repeat that" has something to say again.</summary>
    public void RememberResponse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return;
        }

        lock (_gate)
        {
            _lastResponse = response;
        }
    }

    /// <summary>Stores a command that is waiting for the user to confirm it.</summary>
    public void SetPendingCommand(VoiceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_gate)
        {
            _pendingCommand = command;
        }
    }

    /// <summary>Discards any command waiting for confirmation.</summary>
    public void ClearPendingCommand()
    {
        lock (_gate)
        {
            _pendingCommand = null;
        }
    }

    private void Release(CancellationTokenSource source)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_activeOperation, source))
            {
                _activeOperation = null;
            }
        }

        source.Dispose();
    }

    private sealed class OperationScope : IDisposable
    {
        private readonly AssistantSession _session;
        private readonly CancellationTokenSource _source;
        private bool _disposed;

        public OperationScope(AssistantSession session, CancellationTokenSource source)
        {
            _session = session;
            _source = source;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _session.Release(_source);
        }
    }
}
