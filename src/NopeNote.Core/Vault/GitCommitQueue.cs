using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NopeNote.Core.Vault;

/// <summary>
/// Runs git commits on a background pump so vault operations never wait for a
/// git process. Structural messages keep their own commit; update messages
/// share one commit inside the debounce window.
/// </summary>
public sealed class GitCommitQueue
{
    private readonly IVaultGitCommitter _committer;
    private readonly TimeSpan _debounce;
    private readonly Action<Exception>? _onCommitFailed;
    private readonly object _gate = new();
    private readonly Queue<string> _pending = new();
    private readonly SemaphoreSlim _signal = new(0);

    private string? _pendingUpdate;
    private CancellationTokenSource? _updateTimer;
    private TaskCompletionSource<bool> _idle;
    private bool _committing;

    public GitCommitQueue(
        IVaultGitCommitter committer,
        TimeSpan debounce,
        Action<Exception>? onCommitFailed = null)
    {
        _committer = committer;
        _debounce = debounce;
        _onCommitFailed = onCommitFailed;
        _idle = CompletedIdle();
        _ = Task.Run(PumpAsync);
    }

    /// <summary>True while a commit is queued, pending, or running.</summary>
    public bool HasPendingCommits
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count > 0 || _pendingUpdate is not null || _committing;
            }
        }
    }

    /// <summary>Queues a commit that must stand on its own, after any pending update.</summary>
    public void Enqueue(string message)
    {
        lock (_gate)
        {
            FlushUpdateLocked();
            _pending.Enqueue(message);
        }

        _signal.Release();
    }

    /// <summary>Queues an update commit; later calls inside the window replace it.</summary>
    public void ScheduleUpdate(string message)
    {
        CancellationTokenSource timer;
        lock (_gate)
        {
            _pendingUpdate = message;
            _updateTimer?.Cancel();
            timer = new CancellationTokenSource();
            _updateTimer = timer;
        }

        _ = RunDebounceAsync(timer);
    }

    /// <summary>Waits until every queued commit has been attempted.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task idleTask;
            lock (_gate)
            {
                FlushUpdateLocked();
                if (_pending.Count == 0 && !_committing)
                {
                    return;
                }

                _idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                idleTask = _idle.Task;
            }

            _signal.Release();
            await idleTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            await _signal.WaitAsync().ConfigureAwait(false);

            while (TakeNext(out string message))
            {
                try
                {
                    await _committer.CommitAsync(message).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _onCommitFailed?.Invoke(exception);
                }

                lock (_gate)
                {
                    _committing = false;
                    if (_pending.Count == 0)
                    {
                        _idle.TrySetResult(true);
                    }
                }
            }

            lock (_gate)
            {
                if (_pending.Count == 0 && !_committing)
                {
                    _idle.TrySetResult(true);
                }
            }
        }
    }

    private bool TakeNext(out string message)
    {
        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                message = string.Empty;
                return false;
            }

            message = _pending.Dequeue();
            _committing = true;
            return true;
        }
    }

    private async Task RunDebounceAsync(CancellationTokenSource timer)
    {
        try
        {
            if (_debounce > TimeSpan.Zero)
            {
                await Task.Delay(_debounce, timer.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        bool ready;
        lock (_gate)
        {
            ready = ReferenceEquals(_updateTimer, timer);
            if (ready)
            {
                _updateTimer = null;
                if (_pendingUpdate is not null)
                {
                    _pending.Enqueue(_pendingUpdate);
                    _pendingUpdate = null;
                }
            }
        }

        if (ready)
        {
            _signal.Release();
        }
    }

    private void FlushUpdateLocked()
    {
        if (_updateTimer is not null)
        {
            _updateTimer.Cancel();
            _updateTimer = null;
        }

        if (_pendingUpdate is not null)
        {
            _pending.Enqueue(_pendingUpdate);
            _pendingUpdate = null;
        }
    }

    private static TaskCompletionSource<bool> CompletedIdle()
    {
        var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult(true);
        return source;
    }
}
