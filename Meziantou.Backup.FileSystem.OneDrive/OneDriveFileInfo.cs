using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meziantou.Backup.FileSystem.Abstractions;
using Meziantou.OneDrive;

namespace Meziantou.Backup.FileSystem.OneDrive
{
    [DebuggerDisplay("{FullName}")]
    public class OneDriveFileInfo : IDirectoryInfo, IFileInfo, IFullName
    {
        private readonly OneDriveItem _item;

        internal OneDriveFileInfo(OneDriveItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            _item = item;
        }

        public string Name => _item.Name;

        public bool IsDirectory => _item.Folder != null;

        public string Extension
        {
            get
            {
                if (_item.File != null)
                    return Path.GetExtension(Name);

                return null;
            }
        }

        public bool Exists => true;
        public DateTime CreationTimeUtc => _item.CreatedDateTime;
        public DateTime LastWriteTimeUtc => _item.LastModifiedDateTime;
        public long Length => _item.Size;

        public string FullName
        {
            get
            {
                if (_item.ParentReference == null)
                    return _item.Name;

                return _item.ParentReference.Path + "/" + _item.Name;
            }
        }

        public Task DeleteAsync(CancellationToken ct)
        {
            return _item.DeleteAsync(ct);
        }

        public async Task<IReadOnlyCollection<IFileSystemInfo>> GetItemsAsync(CancellationToken ct)
        {
            var oneDriveItems = await _item.GetChildrenAsync(ct);
            return oneDriveItems.Select(item => new OneDriveFileInfo(item)).ToList();
        }

        public async Task<IFileInfo> CreateFileAsync(string name, Stream stream, long length, CancellationToken ct)
        {
            const int chunckSize = 1 * 1024 * 1024;  // 1MB
            var oneDriveItem = await _item.CreateFileAsync(name, stream, length, chunckSize, OnChunkErrorHandler, ct);
            return new OneDriveFileInfo(oneDriveItem);
        }

        private bool OnChunkErrorHandler(ChunkUploadErrorEventArgs chunkUploadErrorEventArgs)
        {
            return chunkUploadErrorEventArgs.AttemptCount < 3; // Retry 3 times

        }

        public async Task<IDirectoryInfo> CreateDirectoryAsync(string name, CancellationToken ct)
        {
            var item = await _item.CreateDirectoryAsync(name, ct);
            return new OneDriveFileInfo(item);
        }

        public Task<Stream> OpenReadAsync(CancellationToken ct)
        {
            return _item.DownloadAsync(ct);
        }
    }
}