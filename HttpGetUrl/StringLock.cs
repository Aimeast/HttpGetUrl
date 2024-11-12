using System.Collections.Concurrent;

namespace HttpGetUrl;

public static class StringLock
{
    private static readonly ConcurrentDictionary<string, StringLockTuple> _lockDictionary = new();

    public static void LockString(string str)
    {
        StringLockTuple tuple;
        lock (_lockDictionary)
        {
            tuple = _lockDictionary.GetOrAdd(str, _ => new StringLockTuple());
            Interlocked.Increment(ref tuple.Count);
        }
        tuple.Semaphore.Wait();
    }

    public static void ReleaseString(string str)
    {
        StringLockTuple tuple;
        lock (_lockDictionary)
            if (_lockDictionary.TryGetValue(str, out tuple))
            {
                if (Interlocked.Decrement(ref tuple.Count) == 0)
                    _lockDictionary.TryRemove(str, out _);
            }
        tuple.Semaphore.Release();
    }

    private class StringLockTuple
    {
        public SemaphoreSlim Semaphore = new(1, 1);
        public int Count = 0;
    }
}
