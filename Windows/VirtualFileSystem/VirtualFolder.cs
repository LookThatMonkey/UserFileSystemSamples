using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using ITHit.FileSystem;
using ITHit.FileSystem.Windows;
using ITHit.FileSystem.Samples.Common.Windows;

namespace VirtualFileSystem
{
    
    /// <inheritdoc cref="IFolder"/>
    public class VirtualFolder : VirtualFileSystemItem, IFolder
    {
        /// <summary>
        /// Creates instance of this class.
        /// </summary>
        /// <param name="path">Folder path in the user file system.</param>
        /// <param name="remoteStorageItemId">Remote storage item ID.</param>
        /// <param name="logger">Logger.</param>
        public VirtualFolder(string path, byte[] remoteStorageItemId, ILogger logger) : base(path, remoteStorageItemId, logger)
        {

        }

        /// <inheritdoc/>
        public async Task<byte[]> CreateFileAsync(IFileMetadata fileMetadata, Stream content = null, IInSyncResultContext inSyncResultContext = null, CancellationToken cancellationToken = default)
        {
            Logger.LogMessage($"{nameof(IFolder)}.{nameof(CreateFileAsync)}()", Path.Combine(UserFileSystemPath, fileMetadata.Name));

            string remoteStoragePath = Mapping.GetRemoteStoragePathById(RemoteStorageItemId);
            FileInfo remoteStorageNewItem = new FileInfo(Path.Combine(remoteStoragePath, fileMetadata.Name));

            // Create remote storage file.
            using (FileStream remoteStorageStream = remoteStorageNewItem.Open(FileMode.CreateNew, FileAccess.Write, FileShare.Delete))
            {
                // Upload content. Note that if the file is blocked - content parameter is null.
                if (content != null)
                {
                    await content.CopyToAsync(remoteStorageStream);
                    remoteStorageStream.SetLength(content.Length);
                }
            }

            // Update remote storage file metadata.
            remoteStorageNewItem.Attributes = fileMetadata.Attributes;
            remoteStorageNewItem.CreationTimeUtc = fileMetadata.CreationTime.UtcDateTime;
            remoteStorageNewItem.LastWriteTimeUtc = fileMetadata.LastWriteTime.UtcDateTime;
            remoteStorageNewItem.LastAccessTimeUtc = fileMetadata.LastAccessTime.UtcDateTime;
            remoteStorageNewItem.LastWriteTimeUtc = fileMetadata.LastWriteTime.UtcDateTime;

            // Return remote storage item ID. It will be passed later into IEngine.GetFileSystemItemAsync() method.
            return WindowsFileSystemItem.GetItemIdByPath(remoteStorageNewItem.FullName); 
        }

        /// <inheritdoc/>
        public async Task<byte[]> CreateFolderAsync(IFolderMetadata folderMetadata, IInSyncResultContext inSyncResultContext = null, CancellationToken cancellationToken = default)
        {
            Logger.LogMessage($"{nameof(IFolder)}.{nameof(CreateFolderAsync)}()", Path.Combine(UserFileSystemPath, folderMetadata.Name));

            string remoteStoragePath = Mapping.GetRemoteStoragePathById(RemoteStorageItemId);
            DirectoryInfo remoteStorageNewItem = new DirectoryInfo(Path.Combine(remoteStoragePath, folderMetadata.Name));
            remoteStorageNewItem.Create();

            // Update remote storage folder metadata.
            remoteStorageNewItem.Attributes = folderMetadata.Attributes;
            remoteStorageNewItem.CreationTimeUtc = folderMetadata.CreationTime.UtcDateTime;
            remoteStorageNewItem.LastWriteTimeUtc = folderMetadata.LastWriteTime.UtcDateTime;
            remoteStorageNewItem.LastAccessTimeUtc = folderMetadata.LastAccessTime.UtcDateTime;
            remoteStorageNewItem.LastWriteTimeUtc = folderMetadata.LastWriteTime.UtcDateTime;

            // Return the remote storage item ID. It will be passed later into the IEngine.GetFileSystemItemAsync() method.
            return WindowsFileSystemItem.GetItemIdByPath(remoteStorageNewItem.FullName);
        }

        /// <inheritdoc/>
        public async Task GetChildrenAsync(string pattern, IOperationContext operationContext, IFolderListingResultContext resultContext, CancellationToken cancellationToken)
        {
            // This method has a 60 sec timeout. 
            // To process longer requests and reset the timout timer call one of the following:
            // - resultContext.ReturnChildren() method.
            // - resultContext.ReportProgress() method.

            Logger.LogMessage($"{nameof(IFolder)}.{nameof(GetChildrenAsync)}({pattern})", UserFileSystemPath, default, operationContext);

            string remoteStoragePath = Mapping.GetRemoteStoragePathById(RemoteStorageItemId);
            IEnumerable<FileSystemInfo> remoteStorageChildren = new DirectoryInfo(remoteStoragePath).EnumerateFileSystemInfos(pattern);

            List<IFileSystemItemMetadata> userFileSystemChildren = new List<IFileSystemItemMetadata>();
            foreach (FileSystemInfo remoteStorageItem in remoteStorageChildren)
            {
                IFileSystemItemMetadata itemInfo = Mapping.GetUserFileSysteItemMetadata(remoteStorageItem);

                string userFileSystemItemPath = Path.Combine(UserFileSystemPath, itemInfo.Name);

                // Filtering existing files/folders. This is only required to avoid extra errors in the log.
                if (!FsPath.Exists(userFileSystemItemPath))
                {
                    Logger.LogDebug("Creating", userFileSystemItemPath, null, operationContext);
                    userFileSystemChildren.Add(itemInfo);
                }
            }

            // To signal that the children enumeration is completed 
            // always call ReturnChildren(), even if the folder is empty.
            await resultContext.ReturnChildrenAsync(userFileSystemChildren.ToArray(), userFileSystemChildren.Count());
        }

        /// <inheritdoc/>
        public async Task WriteAsync(IFolderMetadata folderMetadata, IOperationContext operationContext = null, IInSyncResultContext inSyncResultContext = null, CancellationToken cancellationToken = default)
        {
            Logger.LogMessage($"{nameof(IFolder)}.{nameof(WriteAsync)}()", UserFileSystemPath, default, operationContext);

            string remoteStoragePath = Mapping.GetRemoteStoragePathById(RemoteStorageItemId);
            DirectoryInfo remoteStorageItem = new DirectoryInfo(remoteStoragePath);

            // Update remote storage folder metadata.
            remoteStorageItem.Attributes = folderMetadata.Attributes;
            remoteStorageItem.CreationTimeUtc = folderMetadata.CreationTime.UtcDateTime;
            remoteStorageItem.LastWriteTimeUtc = folderMetadata.LastWriteTime.UtcDateTime;
            remoteStorageItem.LastAccessTimeUtc = folderMetadata.LastAccessTime.UtcDateTime;
            remoteStorageItem.LastWriteTimeUtc = folderMetadata.LastWriteTime.UtcDateTime;
        }
    }
    
}
