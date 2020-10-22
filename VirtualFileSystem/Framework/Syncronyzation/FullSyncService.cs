﻿using ITHit.FileSystem;
using ITHit.FileSystem.Windows;
using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Provider;

namespace VirtualFileSystem.Syncronyzation
{

    // 4194304    (0x400000)	FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS        (M)
    // 4096 	    (0x1000) 	FILE_ATTRIBUTE_OFFLINE                      (O)
    // 1024 	     (0x400)  	FILE_ATTRIBUTE_REPARSE_POINT                (L)
    // 16   	      (0x10)   	FILE_ATTRIBUTE_DIRECTORY                    (D)
    //          (0x00080000)    FILE_ATTRIBUTE_PINNED                       (P)
    //          (0x00100000)    FILE_ATTRIBUTE_UNPINNED                     (U)
    // 32             (0x20)    FILE_ATTRIBUTE_ARCHIVE                      (A)
    // 512           (0x200)    FILE_ATTRIBUTE_SPARSE_FILE

    [Flags]
    public enum FileAttributesExt
    {
        Pinned      = 0x00080000,
        Unpinned    = 0x00100000,
        Offline     = 0x1000
    }

    /// <summary>
    /// Doing full synchronization between client and server.
    /// </summary>
    /// <remarks>
    /// This is a simple full synchronyzation example. 
    /// </remarks>
    internal class FullSyncService : Logger, IDisposable
    {
        System.Timers.Timer timer = null;

        /// <summary>
        /// User file system path.
        /// </summary>
        private string userFileSystemRootPath;

        /// <summary>
        /// Creates instance of this class.
        /// </summary>
        /// <param name="syncIntervalMs">Synchronization interval in milliseconds.</param>
        /// <param name="userFileSystemRootPath">User file system root path.</param>
        /// <param name="log">Logger.</param>
        internal FullSyncService(double syncIntervalMs, string userFileSystemRootPath, ILog log) : base("Sync Service", log)
        {
            timer = new System.Timers.Timer(syncIntervalMs);
            this.userFileSystemRootPath = userFileSystemRootPath;
        }

        /// <summary>
        /// Starts synchronization.
        /// </summary>
        internal async Task StartAsync()
        {
            timer.Elapsed += Timer_ElapsedAsync;

            // Do not start next synchronyzation automatically, wait until previous synchronyzation completed.
            timer.AutoReset = false; 
            timer.Start();

            LogMessage($"Started");
        }

        private async void Timer_ElapsedAsync(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                // UFS -> RS. Recursivery synchronize all moved files and folders present in the user file system. Restore OriginalPath.
                await new ClientToServerSync(Log).SyncronizeMovedAsync(userFileSystemRootPath);

                // UFS -> RS. Recursivery synchronize all updated/created/deleted file and folders present in the user file system.
                await new ClientToServerSync(Log).SyncronizeFolderAsync(userFileSystemRootPath);

                // UFS <- RS. Recursivery synchronize all updated/created/deleted file and folders present in the user file system.
                await new ServerToClientSync(Log).SyncronizeFolderAsync(userFileSystemRootPath);

                // Wait and than start synchronyzation again.
                timer.Start();
            }
            catch(Exception ex)
            {
                LogError("Timer failure:", null, null, ex);
                throw;
            }
        }

        private void Error(object sender, ErrorEventArgs e)
        {
            LogError(null, null, null, e.GetException());
        }

        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    timer.Stop();
                    timer.Dispose();
                    LogMessage($"Disposed");
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~SyncService()
        // {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }

    }
}
