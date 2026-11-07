using System.IO;
using System.Threading;

namespace Octgn.Library
{
    public static class SavedPasswordManager
    {
        private static ReaderWriterLockSlim _locker = new ReaderWriterLockSlim();
        public static string FileName = "Password.dat";
        public static void SavePassword(string pass)
        {
            var path = Path.Combine(Config.Instance.Paths.ConfigDirectory, FileName);
            _locker.EnterWriteLock();
            try
            {
                File.WriteAllText(path, pass);
            }
            finally
            {
                _locker.ExitWriteLock();
            }
        }

        public static string GetPassword()
        {
            var path = Path.Combine(Config.Instance.Paths.ConfigDirectory, FileName);
            _locker.EnterReadLock();
            try
            {
                if (!File.Exists(path))
                    return "";
                return File.ReadAllText(path);
            }
            finally
            {
                _locker.ExitReadLock();
            }
        }
    }
}
