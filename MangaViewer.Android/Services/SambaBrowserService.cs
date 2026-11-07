using MangaViewer.Android.Models;
using System.Buffers;
using SMBLibrary;
using SMBLibrary.Client;

namespace MangaViewer.Android.Services;

public sealed class SambaBrowserService
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] ArchiveExtensions = [".cbz", ".zip"];
    private readonly AppSettingsService _settingsService;

    public SambaBrowserService(AppSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<IReadOnlyList<BrowserItem>> ListChildrenAsync(SambaConnectionConfig config, string? smbPath)
    {
        return await Task.Run(() =>
        {
            string current = string.IsNullOrWhiteSpace(smbPath)
                ? BuildBasePath(config)
                : EnsureTrailingSlash(smbPath!);

            var items = new List<BrowserItem>();
            string basePath = BuildBasePath(config);
            if (!string.Equals(current, basePath, StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new BrowserItem
                {
                    Name = "..",
                    Path = BuildParentPath(current, config),
                    IsDirectory = true,
                    IsParentLink = true
                });
            }

            var store = ConnectFileStore(config, out _);
            string relativeDirectory = ToShareRelativePath(config, current);
            object handle;
            NTStatus status = store.CreateFile(
                out handle,
                out _,
                relativeDirectory,
                AccessMask.GENERIC_READ,
                SMBLibrary.FileAttributes.Normal,
                ShareAccess.Read | ShareAccess.Write,
                CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_DIRECTORY_FILE,
                new SecurityContext(string.Empty, Environment.MachineName, new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0), null, null));

            if (status != NTStatus.STATUS_SUCCESS)
                return items;

            try
            {
                var entries = new List<QueryDirectoryFileInformation>();
                status = store.QueryDirectory(out entries, handle, "*", FileInformationClass.FileDirectoryInformation);
                if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_NO_MORE_FILES)
                    return items;

                foreach (var entry in entries)
                {
                    if (entry is not FileDirectoryInformation info)
                        continue;

                    if (string.IsNullOrWhiteSpace(info.FileName) || info.FileName == "." || info.FileName == "..")
                        continue;

                    bool isDirectory = (info.FileAttributes & SMBLibrary.FileAttributes.Directory) == SMBLibrary.FileAttributes.Directory;
                    if (!isDirectory)
                        continue;

                    string childPath = PathCombine(current, info.FileName);
                    items.Add(new BrowserItem
                    {
                        Name = info.FileName,
                        Path = EnsureTrailingSlash(childPath),
                        IsDirectory = true
                    });
                }
            }
            finally
            {
                store.CloseFile(handle);
                store.Disconnect();
            }

            return (IReadOnlyList<BrowserItem>)items
                .OrderByDescending(x => x.IsParentLink)
                .ThenBy(x => x.Name, new CbzVolumeComparer())
                .ToList();
        });
    }

    public async Task<IReadOnlyList<string>> ListImagePathsAsync(SambaConnectionConfig config, string smbFolderPath)
    {
        return await Task.Run(() =>
        {
            var store = ConnectFileStore(config, out _);
            string relativeDirectory = ToShareRelativePath(config, EnsureTrailingSlash(smbFolderPath));
            object handle;
            NTStatus status = store.CreateFile(
                out handle,
                out _,
                relativeDirectory,
                AccessMask.GENERIC_READ,
                SMBLibrary.FileAttributes.Normal,
                ShareAccess.Read | ShareAccess.Write,
                CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_DIRECTORY_FILE,
                new SecurityContext(string.Empty, Environment.MachineName, new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0), null, null));

            if (status != NTStatus.STATUS_SUCCESS)
                return Array.Empty<string>();

            try
            {
                var entries = new List<QueryDirectoryFileInformation>();
                status = store.QueryDirectory(out entries, handle, "*", FileInformationClass.FileDirectoryInformation);
                if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_NO_MORE_FILES)
                    return Array.Empty<string>();

                var result = new List<string>();
                foreach (var entry in entries)
                {
                    if (entry is not FileDirectoryInformation info)
                        continue;

                    string fileName = info.FileName;
                    if (string.IsNullOrWhiteSpace(fileName) || fileName == "." || fileName == "..")
                        continue;

                    if ((info.FileAttributes & SMBLibrary.FileAttributes.Directory) == SMBLibrary.FileAttributes.Directory)
                        continue;

                    string ext = Path.GetExtension(fileName).ToLowerInvariant();
                    if (!ImageExtensions.Contains(ext))
                        continue;

                    result.Add(BuildFullSmbPath(config, relativeDirectory, fileName));
                }

                result.Sort(new CbzVolumeComparer());
                return (IReadOnlyList<string>)result;
            }
            finally
            {
                store.CloseFile(handle);
                store.Disconnect();
            }
        });
    }

    public async Task<IReadOnlyList<string>> ListArchivePathsAsync(SambaConnectionConfig config, string smbFolderPath)
    {
        return await Task.Run(() =>
        {
            var store = ConnectFileStore(config, out _);
            string relativeDirectory = ToShareRelativePath(config, EnsureTrailingSlash(smbFolderPath));
            object handle;
            NTStatus status = store.CreateFile(
                out handle,
                out _,
                relativeDirectory,
                AccessMask.GENERIC_READ,
                SMBLibrary.FileAttributes.Normal,
                ShareAccess.Read | ShareAccess.Write,
                CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_DIRECTORY_FILE,
                new SecurityContext(string.Empty, Environment.MachineName, new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0), null, null));

            if (status != NTStatus.STATUS_SUCCESS)
                return Array.Empty<string>();

            try
            {
                var entries = new List<QueryDirectoryFileInformation>();
                status = store.QueryDirectory(out entries, handle, "*", FileInformationClass.FileDirectoryInformation);
                if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_NO_MORE_FILES)
                    return Array.Empty<string>();

                var result = new List<string>();
                foreach (var entry in entries)
                {
                    if (entry is not FileDirectoryInformation info)
                        continue;

                    string fileName = info.FileName;
                    if (string.IsNullOrWhiteSpace(fileName) || fileName == "." || fileName == "..")
                        continue;

                    if ((info.FileAttributes & SMBLibrary.FileAttributes.Directory) == SMBLibrary.FileAttributes.Directory)
                        continue;

                    string ext = Path.GetExtension(fileName).ToLowerInvariant();
                    if (!ArchiveExtensions.Contains(ext))
                        continue;

                    result.Add(BuildFullSmbPath(config, relativeDirectory, fileName));
                }

                result.Sort(new CbzVolumeComparer());
                return (IReadOnlyList<string>)result;
            }
            finally
            {
                store.CloseFile(handle);
                store.Disconnect();
            }
        });
    }

    public Stream OpenRead(SambaConnectionConfig config, string smbFilePath)
    {
        var store = ConnectFileStore(config, out var client);
        string relativePath = ToShareRelativePath(config, smbFilePath);
        object handle;
        NTStatus status = store.CreateFile(
            out handle,
            out var fileStatus,
            relativePath,
            AccessMask.GENERIC_READ,
            SMBLibrary.FileAttributes.Normal,
            ShareAccess.Read,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_NON_DIRECTORY_FILE,
            new SecurityContext(string.Empty, Environment.MachineName, new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0), null, null));

        if (status != NTStatus.STATUS_SUCCESS)
            throw new IOException($"SMB file open failed: {status}");

        long? fileLength = null;
        NTStatus infoStatus = store.GetFileInformation(out var fileInfo, handle, FileInformationClass.FileStandardInformation);
        if (infoStatus == NTStatus.STATUS_SUCCESS && fileInfo is FileStandardInformation standardInfo && standardInfo.EndOfFile >= 0)
            fileLength = standardInfo.EndOfFile;

        int maxReadSize = (int)Math.Clamp((long)store.MaxReadSize, 64L * 1024L, 1024L * 1024L);
        var stream = new SmbReadStream(client, store, handle, maxReadSize, fileLength);
        return stream;
    }

    public string BuildBasePath(SambaConnectionConfig config)
    {
        string basePath = string.IsNullOrWhiteSpace(config.ShareName)
            ? $"smb://{config.Host}/"
            : $"smb://{config.Host}/{config.ShareName.Trim('/')}/";

        if (!string.IsNullOrWhiteSpace(config.RootPath))
            basePath = EnsureTrailingSlash(basePath + config.RootPath.Trim('/'));

        return basePath;
    }

    private SMB2Client CreateClient(SambaConnectionConfig config)
    {
        var client = new SMB2Client();
        if (!client.Connect(config.Host, SMBTransportType.DirectTCPTransport))
            throw new IOException($"SMB connect failed: {config.Host}");

        (string userName, string password)? credential = _settingsService.GetCredentialAsync(config.Id).GetAwaiter().GetResult();
        string user = credential?.userName ?? string.Empty;
        string pass = credential?.password ?? string.Empty;
        NTStatus loginStatus = client.Login(string.Empty, user, pass);
        if (loginStatus != NTStatus.STATUS_SUCCESS)
            throw new IOException($"SMB login failed: {loginStatus}");

        return client;
    }

    private ISMBFileStore ConnectFileStore(SambaConnectionConfig config, out SMB2Client client)
    {
        client = CreateClient(config);
        var store = client.TreeConnect(config.ShareName, out var status);
        if (status != NTStatus.STATUS_SUCCESS)
            throw new IOException($"SMB tree connect failed: {status}");

        return store;
    }

    private static string ToShareRelativePath(SambaConnectionConfig config, string smbPath)
    {
        string normalized = smbPath.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        string sharePrefix = $"smb://{config.Host}/{config.ShareName.Trim('/')}/";
        string trimmed = normalized.TrimEnd('/');
        if (trimmed.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
        {
            if (trimmed.StartsWith(sharePrefix, StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(sharePrefix.Length);
        }

        trimmed = trimmed.Replace('/', '\\');
        return trimmed.Trim('\\');
    }

    private static string BuildFullSmbPath(SambaConnectionConfig config, string relativeDirectory, string fileName)
    {
        string fullDirectory = relativeDirectory.Trim('\\');
        string file = fileName.TrimStart('\\');
        string pathPart = string.IsNullOrWhiteSpace(fullDirectory)
            ? file
            : $"{fullDirectory.Replace('\\', '/')}/{file}";
        return $"smb://{config.Host}/{config.ShareName.Trim('/')}/{pathPart}";
    }

    private static string EnsureTrailingSlash(string path)
    {
        if (path.EndsWith('/'))
            return path;
        return path + "/";
    }

    private string BuildParentPath(string currentPath, SambaConnectionConfig config)
    {
        string basePath = BuildBasePath(config);
        string normalizedCurrent = EnsureTrailingSlash(currentPath);
        string trimmed = normalizedCurrent.TrimEnd('/');
        int slash = trimmed.LastIndexOf('/');
        if (slash < 6)
            return basePath;

        string parent = trimmed.Substring(0, slash + 1);
        if (parent.Length < basePath.Length)
            return basePath;

        if (!parent.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            return basePath;

        return parent;
    }

    private static string PathCombine(string basePath, string childName)
    {
        string normalizedBase = basePath.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalizedBase))
            return childName;
        return normalizedBase + "/" + childName.TrimStart('/');
    }

    private sealed class SmbReadStream : Stream
    {
        private readonly SMB2Client _client;
        private readonly ISMBFileStore _store;
        private readonly object _handle;
        private readonly int _maxReadSize;
        private readonly long? _length;
        private byte[] _chunk = Array.Empty<byte>();
        private int _chunkOffset;
        private long _position;
        private bool _reachedEnd;
        private bool _disposed;

        public SmbReadStream(SMB2Client client, ISMBFileStore store, object handle, int maxReadSize, long? length)
        {
            _client = client;
            _store = store;
            _handle = handle;
            _maxReadSize = maxReadSize > 0 ? maxReadSize : 256 * 1024;
            _length = length;
        }

        public override bool CanRead => !_disposed;

        public override bool CanSeek => !_disposed && _length.HasValue;

        public override bool CanWrite => false;

        public override long Length => _length ?? throw new NotSupportedException();

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer is null)
                throw new ArgumentNullException(nameof(buffer));

            if (offset < 0 || count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException();

            if (count == 0)
                return 0;

            int totalRead = 0;
            while (count > 0)
            {
                if (_length.HasValue && _position >= _length.Value)
                    break;

                if (_chunkOffset >= _chunk.Length)
                {
                    FillChunk();
                    if (_chunk.Length == 0)
                        break;
                }

                int available = _chunk.Length - _chunkOffset;
                int toCopy = Math.Min(available, count);
                Array.Copy(_chunk, _chunkOffset, buffer, offset, toCopy);

                _chunkOffset += toCopy;
                _position += toCopy;
                offset += toCopy;
                count -= toCopy;
                totalRead += toCopy;
            }

            return totalRead;
        }

        public override int Read(Span<byte> buffer)
        {
            if (buffer.Length == 0)
                return 0;

            byte[] temp = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                int read = Read(temp, 0, buffer.Length);
                if (read > 0)
                    temp.AsSpan(0, read).CopyTo(buffer);
                return read;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(temp);
            }
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return ValueTask.FromCanceled<int>(cancellationToken);

            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<int>(cancellationToken);

            return Task.FromResult(Read(buffer, offset, count));
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (!CanSeek)
                throw new NotSupportedException();

            long basePos = origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => _position,
                SeekOrigin.End => _length!.Value,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };

            long newPos = basePos + offset;
            if (newPos < 0)
                throw new IOException("Attempted to seek before beginning of stream.");

            if (_length.HasValue && newPos > _length.Value)
                newPos = _length.Value;

            _position = newPos;
            _chunk = Array.Empty<byte>();
            _chunkOffset = 0;
            _reachedEnd = _length.HasValue && _position >= _length.Value;
            return _position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        private void FillChunk()
        {
            if (_reachedEnd)
            {
                _chunk = Array.Empty<byte>();
                _chunkOffset = 0;
                return;
            }

            NTStatus status = _store.ReadFile(out var data, _handle, _position, _maxReadSize);
            if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_END_OF_FILE)
                throw new IOException($"SMB read failed: {status}");

            if (data.Length == 0)
            {
                _reachedEnd = true;
                _chunk = Array.Empty<byte>();
                _chunkOffset = 0;
                return;
            }

            _chunk = data;
            _chunkOffset = 0;
            if (status == NTStatus.STATUS_END_OF_FILE)
                _reachedEnd = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                base.Dispose(disposing);
                return;
            }

            if (disposing)
            {
                try
                {
                    _store.CloseFile(_handle);
                }
                catch
                {
                }

                try
                {
                    _store.Disconnect();
                }
                catch
                {
                }

                try
                {
                    _client.Logoff();
                }
                catch
                {
                }

                try
                {
                    _client.Disconnect();
                }
                catch
                {
                }
            }

            _disposed = true;
            base.Dispose(disposing);
        }
    }
}
