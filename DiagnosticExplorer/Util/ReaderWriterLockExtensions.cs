using System;
using System.Threading;

namespace DiagnosticExplorer.Util;

internal static class ReaderWriterLockExtensions
{

    public static IDisposable ReadGuard(this ReaderWriterLockSlim lockSlim)
    {
        lockSlim.EnterReadLock();
        return new ExitRead(lockSlim);
    }

    public static IDisposable WriteGuard(this ReaderWriterLockSlim lockSlim)
    {
        lockSlim.EnterWriteLock();
        return new ExitWrite(lockSlim);
    }

    public static IDisposable UpgradeableReadGuard(this ReaderWriterLockSlim lockSlim)
    {
        lockSlim.EnterUpgradeableReadLock();
        return new ExitupgradeableRead(lockSlim);
    }

    #region ExitRead

    private struct ExitRead : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock;
        private bool _isDisposed;

        public ExitRead(ReaderWriterLockSlim lockSlim)
        {
            _lock = lockSlim;
            _isDisposed = false;
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _lock.ExitReadLock();
            }
        }
    }

    #endregion

    #region ExitWrite

    private struct ExitWrite : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock;
        private bool _isDisposed;

        public ExitWrite(ReaderWriterLockSlim lockSlim)
        {
            _lock = lockSlim;
            _isDisposed = false;
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _lock.ExitWriteLock();
            }
        }
    }

    #endregion

    #region ExitWrite

    private struct ExitupgradeableRead : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock;
        private bool _isDisposed;

        public ExitupgradeableRead(ReaderWriterLockSlim lockSlim)
        {
            _lock = lockSlim;
            _isDisposed = false;
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _lock.ExitUpgradeableReadLock();
            }
        }
    }

    #endregion


}