using ITHit.FileSystem.Windows.Package;
using ITHit.FileSystem.Windows.ShellExtension;
using System;
using System.Collections.Generic;
using Windows.Storage.Provider;

namespace WebDAVDrive.ShellExtension
{
    /// <summary>
    /// Shell extension handlers registration and startup functionality.
    /// </summary>
    internal static class ShellExtensions
    {
        /// <summary>
        /// List of shell extension handlers. Required for app without identity only.
        /// </summary>
        /// <remarks>
        /// For apps with identity this list is ignored. Shell extensions are installed/uninstalled
        /// via the sparse package manifest or package manifest in this case.
        /// 
        /// Note that on Windows 11 your context menu will be shown under the "Show more options"
        /// menu if your app runs without identity.
        /// </remarks>
        internal static readonly List<(string Name, Guid Guid)> Handlers = new List<(string, Guid)>
        {
            ("ThumbnailProvider", typeof(ThumbnailProviderIntegrated).GUID),
            ("MenuVerbHandler_0", typeof(ContextMenuVerbIntegrated).GUID),
            ("CustomStateHandler", typeof(CustomStateProviderIntegrated).GUID),
            //("UriHandler", typeof(ShellExtension.UriSourceIntegrated).GUID)
        };

        /// <summary>
        /// Runs shell extensions COM server and registers shell extension class objects with EXE COM server
        /// so other applications can connect to them if <paramref name="shellExtensionsComServerExePath"/> is null or empty
        /// or the application is running with a sparse package identity.
        /// </summary>
        /// <param name="shellExtensionsComServerRpcEnabled">True if RPC server is enabled</param>
        /// <returns><see cref="LocalServer"/> instance if <paramref name="shellExtensionsComServerExePath"/> is null or empty
        /// or the application is running with a sparse package identity; null otherwise.</returns>
        internal static LocalServer StartComServer(bool shellExtensionsComServerRpcEnabled)
        {
            if (shellExtensionsComServerRpcEnabled && !PackageRegistrar.IsRunningWithSparsePackageIdentity())
            {
                return null;
            }

            LocalServer server = new LocalServer();

            server.RegisterClass<ThumbnailProviderIntegrated>();
            server.RegisterClass<ContextMenuVerbIntegrated>();
            server.RegisterWinRTClass<IStorageProviderItemPropertySource, CustomStateProviderIntegrated>();
            //server.RegisterWinRTClass<IStorageProviderUriSource, ShellExtension.UriSourceIntegrated>();

            return server;
        }
    }
}
