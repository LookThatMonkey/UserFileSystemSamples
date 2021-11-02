using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ITHit.FileSystem;
using ITHit.FileSystem.Samples.Common;
using ITHit.FileSystem.Samples.Common.Windows;

namespace VirtualDrive
{
    /// <summary>
    /// Maps a user file system path to the remote storage path and back. 
    /// </summary>
    /// <remarks>
    /// You will change methods of this class to map the user file system path to your remote storage path.
    /// </remarks>
    public class Mapping : IMapping
    {
        private readonly VirtualEngineBase engine;

        internal Mapping(VirtualEngineBase engine)
        {
            this.engine = engine;
        }

        /// <summary>
        /// Returns a remote storage URI that corresponds to the user file system path.
        /// </summary>
        /// <param name="userFileSystemPath">Full path in the user file system.</param>
        /// <returns>Remote storage URI that corresponds to the <paramref name="userFileSystemPath"/>.</returns>
        public static string MapPath(string userFileSystemPath)
        {
            // Get path relative to the virtual root.
            string relativePath = Path.TrimEndingDirectorySeparator(userFileSystemPath).Substring(
                Path.TrimEndingDirectorySeparator(Program.Settings.UserFileSystemRootPath).Length);

            string path = $"{Path.TrimEndingDirectorySeparator(Program.Settings.RemoteStorageRootPath)}{relativePath}";
            return path;
        }

        /// <summary>
        /// Returns a user file system path that corresponds to the remote storage URI.
        /// </summary>
        /// <param name="remoteStorageUri">Remote storage URI.</param>
        /// <returns>Path in the user file system that corresponds to the <paramref name="remoteStorageUri"/>.</returns>
        public static string ReverseMapPath(string remoteStorageUri)
        {
            // Get path relative to the virtual root.
            string relativePath = Path.TrimEndingDirectorySeparator(remoteStorageUri).Substring(
                Path.TrimEndingDirectorySeparator(Program.Settings.RemoteStorageRootPath).Length);

            string path = $"{Path.TrimEndingDirectorySeparator(Program.Settings.UserFileSystemRootPath)}{relativePath}";
            return path;
        }

        /// <summary>
        /// Gets a user file system item info from the remote storage data.
        /// </summary>
        /// <param name="remoteStorageItem">Remote storage item info.</param>
        /// <returns>User file system item info.</returns>
        public static FileSystemItemMetadataExt GetUserFileSysteItemMetadata(FileSystemInfo remoteStorageItem)
        {
            FileSystemItemMetadataExt userFileSystemItem;

            if (remoteStorageItem is FileInfo)
            {
                userFileSystemItem = new FileMetadataExt();
                ((FileMetadataExt)userFileSystemItem).Length = ((FileInfo)remoteStorageItem).Length;
            }
            else
            {
                userFileSystemItem = new FolderMetadataExt();
            }

            userFileSystemItem.Name = remoteStorageItem.Name;
            userFileSystemItem.Attributes = remoteStorageItem.Attributes;
            userFileSystemItem.CreationTime = remoteStorageItem.CreationTime;
            userFileSystemItem.LastWriteTime = remoteStorageItem.LastWriteTime;
            userFileSystemItem.LastAccessTime = remoteStorageItem.LastAccessTime;
            userFileSystemItem.ChangeTime = remoteStorageItem.LastWriteTime;

            userFileSystemItem.IsLocked = false;

            string eTag = "1234567890";
            if (userFileSystemItem is IFileMetadata)
            {
                ((FileMetadataExt)userFileSystemItem).ETag = eTag;
            };

            // Set custom columns to be displayed in file manager.
            // We create property definitions when registering the sync root with corresponding IDs.
            List<FileSystemItemPropertyData> customProps = new List<FileSystemItemPropertyData>();

            // Set ETag property.
            if (userFileSystemItem is IFileMetadata)
            {
                customProps.Add(new FileSystemItemPropertyData((int)CustomColumnIds.ETag, eTag));
            };

            userFileSystemItem.CustomProperties = customProps;

            return userFileSystemItem;
        }

        public async Task<bool> IsModifiedAsync(string userFileSystemPath, FileSystemItemMetadataExt remoteStorageItemMetadata, ILogger logger)
        {
            string remoteStoragePath = MapPath(userFileSystemPath);

            return IsModified(userFileSystemPath, remoteStoragePath);
        }

        /// <summary>
        /// Compares two files contents.
        /// </summary>
        /// <param name="filePath1">File or folder 1 to compare.</param>
        /// <param name="filePath2">File or folder 2 to compare.</param>
        /// <returns>True if file is modified. False - otherwise.</returns>
        internal static bool IsModified(string filePath1, string filePath2)
        {
            if (FsPath.IsFolder(filePath1) && FsPath.IsFolder(filePath2))
            {
                return false;
            }

            try
            {
                if (new FileInfo(filePath1).Length == new FileInfo(filePath2).Length)
                {
                    // Verify that the file is not offline,
                    // therwise the file will be hydrated when the file stream is opened.
                    if (new FileInfo(filePath1).Attributes.HasFlag(System.IO.FileAttributes.Offline)
                        || new FileInfo(filePath1).Attributes.HasFlag(System.IO.FileAttributes.Offline))
                    {
                        return false;
                    }

                    byte[] hash1;
                    byte[] hash2;
                    using (var alg = System.Security.Cryptography.MD5.Create())
                    {
                        // This code for demo purposes only. We do not block files for writing, which is required by some apps, for example by AutoCAD.
                        using (FileStream stream = new FileStream(filePath1, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        {
                            hash1 = alg.ComputeHash(stream);
                        }
                        using (FileStream stream = new FileStream(filePath2, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        {
                            hash2 = alg.ComputeHash(stream);
                        }
                    }

                    return !hash1.SequenceEqual(hash2);
                }
            }
            catch (IOException)
            {
                // One of the files is blocked. Can not compare files and start sychronization.
                return false;
            }

            return true;
        }
    }
}
