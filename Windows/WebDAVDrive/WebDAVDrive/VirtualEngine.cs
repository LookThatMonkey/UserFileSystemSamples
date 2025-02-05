using System;
using System.Net;
using System.Threading.Tasks;
using System.Threading;

using ITHit.FileSystem;
using ITHit.FileSystem.Windows;
using ITHit.FileSystem.Samples.Common.Windows;


namespace WebDAVDrive
{
    /// <inheritdoc />
    public class VirtualEngine : VirtualEngineBase
    {
        /// <summary>
        /// Engine instance ID, unique for every Engine instance.
        /// </summary>
        /// <remarks>
        /// Used to prevent circular calls between remote storage and user file system.
        /// You can send this ID with every update to the remote storage. Your remote storage 
        /// will return this ID back to the client. If IDs match you do not update the item.
        /// </remarks>
        public readonly Guid InstanceId = Guid.NewGuid();

        /// <summary>
        /// Monitors changes in the remote storage, notifies the client and updates the user file system.
        /// </summary>
        public readonly RemoteStorageMonitor RemoteStorageMonitor;

        /// <summary>
        /// Performs complete remote storage scan for changes by comparing every item eTag.
        /// </summary>
        public readonly IncomingFullSync IncomingFullSync;

        /// <summary>
        /// Credentials used to connect to the server. 
        /// Used for challenge-responce auth (Basic, Digest, NTLM or Kerberos).
        /// </summary>
        public NetworkCredential Credentials { get; set; }

        /// <summary>
        /// Cookies used to connect to the server. 
        /// Used for cookies auth and MS-OFBA auth.
        /// </summary>
        public CookieCollection Cookies { get; set; } = new CookieCollection();

        /// <summary>
        /// Automatic lock timout in milliseconds.
        /// </summary>
        private readonly double autoLockTimoutMs;

        /// <summary>
        /// Manual lock timout in milliseconds.
        /// </summary>
        private readonly double manualLockTimoutMs;

        /// <summary>
        /// Creates a vitual file system Engine.
        /// </summary>
        /// <param name="license">A license string.</param>
        /// <param name="userFileSystemRootPath">
        /// A root folder of your user file system. 
        /// Your file system tree will be located under this folder.
        /// </param>
        /// <param name="remoteStorageRootPath">Path to the remote storage root.</param>
        /// <param name="webSocketServerUrl">Web sockets server that sends notifications about changes on the server.</param>
        /// <param name="iconsFolderPath">Path to the icons folder.</param>
        public VirtualEngine(
            string license, 
            string userFileSystemRootPath, 
            string remoteStorageRootPath, 
            string webSocketServerUrl, 
            string iconsFolderPath,
            double autoLockTimoutMs, 
            double manualLockTimoutMs,
            LogFormatter logFormatter)
            : base(license, userFileSystemRootPath, remoteStorageRootPath, iconsFolderPath, logFormatter)
        {
            RemoteStorageMonitor = new RemoteStorageMonitor(webSocketServerUrl, this);
            IncomingFullSync = new IncomingFullSync(this);

            this.SyncService.BeforeStateChanged += SyncService_BeforeStateChanged;
            this.autoLockTimoutMs = autoLockTimoutMs;
            this.manualLockTimoutMs = manualLockTimoutMs;
        }

        /// <inheritdoc/>
        public override async Task<IFileSystemItem> GetFileSystemItemAsync(string userFileSystemPath, FileSystemItemType itemType, byte[] itemId, ILogger logger = null)
        {
            if (itemType == FileSystemItemType.File)
            {
                return new VirtualFile(userFileSystemPath, this, autoLockTimoutMs, manualLockTimoutMs, logger);
            }
            else
            {
                return new VirtualFolder(userFileSystemPath, this, autoLockTimoutMs, manualLockTimoutMs, logger);
            }
        }

        private async void SyncService_BeforeStateChanged(object sender, SynchEventArgs synchEventArgs)
        {
            if (synchEventArgs.NewState == SynchronizationState.Idle)
            {
                await IncomingFullSync.TrySyncronizeAsync();
            }
        }

        /// <inheritdoc/>
        public override async Task<IMenuCommand> GetMenuCommandAsync(Guid menuGuid)
        {
            // For this method to be called you need to register a menu command handler.
            // See method description for more details.

            Logger.LogDebug($"{nameof(IEngine)}.{nameof(GetMenuCommandAsync)}()", menuGuid.ToString());

            Guid menuCommandLockGuid = typeof(WebDAVDrive.ShellExtension.ContextMenuVerbIntegrated).GUID;

            if (menuGuid == menuCommandLockGuid)
            {
                return new MenuCommandLock(this, this.Logger);
            }

            Logger.LogError($"Menu not found", menuGuid.ToString());
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public override async Task StartAsync(bool processModified = true, CancellationToken cancellationToken = default)
        {
            await base.StartAsync(processModified, cancellationToken);
            await RemoteStorageMonitor.StartAsync();
            await IncomingFullSync.StartAsync();
        }

        public override async Task StopAsync()
        {
            await base.StopAsync();
            await RemoteStorageMonitor.StopAsync();
            await IncomingFullSync.StopAsync();
        }

        private bool disposedValue;

        protected override void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    RemoteStorageMonitor.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
            base.Dispose(disposing);
        }
    }
}
