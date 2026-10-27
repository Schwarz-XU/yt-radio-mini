using YTRadioMini.Services;

namespace YTRadioMini.Tests;

public class SingleInstanceGuardTests
{
    private static string UniqueName() => "ytradio-test-" + Guid.NewGuid().ToString("N");

    // 命名互斥体归线程所有，同一线程可以重复获取。真实场景里两个实例在不同进程，
    // 所以测试里让"第二个实例"在另一个线程上尝试获取。
    private static bool AcquireElsewhere(SingleInstanceGuard guard) =>
        Task.Factory.StartNew(guard.TryAcquire, TaskCreationOptions.LongRunning).GetAwaiter().GetResult();

    [Fact]
    public void OnlyTheFirstInstanceAcquires()
    {
        var name = UniqueName();
        using var first = new SingleInstanceGuard(name);
        using var second = new SingleInstanceGuard(name);

        Assert.True(first.TryAcquire());
        Assert.False(AcquireElsewhere(second));
    }

    [Fact]
    public void DifferentNames_DoNotInterfere()
    {
        using var a = new SingleInstanceGuard(UniqueName());
        using var b = new SingleInstanceGuard(UniqueName());

        Assert.True(a.TryAcquire());
        Assert.True(b.TryAcquire());
    }

    [Fact]
    public async Task SecondInstance_CanAskTheFirstToActivate()
    {
        var name = UniqueName();
        using var first = new SingleInstanceGuard(name);
        var activated = new TaskCompletionSource();
        first.ActivationRequested += () => activated.TrySetResult();
        Assert.True(first.TryAcquire());

        using var second = new SingleInstanceGuard(name);
        Assert.False(AcquireElsewhere(second));
        second.SignalExistingInstance();

        await activated.Task.WithTimeout(5);
    }

    [Fact]
    public async Task SeveralRequests_AreAllDelivered_AfterEachOther()
    {
        var name = UniqueName();
        using var first = new SingleInstanceGuard(name);
        var count = 0;
        first.ActivationRequested += () => Interlocked.Increment(ref count);
        Assert.True(first.TryAcquire());

        using var second = new SingleInstanceGuard(name);
        AcquireElsewhere(second);
        for (var i = 0; i < 3; i++)
        {
            var before = Volatile.Read(ref count);
            second.SignalExistingInstance();
            await TestSupport.WaitUntil(() => Volatile.Read(ref count) > before, 5, "activation delivered");
        }
        Assert.Equal(3, count);
    }

    [Fact]
    public void ThrowingSubscriber_DoesNotStopTheListener()
    {
        var name = UniqueName();
        using var first = new SingleInstanceGuard(name);
        var calls = 0;
        first.ActivationRequested += () => { Interlocked.Increment(ref calls); throw new InvalidOperationException("boom"); };
        first.TryAcquire();

        using var second = new SingleInstanceGuard(name);
        second.SignalExistingInstance();
        TestSupport.WaitUntil(() => Volatile.Read(ref calls) == 1).GetAwaiter().GetResult();
        second.SignalExistingInstance();
        TestSupport.WaitUntil(() => Volatile.Read(ref calls) == 2).GetAwaiter().GetResult();
    }

    [Fact]
    public void AfterTheFirstInstanceIsDisposed_ANewOneCanAcquire()
    {
        var name = UniqueName();
        var first = new SingleInstanceGuard(name);
        Assert.True(first.TryAcquire());
        first.Dispose();

        using var next = new SingleInstanceGuard(name);
        Assert.True(AcquireElsewhere(next));
    }
}
