namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     Counts the operations that are using a shared resource and releases the resource only after the owner has closed
///     it and the last operation has finished.
/// </summary>
/// <remarks>
///     An operation calls <see cref="Enter"/> before it touches the resource and <see cref="Exit"/> when it is done;
///     the owner calls <see cref="Close"/> when it is disposed. Once closed, <see cref="Enter"/> throws, so no new
///     operation starts, while the operations already running complete undisturbed. The release action runs exactly
///     once, outside the internal lock, on the thread that closed the tracker (when no operation was running) or on
///     the thread of the last operation to finish. <see cref="Close"/> never waits for running operations.
/// </remarks>
internal sealed class InFlightTracker
{
    private readonly object _lock = new();
    private readonly string _objectName;
    private readonly Action _release;
    private int _active;
    private bool _closed;
    private bool _released;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InFlightTracker"/> class.
    /// </summary>
    /// <param name="objectName">The name reported by the <see cref="ObjectDisposedException"/> of <see cref="Enter"/>.</param>
    /// <param name="release">The action that releases the resource; it runs once.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public InFlightTracker(string objectName, Action release)
    {
        ArgumentNullException.ThrowIfNull(objectName);
        ArgumentNullException.ThrowIfNull(release);
        _objectName = objectName;
        _release = release;
    }

    /// <summary>
    ///     Gets the number of operations between <see cref="Enter"/> and <see cref="Exit"/>.
    /// </summary>
    public int Active
    {
        get
        {
            lock (_lock)
            {
                return _active;
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the release action has run.
    /// </summary>
    public bool Released
    {
        get
        {
            lock (_lock)
            {
                return _released;
            }
        }
    }

    /// <summary>
    ///     Registers an operation that is about to use the resource.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the tracker has been closed.</exception>
    public void Enter()
    {
        lock (_lock)
        {
            if (_closed)
            {
                throw new ObjectDisposedException(_objectName);
            }
            _active++;
        }
    }

    /// <summary>
    ///     Unregisters an operation; the last one to finish after <see cref="Close"/> releases the resource.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when no operation is registered.</exception>
    public void Exit()
    {
        bool release;
        lock (_lock)
        {
            if (_active == 0)
            {
                throw new InvalidOperationException("No operation is registered.");
            }

            _active--;
            release = _closed && _active == 0 && !_released;
            _released |= release;
        }

        if (release)
        {
            _release();
        }
    }

    /// <summary>
    ///     Closes the tracker: no operation can start afterwards. The resource is released now when no operation is
    ///     running, otherwise when the last one finishes. Calling it again is harmless.
    /// </summary>
    public void Close()
    {
        bool release;
        lock (_lock)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            release = _active == 0;
            _released |= release;
        }

        if (release)
        {
            _release();
        }
    }
}
