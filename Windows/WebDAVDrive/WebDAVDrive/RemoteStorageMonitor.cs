using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using ITHit.FileSystem;
using ITHit.FileSystem.Samples.Common;
using ITHit.FileSystem.Samples.Common.Windows;
using ITHit.FileSystem.Windows;
using ITHit.WebDAV.Client;


namespace WebDAVDrive
{
    /// <summary>
    /// Monitors changes in the remote storage, notifies the client and updates the user file system.
    /// If any file or folder is created, updated, delated, moved, locked or unlocked in the remote storage, 
    /// triggers an event with information about changes being made.
    /// </summary>
    public class RemoteStorageMonitor : IncomingServerNotifications, ISyncService, IDisposable
    {
        /// <summary>
        /// Virtual drive.
        /// </summary>
        private new readonly VirtualEngine Engine;

        /// <summary>
        /// Current synchronization state.
        /// </summary>
        public virtual SynchronizationState SyncState
        {
            get
            {
                return
                (clientWebSocket != null
                    && (clientWebSocket?.State == WebSocketState.Open || clientWebSocket?.State == WebSocketState.CloseSent || clientWebSocket?.State == WebSocketState.CloseReceived))
                    ? SynchronizationState.Enabled : SynchronizationState.Disabled;
            }
        }

        /// <summary>
        /// WebSocket client.
        /// </summary>
        private ClientWebSocket clientWebSocket;

        /// <summary>
        /// WebSocket server url.
        /// </summary>
        private readonly string webSocketServerUrl;

        /// <summary>
        /// WebSocket cancellation token.
        /// </summary>
        private CancellationTokenSource cancellationTokenSource;

        /// <summary>
        /// Creates instance of this class.
        /// </summary>
        /// <param name="webSocketServerUrl">WebSocket server url.</param>
        /// <param name="engine">Engine to send notifications about changes in the remote storage.</param>
        internal RemoteStorageMonitor(string webSocketServerUrl, VirtualEngine engine)
            : base(engine, engine.Logger.CreateLogger("Remote Storage Monitor"))
        {
            this.Engine = engine;
            this.webSocketServerUrl = webSocketServerUrl;
        }

        /// <summary>
        /// Starts monitoring changes in the remote storage.
        /// </summary>
        private async Task StartMonitoringAsync(NetworkCredential credentials, CookieCollection cookies, Guid thisInstanceId)
        {
            cancellationTokenSource = new CancellationTokenSource();
            clientWebSocket = new ClientWebSocket();
            clientWebSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
            clientWebSocket.Options.Credentials = credentials;
            clientWebSocket.Options.Cookies = new CookieContainer();
            clientWebSocket.Options.Cookies.Add(cookies);
            clientWebSocket.Options.SetRequestHeader("InstanceId", thisInstanceId.ToString());

            await clientWebSocket.ConnectAsync(new Uri(webSocketServerUrl), CancellationToken.None);

            Logger.LogMessage("Started", webSocketServerUrl);

            var rcvBuffer = new ArraySegment<byte>(new byte[2048]);
            while (true)
            {
                WebSocketReceiveResult rcvResult = await clientWebSocket.ReceiveAsync(rcvBuffer, cancellationTokenSource.Token);
                byte[] msgBytes = rcvBuffer.Skip(rcvBuffer.Offset).Take(rcvResult.Count).ToArray();
                string rcvMsg = Encoding.UTF8.GetString(msgBytes);
                await ProcessAsync(rcvMsg);
            }
        }

