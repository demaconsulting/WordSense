namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for <see cref="InFlightTracker"/>, which defers the release of a shared resource until the last running
///     operation has finished. They need no model files.
/// </summary>
public class InFlightTrackerTests
{
    /// <summary>
    ///     Closing while no operation runs releases at once, and once only.
    /// </summary>
    [Fact]
    public void InFlightTracker_Close_Idle_ReleasesOnce()
    {
        var releases = 0;
        var tracker = new InFlightTracker("T", () => releases++);

        tracker.Close();
        tracker.Close();

        Assert.Equal(1, releases);
        Assert.True(tracker.Released);
    }

    /// <summary>
    ///     Closing while operations run defers the release to the last one that finishes.
    /// </summary>
    [Fact]
    public void InFlightTracker_Close_WithRunningOperations_ReleasesAfterLastExit()
    {
        var releases = 0;
        var tracker = new InFlightTracker("T", () => releases++);
        tracker.Enter();
        tracker.Enter();

        tracker.Close();
        Assert.Equal(0, releases);
        Assert.Equal(2, tracker.Active);

        tracker.Exit();
        Assert.Equal(0, releases);

        tracker.Exit();
        Assert.Equal(1, releases);
        Assert.Equal(0, tracker.Active);
    }

    /// <summary>
    ///     A closed tracker refuses new operations with the object name.
    /// </summary>
    [Fact]
    public void InFlightTracker_Enter_AfterClose_ThrowsObjectDisposed()
    {
        var tracker = new InFlightTracker("Thing", () => { });
        tracker.Close();

        var ex = Assert.Throws<ObjectDisposedException>(tracker.Enter);

        Assert.Equal("Thing", ex.ObjectName);
        Assert.Equal(0, tracker.Active);
    }

    /// <summary>
    ///     An operation that finishes without a close does not release anything.
    /// </summary>
    [Fact]
    public void InFlightTracker_Exit_WithoutClose_DoesNotRelease()
    {
        var releases = 0;
        var tracker = new InFlightTracker("T", () => releases++);

        tracker.Enter();
        tracker.Exit();

        Assert.Equal(0, releases);
        Assert.False(tracker.Released);
    }

    /// <summary>
    ///     An exit without an enter is a programming error.
    /// </summary>
    [Fact]
    public void InFlightTracker_Exit_WithoutEnter_Throws()
    {
        var tracker = new InFlightTracker("T", () => { });

        Assert.Throws<InvalidOperationException>(tracker.Exit);
    }

    /// <summary>
    ///     A close while an operation runs does not wait for it: it returns at once, and the operation then releases.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task InFlightTracker_Close_DoesNotWaitForRunningOperation()
    {
        var releases = 0;
        var tracker = new InFlightTracker("T", () => Interlocked.Increment(ref releases));
        var started = new ManualResetEventSlim();
        var gate = new ManualResetEventSlim();
        tracker.Enter();
        var worker = Task.Run(() =>
        {
            started.Set();
            gate.Wait(TestContext.Current.CancellationToken);
            tracker.Exit();
        }, TestContext.Current.CancellationToken);
        started.Wait(TestContext.Current.CancellationToken);

        tracker.Close();
        Assert.Equal(0, Volatile.Read(ref releases));

        gate.Set();
        await worker;
        Assert.Equal(1, Volatile.Read(ref releases));
    }

    /// <summary>
    ///     Many operations racing with a close release exactly once and never after an enter that succeeded.
    /// </summary>
    [Fact]
    public void InFlightTracker_ConcurrentEnterExitClose_ReleasesExactlyOnce()
    {
        var releases = 0;
        var usedAfterRelease = 0;
        var tracker = new InFlightTracker("T", () => Interlocked.Increment(ref releases));

        Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
        {
            if (i == 32)
            {
                tracker.Close();
                return;
            }

            try
            {
                tracker.Enter();
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (tracker.Released)
            {
                Interlocked.Increment(ref usedAfterRelease);
            }

            tracker.Exit();
        });

        tracker.Close();
        Assert.Equal(1, Volatile.Read(ref releases));
        Assert.Equal(0, usedAfterRelease);
        Assert.Equal(0, tracker.Active);
    }
}
