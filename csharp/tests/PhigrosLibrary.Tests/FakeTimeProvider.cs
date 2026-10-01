namespace PhigrosLibrary.Tests;

/// <summary>
/// 可手动控制的时间源，并且让 <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/>
/// 立即返回，避免登录流程的轮询测试真的去等秒级间隔。
/// </summary>
internal sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <summary>当前时间，可写。</summary>
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => new ImmediateTimer(callback, state);

    private sealed class ImmediateTimer(TimerCallback callback, object? state) : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return true;
        }

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
