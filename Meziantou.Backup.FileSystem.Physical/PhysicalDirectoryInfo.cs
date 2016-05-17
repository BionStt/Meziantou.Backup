using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Meziantou.Backup.FileSystem.Abstractions;
using DirectoryInfo = Alphaleonis.Win32.Filesystem.DirectoryInfo;
using FileInfo = Alphaleonis.Win32.Filesystem.FileInfo;
using Path = Alphaleonis.Win32.Filesystem.Path;

namespace Meziantou.Backup.FileSystem.Physical
{
    [DebuggerDisplay("{FullName}")]
    public class PhysicalDirectoryInfo : PhysicalFileSystemInfo, IDirectoryInfo
    {
        private readonly DirectoryInfo _di;

        public PhysicalDirectoryInfo(DirectoryInfo di) : base(di)
        {
            if (di == null) throw new ArgumentNullException(nameof(di));
            _di = di;
        }

        public Task<IReadOnlyCollection<IFileSystemInfo>> GetItemsAsync(CancellationToken ct)
        {
            var result = new List<PhysicalFileSystemInfo>();
            var children = _di.GetFileSystemInfos();
            foreach (var fileSystemInfo in children)
            {
                var di = fileSystemInfo as DirectoryInfo;
                if (di != null)
                {
                    result.Add(new PhysicalDirectoryInfo(di));
                }
                else
                {
                    var fi = fileSystemInfo as FileInfo;
                    if (fi != null)
                    {
                        result.Add(new PhysicalFileInfo(fi));
                    }
                }
            }

            return Task.FromResult<IReadOnlyCollection<IFileSystemInfo>>(result);
        }

        public async Task<IFileInfo> CreateFileAsync(string name, Stream stream, long length, CancellationToken ct)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            var path = Path.Combine(_di.FullName, name);
            var fi = new FileInfo(path);
            using (var dest = fi.Create())
            {
                await stream.CopyToAsync(dest, 81920 /* Stream._DefaultCopyBufferSize */, ct);
            }

            return new PhysicalFileInfo(fi);
        }

        public Task<IDirectoryInfo> CreateDirectoryAsync(string name, CancellationToken ct)
        {
            var sub = _di.CreateSubdirectory(name);
            var item = new PhysicalDirectoryInfo(sub);
            return Task.FromResult<IDirectoryInfo>(item);
        }
    }
}