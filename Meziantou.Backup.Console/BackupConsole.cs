using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meziantou.Backup.FileSystem.Abstractions;
using Meziantou.Backup.FileSystem.Aes;
using Microsoft.Extensions.Configuration;

namespace Meziantou.Backup.Console
{
    internal class BackupConsole
    {
        private string GetValue(IConfigurationRoot configuration, string key, string defaultValue)
        {
            var section = configuration.GetSection(key);
            if (section == null)
                return defaultValue;

            return section.Value;
        }

        private int GetValue(IConfigurationRoot configuration, string key, int defaultValue)
        {
            var section = configuration.GetSection(key);
            if (section == null)
                return defaultValue;

            int v;
            if (int.TryParse(section.Value, out v))
                return v;

            return defaultValue;
        }

        private bool GetValue(IConfigurationRoot configuration, string key, bool defaultValue)
        {
            var section = configuration.GetSection(key);
            if (section == null)
                return defaultValue;

            bool v;
            if (bool.TryParse(section.Value, out v))
                return v;

            return defaultValue;
        }

        private T GetValue<T>(IConfigurationRoot configuration, string key, T defaultValue) where T : struct
        {
            var section = configuration.GetSection(key);
            if (section == null)
                return defaultValue;

            if (typeof(T).IsEnum)
            {
                T v;
                if (Enum.TryParse(section.Value, true, out v))
                    return v;
            }

            return defaultValue;
        }

        private T? GetValue<T>(IConfigurationRoot configuration, string key, T? defaultValue) where T : struct
        {
            var section = configuration.GetSection(key);
            if (section == null)
                return defaultValue;

            if (typeof(T).IsEnum)
            {
                T v;
                if (Enum.TryParse(section.Value, true, out v))
                    return v;
            }

            return defaultValue;
        }

        public void Main(string[] args)
        {
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                System.Console.CancelKeyPress += (sender, eventArgs) =>
                {
                    cts.Cancel();
                    eventArgs.Cancel = true;
                };

                var configuration = new ConfigurationBuilder()
                    .AddCommandLine(args)
                    .Build();

                Backup backup = new Backup();
                backup.Action += Backup_Action;
                backup.Error += Backup_Error;
                backup.Copying += Backup_Copying;
                backup.EqualityMethods = GetValue(configuration, "EqualityMethods", FileInfoEqualityMethods.Default);
                backup.RetryCount = GetValue(configuration, "RetryCount", 3);
                backup.CanCreateDirectories = GetValue(configuration, "CanCreateDirectories", true);
                backup.CanDeleteDirectories = GetValue(configuration, "CanDeleteDirectories", false);
                backup.CanCreateFiles = GetValue(configuration, "CanCreateFiles", true);
                backup.CanUpdateFiles = GetValue(configuration, "CanUpdateFiles", true);
                backup.CanDeleteFiles = GetValue(configuration, "CanDeleteFiles", false);

                var summary = new BackupSummary(backup);

                try
                {
                    var backupAsync = RunAsync(backup, configuration, cts.Token);
                    var awaiter = backupAsync.GetAwaiter();
                    awaiter.GetResult();
                }
                catch (OperationCanceledException)
                {
                    System.Console.WriteLine("Operation was canceled");
                }

                System.Console.WriteLine("Directories: " + summary.DirectoryCount);
                System.Console.WriteLine("  Created: " + summary.DirectoryCreatedCount);
                System.Console.WriteLine("  Deleted: " + summary.DirectoryDeletedCount);

                System.Console.WriteLine("File: " + summary.FileCount);
                System.Console.WriteLine("  Created: " + summary.FileCreatedCount);
                System.Console.WriteLine("  Updated: " + summary.FileUpdatedCount);
                System.Console.WriteLine("  Deleted: " + summary.FileDeletedCount);
            }
        }

        private async Task RunAsync(Backup backup, IConfigurationRoot configuration, CancellationToken ct)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            ct.ThrowIfCancellationRequested();

            var sourceDir = await GetDirectoryInfoAsync(configuration, "source", ct);
            var targetDir = await GetDirectoryInfoAsync(configuration, "target", ct);

