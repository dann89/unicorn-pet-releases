using System.Threading;

namespace UnicornPet.App
{
    public static class SingleInstance
    {
        static Mutex _mutex;

        public static void Release()
        {
            try { _mutex?.ReleaseMutex(); _mutex?.Dispose(); } catch { }
            _mutex = null;
        }

        public static bool TryAcquire(string name)
        {
            bool created;
            _mutex = new Mutex(true, name, out created);
            if (!created) { _mutex.Dispose(); _mutex = null; }
            return created;
        }
    }
}