        /// <summary>
        /// Starts websockets to monitor changes in remote storage.
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            await Task.Factory.StartNew(
              async () =>
              {
                  bool repeat = false;
                  do
                  {
                      try
                      {
                          repeat = false;
                          await StartMonitoringAsync(Engine.Credentials, Engine.Cookies, Engine.InstanceId);
                      }
                      catch (Exception e) when (e is WebSocketException || e is AggregateException)
                      {
                          // Start socket after first success WebDAV PROPFIND. Restart socket when it disconnects.
                          if (clientWebSocket != null && clientWebSocket?.State != WebSocketState.Closed)
                          {
                              Logger.LogError(e.Message, webSocketServerUrl);
                          }

                          // Delay websocket connect to not overload it on network disappear.
                          await Task.Delay(TimeSpan.FromSeconds(2));
                          repeat = true;
                      };
                  } while (repeat);
              }, new CancellationTokenSource().Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        /// <summary>
        /// Stops monitoring changes in the remote storage.
        /// </summary>
        public async Task StopAsync()
        {
            try
            {
                cancellationTokenSource?.Cancel();
                if (clientWebSocket != null
                    && (clientWebSocket?.State == WebSocketState.Open || clientWebSocket?.State == WebSocketState.CloseSent || clientWebSocket?.State == WebSocketState.CloseReceived))
                {
                    await clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                }
            }
            catch (WebSocketException ex)
            {
                Logger.LogError("Failed to close websocket.", webSocketServerUrl, null, ex);
            };

            Logger.LogMessage("Stoped", webSocketServerUrl);
        }

        /// <summary>
        /// Process json string from websocket with update in the remote storage.
        /// </summary>
        internal async Task ProcessAsync(string jsonString)
        {
            WebSocketMessage jsonMessage = JsonSerializer.Deserialize<WebSocketMessage>(jsonString, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            string remoteStoragePath = Mapping.GetAbsoluteUri(jsonMessage.ItemPath);

            // Check if remote URL starts with WebDAVServerUrl.
            if (remoteStoragePath.StartsWith(Program.Settings.WebDAVServerUrl, StringComparison.InvariantCultureIgnoreCase))
            {
                Logger.LogDebug($"EventType: {jsonMessage.EventType}", jsonMessage.ItemPath, jsonMessage.TargetPath);
                switch (jsonMessage.EventType)
                {
                    case "created":
                        await CreatedAsync(remoteStoragePath);
                        break;
                    case "updated":
                        await ChangedAsync(remoteStoragePath);
                        break;
                    case "moved":
                        string remoteStorageNewPath = Mapping.GetAbsoluteUri(jsonMessage.TargetPath);
                        await MovedAsync(remoteStoragePath, remoteStorageNewPath);
                        break;
                    case "deleted":
                        await DeletedAsync(remoteStoragePath);
                        break;
                    case "locked":
                        await LockedAsync(remoteStoragePath);
                        break;
                    case "unlocked":
                        await UnlockedAsync(remoteStoragePath);
                        break;
                }
            }
        }

        /// <summary>
        /// Called when a file or folder is created in the remote storage.
        /// </summary>
        /// <param name="remoteStoragePath">Path in the remote storage.</param>
        /// <remarks>In this method we create a new file/folder in the user file system.</remarks>
        private async Task CreatedAsync(string remoteStoragePath)
        {
            try
            {
                string userFileSystemPath = Mapping.ReverseMapPath(remoteStoragePath);
                string userFileSystemParentPath = Path.GetDirectoryName(userFileSystemPath);

                // Because of the on-demand population the file or folder placeholder may not exist in the user file system.
                // We do not want to send extra requests to the remote storage if the parent folder is offline.
                if (Directory.Exists(userFileSystemParentPath)
                    && !new DirectoryInfo(userFileSystemParentPath).Attributes.HasFlag(FileAttributes.Offline)
                    && !FsPath.Exists(userFileSystemPath))
                {
                    IHierarchyItem remoteStorageItem = await Program.DavClient.GetItemAsync(new Uri(remoteStoragePath));

                    if ( (remoteStorageItem != null) && !FsPath.Exists(userFileSystemPath))
                    {
                        FileSystemItemMetadataExt itemMetadata = Mapping.GetUserFileSystemItemMetadata(remoteStorageItem);
                        await IncomingCreatedAsync(userFileSystemParentPath, itemMetadata);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(nameof(CreatedAsync), remoteStoragePath, null, ex);
            }
        }

        /// <summary>
        /// Called when a file content changed or file/folder attributes changed in the remote storage.
        /// </summary>
        /// <param name="remoteStoragePath">Path in the remote storage.</param>
        /// <remarks>
        /// In this method we update corresponding file/folder information in user file system.
        /// We also dehydrate the file if it is not blocked.
        /// </remarks>
        private async Task ChangedAsync(string remoteStoragePath)
        {
            try
            {
                string userFileSystemPath = Mapping.ReverseMapPath(remoteStoragePath);

                // Because of the on-demand population the file or folder placeholder may not exist in the user file system.
                // We do not want to send extra requests to the remote storage if the item does not exists in the user file system.
                if (FsPath.Exists(userFileSystemPath))
                {
                    IHierarchyItem remoteStorageItem = await Program.DavClient.GetItemAsync(new Uri(remoteStoragePath));
                    if (remoteStorageItem != null)
                    {
                        FileSystemItemMetadataExt itemMetadata = Mapping.GetUserFileSystemItemMetadata(remoteStorageItem);
                        await IncomingChangedAsync(userFileSystemPath, itemMetadata);
                    }
                }
            }
            catch (IOException ex)
            {
                // The file is blocked in the user file system. This is a normal behaviour.
                Logger.LogDebug(ex.Message);
            }
            catch (Exception ex)
            {
                Logger.LogError(nameof(ChangedAsync), remoteStoragePath, null, ex);
            }
        }

        /// <summary>
        /// Called when a file or folder is renamed in the remote storage.
        /// </summary>
        /// <param name="remoteStorageOldPath">Old path in the remote storage.</param>
        /// <param name="remoteStorageNewPath">New path in the remote storage.</param>
        /// <remarks>
        /// In this method we rename corresponding file/folder in the user file system.
        /// If the target folder parent does not exists or is offline we delete the source item.
        /// If the source item does not exists we create the target item.
        /// </remarks>
        private async Task MovedAsync(string remoteStorageOldPath, string remoteStorageNewPath)
        {
            try
            {
                string userFileSystemOldPath = Mapping.ReverseMapPath(remoteStorageOldPath);
                string userFileSystemNewPath = Mapping.ReverseMapPath(remoteStorageNewPath);

                if (FsPath.Exists(userFileSystemOldPath))
                {
                    // Source item is loaded, move it to a new location or delete.
                    if (await Engine.ServerNotifications(userFileSystemOldPath, Logger).MoveToAsync(userFileSystemNewPath))
                    {
                        Logger.LogMessage("Moved successfully", userFileSystemOldPath, userFileSystemNewPath);
                    }
                    else
                    {
                        // The target parent folder does not exists or is offline, delete the source item.
                        await DeletedAsync(remoteStorageOldPath);
                    }
                }
                else
                {
                    // Source item is not loaded. Creating the a item in the target folder, if the target parent folder is loaded.
                    await CreatedAsync(remoteStorageNewPath);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(nameof(MovedAsync), remoteStorageOldPath, remoteStorageNewPath, ex);
            }
        }

        /// <summary>
        /// Called when a file or folder is deleted in the remote storage.
        /// </summary>
        /// <param name="remoteStoragePath">Path in the remote storage.</param>
        /// <remarks>In this method we delete corresponding file/folder in the user file system.</remarks>
        private async Task DeletedAsync(string remoteStoragePath)
        {
            try
            {
                string userFileSystemPath = Mapping.ReverseMapPath(remoteStoragePath);

                if (FsPath.Exists(userFileSystemPath))
                {
                    await IncomingDeletedAsync(userFileSystemPath);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(nameof(DeletedAsync), remoteStoragePath, null, ex);
            }
        }

        /// <summary>
        /// Called when a file or folder is being locked in the remote storage.
        /// </summary>
        /// <param name="remoteStoragePath">Path in the remote storage.</param>
        /// <remarks>In this method we locked corresponding file/folder in user file system.</remarks>
        private async Task LockedAsync(string remoteStoragePath)
        {
            try
            {
                string userFileSystemPath = Mapping.ReverseMapPath(remoteStoragePath);

                if (FsPath.Exists(userFileSystemPath))
                {
                    IHierarchyItem remoteStorageItem = await Program.DavClient.GetItemAsync(new Uri(remoteStoragePath));
                    if (remoteStorageItem != null)
                    {
                        FileSystemItemMetadataExt itemMetadata = Mapping.GetUserFileSystemItemMetadata(remoteStorageItem);

                        // Save info about the third-party lock.
                        Engine.Placeholders.GetItem(userFileSystemPath).SetLockInfo(itemMetadata.Lock);
                        PlaceholderItem.UpdateUI(userFileSystemPath);

                        Logger.LogMessage("Locked", userFileSystemPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(nameof(LockedAsync), remoteStoragePath, null, ex);
            }
        }

        /// <summary>
        /// Called when a file or folder is unlocked in the remote storage.
        /// </summary>
        /// <param name="remoteStoragePath">Path in the remote storage.</param>
        /// <remarks>In this method we unlocked corresponding file/folder in user file system.</remarks>
        private async Task UnlockedAsync(string remoteStoragePath)
        {
            try
            {
                string userFileSystemPath = Mapping.ReverseMapPath(remoteStoragePath);

                if (FsPath.Exists(userFileSystemPath))
                {
                    PlaceholderItem placeholder = Engine.Placeholders.GetItem(userFileSystemPath);
                    if (placeholder.TryDeleteLockInfo())
                    {
                        PlaceholderItem.UpdateUI(userFileSystemPath);
                        Logger.LogMessage("Unlocked", userFileSystemPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(nameof(UnlockedAsync), remoteStoragePath, null, ex);
            }
        }


        private void Error(object sender, ErrorEventArgs e)
        {
            Logger.LogError(null, null, null, e.GetException());
        }


        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    clientWebSocket.Dispose();
                    Logger.LogMessage($"Disposed");
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~ServerChangesMonitor()
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

    /// <summary>
    /// Represents information about changes on the server.
    /// </summary>
    internal class WebSocketMessage
    {
        /// <summary>
        /// Operation type: "created", "updated", "moved", "deleted", "locked", "unlocked".
        /// </summary>
        public string EventType { get; set; }

        /// <summary>
        /// Item path in the remote storage. Source item path in case of the move operation.
        /// </summary>
        public string ItemPath { get; set; }

        /// <summary>
        /// Target item path in the remote storage. Provided only in case of the move operation.
        /// </summary>
        public string TargetPath { get; set; }
    }
}