            await backup.RunAsync(sourceDir, targetDir, ct).ConfigureAwait(false);
        }

        private async Task<IDirectoryInfo> GetDirectoryInfoAsync(IConfigurationRoot configuration, string prefix, CancellationToken ct)
        {
            string providerName = GetValue(configuration, prefix + "ProviderName", null);
            string path = GetValue(configuration, prefix + "Path", null);
            AesVersion? aesVersion = GetValue(configuration, prefix + "AesVersion", (AesVersion?)null);
            bool aesEncryptFileName = GetValue(configuration, prefix + "AesEncryptFileName", false);
            bool aesEncryptDirectoryName = GetValue(configuration, prefix + "AesEncryptDirectoryName", false);
            string aesPassword = GetValue(configuration, prefix + "AesPassword", null);

            Dictionary<string, object> providerConfiguration = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            var configurationPrefix = prefix + "Configuration-";
            var children = configuration.GetChildren();
            foreach (var child in children)
            {
                if (child.Key.StartsWith(configurationPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    providerConfiguration[child.Key.Substring(configurationPrefix.Length)] = child.Value;
                }
            }

            // Instanciate
            if (providerName == null)
                throw new Exception("Unknown ProviderName");

            var type = GetType(providerName);
            if (type == null)
                throw new Exception("Unknown ProviderName: " + providerName);

            var fileSystem = (IFileSystem)CreateInstance(type);
            if (fileSystem == null)
                throw new Exception("Cannot instanciate type: " + type.AssemblyQualifiedName);

            fileSystem.Initialize(providerConfiguration);

            if (!string.IsNullOrEmpty(aesPassword))
            {
                var aesFileSystem = new AesFileSystem(fileSystem);
                if (aesVersion != null)
                {
                    aesFileSystem.Version = aesVersion.Value;
                }

                aesFileSystem.EncryptDirectoryName = aesEncryptDirectoryName;
                aesFileSystem.EncryptFileName = aesEncryptFileName;
                aesFileSystem.Password = aesPassword;
                fileSystem = aesFileSystem;
            }

            return await GetOrCreateRootDirectoryItemAsync(fileSystem, path, ct);
        }

        protected virtual object CreateInstance(Type type)
        {
            return Activator.CreateInstance(type);
        }

        protected virtual Type GetType(string name)
        {
            if (string.Equals("FileSystem", name, StringComparison.InvariantCultureIgnoreCase))
                return Type.GetType("Meziantou.Backup.FileSystem.Physical.PhysicalFileSystem, Meziantou.Backup.FileSystem.Physical");

            if (string.Equals("OneDrive", name, StringComparison.InvariantCultureIgnoreCase))
                return Type.GetType("Meziantou.Backup.FileSystem.OneDrive.OneDriveFileSystem, Meziantou.Backup.FileSystem.OneDrive");

            if (string.Equals("AES", name, StringComparison.InvariantCultureIgnoreCase))
                return Type.GetType("Meziantou.Backup.FileSystem.Aes.AesFileSystem, Meziantou.Backup.FileSystem.Aes");

            return Type.GetType(name, true);
        }

        private async Task<IDirectoryInfo> GetOrCreateRootDirectoryItemAsync(IFileSystem fileSystem, string path, CancellationToken ct)
        {
            var authenticable = fileSystem as IAuthenticable;
            if (authenticable != null)
            {
                await authenticable.LogInAsync(ct).ConfigureAwait(false);
            }

            return await fileSystem.GetOrCreateDirectoryItemAsync(path, ct).ConfigureAwait(false);
        }

        private void Backup_Copying(object sender, FileCopyingEventArgs e)
        {
            float percent = ((float)e.CurrentPosition) / e.FileLength * 100f;
            System.Console.WriteLine($"[{Thread.CurrentThread.ManagedThreadId}] Copying ({percent:F1}% - {FriendlyFileLength(e.CurrentPosition)}/{FriendlyFileLength(e.FileLength)}): <{GetDisplayName(e.SourceItem)}> -> <{GetDisplayName(e.TargetItem)}>");
        }

        private string FriendlyFileLength(long length)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
            int s = 0;

            while (length >= 1024)
            {
                s++;
                length /= 1024;
            }

            return $"{length} {suffixes[s]}";
        }

        private void Backup_Error(object sender, BackupErrorEventArgs e)
        {
            if (e.Exception is OperationCanceledException)
            {
                System.Console.WriteLine($"[{Thread.CurrentThread.ManagedThreadId}] Error: Operation canceled");
                e.Cancel = true;
            }
            else
            {
                System.Console.WriteLine($"[{Thread.CurrentThread.ManagedThreadId}] Retry ({e.RetryCount}): {e.Exception}");
            }
        }

        private void Backup_Action(object sender, BackupActionEventArgs e)
        {
            if (e.Method != FileInfoEqualityMethods.None)
            {
                System.Console.WriteLine($"[{Thread.CurrentThread.ManagedThreadId}] {e.Action} ({e.Method}): <{GetDisplayName(e.SourceItem)}> -> <{GetDisplayName(e.TargetItem)}>");
            }
            else
            {
                System.Console.WriteLine($"[{Thread.CurrentThread.ManagedThreadId}] {e.Action}: <{GetDisplayName(e.SourceItem)}> -> <{GetDisplayName(e.TargetItem)}>");
            }
        }

        private string GetDisplayName(IFileSystemInfo item)
        {
            var fullName = item as IFullName;
            if (fullName != null)
                return fullName.FullName;

            return item?.Name;
        }
    }
}