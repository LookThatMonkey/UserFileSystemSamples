﻿using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace VirtualFileSystem
{
    /// <summary>
    /// Strongly binded project settings.
    /// </summary>
    public class Settings
    {
        /// <summary>
        /// IT Hit User File System License string;
        /// </summary>
        public string UserFileSystemLicense { get; set; }

        /// <summary>
        /// IT Hit WebDAV Client Library for .NET License string;
        /// </summary>
        public string WebDAVClientLicense { get; set; }

        /// <summary>
        /// WebDAV server URL.
        /// </summary>
        public string WebDAVServerUrl { get; set; }

        /// <summary>
        /// Your virtual file system will be mounted under this path.
        /// </summary>
        public string UserFileSystemRootPath { get; set; }

        /// <summary>
        /// Full synchronization interval in milliseconds.
        /// </summary>
        public double SyncIntervalMs { get; set; }

        /// <summary>
        /// Network delay in milliseconds. When this parameter is > 0 the file download will be delayd to demonstrate file transfer progress.
        /// Set this parameter to 0 to avoid any network simulation delays.
        /// </summary>
        public int NetworkSimulationDelayMs { get; set; }

        /// <summary>
        /// Path to the icons folder.
        /// </summary>
        public string IconsFolderPath { get; set; }

        /// <summary>
        /// Path to the folder that stores ETags, locks and other data associated with files and folders.
        /// </summary>
        public string ServerDataFolderPath{ get; set; }

        /// <summary>
        /// Automatically lock the file in remote storage when a file handle is being opened for writing, unlock on close.
        /// </summary>
        public bool AutoLock { get; set; }
    }

    /// <summary>
    /// Binds, validates and normalizes Settings configuration.
    /// </summary>
    public static class SettingsConfigValidator
    {
        /// <summary>
        /// Binds, validates and normalizes WebDAV Context configuration.
        /// </summary>
        /// <param name="configurationSection">Instance of <see cref="IConfigurationSection"/>.</param>
        /// <param name="settings">Virtual File System Settings.</param>
        public static Settings ReadSettings(this IConfiguration configuration)
        {
            Settings settings = new Settings();

            if (configuration == null)
            {
                throw new ArgumentNullException("configurationSection");
            }

            configuration.Bind(settings);

            if (string.IsNullOrEmpty(settings.WebDAVServerUrl))
            {
                throw new ArgumentNullException("Settings.WebDAVServerUrl");
            }

            settings.WebDAVServerUrl = $"{settings.WebDAVServerUrl.TrimEnd('/')}/";

            if (string.IsNullOrEmpty(settings.UserFileSystemRootPath))
            {
                throw new ArgumentNullException("Settings.UserFileSystemRootPath");
            }

            settings.UserFileSystemRootPath = $"{Path.TrimEndingDirectorySeparator(settings.UserFileSystemRootPath)}{Path.DirectorySeparatorChar}";

            if (!Directory.Exists(settings.UserFileSystemRootPath))
            {
                settings.UserFileSystemRootPath = Environment.ExpandEnvironmentVariables(settings.UserFileSystemRootPath);
            }

            if (!Directory.Exists(settings.UserFileSystemRootPath))
            {
                Directory.CreateDirectory(settings.UserFileSystemRootPath);
            }

            // Icons folder.
            settings.IconsFolderPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), @"Images");

            // Folder where eTags and file locks are sored.
            string localApplicationDataFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            settings.ServerDataFolderPath = Path.Combine(localApplicationDataFolderPath, settings.UserFileSystemRootPath.Replace(":", ""), "ServerData");


            return settings;
        }
    }
}
