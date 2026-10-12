namespace DiskHealth.Services;

/// <summary>
/// Scoped NTFS MFT scanner: finds FILE records that are no longer in-use (deleted)
/// and still expose a $FILE_NAME (and optionally non-resident $DATA runs).
/// Not a full forensic undelete suite — clusters may already be reused.
/// </summary>
public static class MftDeletedScanner
{
    private const int FileFlagBackupSemantics = 0x02000000;
    private const int MaxRecordsDefault = 200_000;
    private const int MaxResults = 2_000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        FileAccess dwDesiredAccess,
        FileShare dwShareMode,
        IntPtr lpSecurityAttributes,
        FileMode dwCreationDisposition,
        FileAttributes dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFilePointerEx(SafeFileHandle hFile, long liDistanceToMove, out long lpNewFilePointer, uint dwMoveMethod);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer, int nNumberOfBytesToRead, out int lpNumberOfBytesRead, IntPtr lpOverlapped);

    /// <summary>Scans the volume MFT for deleted file records (best-effort).</summary>
    public static async Task<IReadOnlyList<RecoverableFileInfo>> ScanAsync(
        string driveLetter,
        IProgress<(long Records, int Found)>? progress = null,
        CancellationToken ct = default)
    {
        driveLetter = driveLetter.Trim().TrimEnd(':').ToUpperInvariant();
        string path = $@"\\.\{driveLetter}:";

        return await Task.Run(() =>
        {
            using var handle = CreateFile(
                path,
                FileAccess.Read,
                FileShare.ReadWrite,
                IntPtr.Zero,
                FileMode.Open,
                (FileAttributes)FileFlagBackupSemantics,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == 5)
                {
                    throw new UnauthorizedAccessException("Administrator privileges required to read the NTFS volume.");
                }

                throw new IOException($"Cannot open volume {driveLetter}: (Win32 {err}).");
            }

            var boot = new byte[512];
            if (!ReadAt(handle, 0, boot) || boot[3] != (byte)'N' || boot[4] != (byte)'T' || boot[5] != (byte)'F' || boot[6] != (byte)'S')
            {
                throw new InvalidOperationException("Volume does not look like NTFS (missing boot signature).");
            }

            int bytesPerSector = BitConverter.ToUInt16(boot, 0x0B);
            int sectorsPerCluster = boot[0x0D];
            if (bytesPerSector <= 0 || sectorsPerCluster <= 0)
            {
                throw new InvalidOperationException("Invalid NTFS boot sector geometry.");
            }

            int bytesPerCluster = bytesPerSector * sectorsPerCluster;
            long mftLcn = BitConverter.ToInt64(boot, 0x30);
            sbyte clustersPerRecord = unchecked((sbyte)boot[0x40]);
            int recordSize = clustersPerRecord < 0
                ? 1 << -clustersPerRecord
                : clustersPerRecord * bytesPerCluster;
            if (recordSize < 1024 || recordSize > 65536)
            {
                recordSize = 1024;
            }

            long mftOffset = mftLcn * bytesPerCluster;
            var results = new List<RecoverableFileInfo>();
            var record = new byte[recordSize];
            long recordsScanned = 0;

            for (int i = 0; i < MaxRecordsDefault && results.Count < MaxResults; i++)
            {
                ct.ThrowIfCancellationRequested();
                long offset = mftOffset + (long)i * recordSize;
                if (!ReadAt(handle, offset, record))
                {
                    break;
                }

                recordsScanned++;
                if (recordsScanned % 2048 == 0)
                {
                    progress?.Report((recordsScanned, results.Count));
                }

                if (record[0] != (byte)'F' || record[1] != (byte)'I' || record[2] != (byte)'L' || record[3] != (byte)'E')
                {
                    // End of allocated MFT region often turns to zeros
                    if (i > 16 && record.AsSpan(0, 16).SequenceEqual(new byte[16]))
                    {
                        // Keep scanning a bit; sparse holes exist
                        continue;
                    }

                    continue;
                }

                ushort flags = BitConverter.ToUInt16(record, 0x16);
                bool inUse = (flags & 0x0001) != 0;
                bool isDir = (flags & 0x0002) != 0;
                if (inUse || isDir)
                {
                    continue;
                }

                if (!TryParseDeletedRecord(record, bytesPerCluster, out var info))
                {
                    continue;
                }

                results.Add(info);
            }

            progress?.Report((recordsScanned, results.Count));
            return (IReadOnlyList<RecoverableFileInfo>)results;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Parses one MFT FILE record buffer for deleted-file metadata (tests).</summary>
    public static bool TryParseDeletedRecord(byte[] record, int bytesPerCluster, out RecoverableFileInfo info)
    {
        info = null!;
        if (record.Length < 0x30 || record[0] != (byte)'F')
        {
            return false;
        }

        ushort flags = BitConverter.ToUInt16(record, 0x16);
        if ((flags & 0x0001) != 0)
        {
            return false; // still in use
        }

        ushort firstAttr = BitConverter.ToUInt16(record, 0x14);
        if (firstAttr < 0x30 || firstAttr >= record.Length)
        {
            return false;
        }

        string? fileName = null;
        long estimatedSize = 0;
        var runs = new List<(long Lcn, long Clusters)>();
        int pos = firstAttr;

        while (pos + 8 < record.Length)
        {
            uint type = BitConverter.ToUInt32(record, pos);
            if (type == 0xFFFFFFFF)
            {
                break;
            }

            int len = BitConverter.ToInt32(record, pos + 4);
            if (len < 8 || pos + len > record.Length)
            {
                break;
            }

            byte nonResident = record[pos + 8];
            if (type == 0x30) // $FILE_NAME
            {
                if (nonResident == 0)
                {
                    int valueLen = BitConverter.ToInt32(record, pos + 0x10);
                    int valueOff = BitConverter.ToUInt16(record, pos + 0x14);
                    int nameInfo = pos + valueOff;
                    // FILE_NAME attr: parent ref 8, times 32, sizes 16, flags 4, name_len 1, ns 1, name
                    if (nameInfo + 0x42 <= pos + len && valueLen > 0x42)
                    {
                        int nameLen = record[nameInfo + 0x40];
                        int nameStart = nameInfo + 0x42;
                        if (nameLen > 0 && nameStart + nameLen * 2 <= pos + len)
                        {
                            string n = Encoding.Unicode.GetString(record, nameStart, nameLen * 2);
                            // Prefer Win32 namespace names (longer / not DOS 8.3) when multiple exist
                            if (fileName == null || n.Length > fileName.Length)
                            {
                                fileName = n;
                            }
                        }
                    }
                }
            }
            else if (type == 0x80) // $DATA (unnamed preferred)
            {
                int nameLength = record[pos + 0x09];
                if (nameLength == 0)
                {
                    if (nonResident == 0)
                    {
                        estimatedSize = BitConverter.ToInt32(record, pos + 0x10);
                    }
                    else
                    {
                        estimatedSize = BitConverter.ToInt64(record, pos + 0x30); // real size
                        int runOff = BitConverter.ToUInt16(record, pos + 0x20);
                        ParseDataRuns(record.AsSpan(pos + runOff, len - runOff), runs);
                    }
                }
            }

            pos += len;
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        string ext = Path.GetExtension(fileName);
        int chance = runs.Count > 0 ? 55 : 25;
        if (estimatedSize > 0 && runs.Count > 0)
        {
            chance = 70;
        }

        long firstOffset = 0;
        if (runs.Count > 0 && bytesPerCluster > 0)
        {
            firstOffset = runs[0].Lcn * bytesPerCluster;
        }

        info = new RecoverableFileInfo
        {
            DisplayName = fileName,
            FileType = string.IsNullOrEmpty(ext) ? ".bin" : ext.ToLowerInvariant(),
            EstimatedSize = estimatedSize,
            RecoveryChancePercent = chance,
            Offset = firstOffset,
            PathHint = fileName,
            SourceKind = RecoverySourceKind.MftDeleted,
            DataRuns = runs.Count > 0 ? runs : null,
            BytesPerCluster = bytesPerCluster
        };
        return true;
    }

    /// <summary>Decodes NTFS data runs into LCN + cluster counts (tests).</summary>
    public static void ParseDataRuns(ReadOnlySpan<byte> runs, List<(long Lcn, long Clusters)> output)
    {
        long lcn = 0;
        int i = 0;
        while (i < runs.Length)
        {
            byte header = runs[i++];
            if (header == 0)
            {
                break;
            }

            int lengthSize = header & 0x0F;
            int offsetSize = header >> 4;
            if (i + lengthSize + offsetSize > runs.Length)
            {
                break;
            }

            long clusterCount = ReadLittleEndian(runs.Slice(i, lengthSize));
            i += lengthSize;
            long offset = offsetSize == 0 ? 0 : ReadSignedLittleEndian(runs.Slice(i, offsetSize));
            i += offsetSize;
            if (offsetSize == 0)
            {
                // sparse — skip
                continue;
            }

            lcn += offset;
            if (clusterCount > 0 && lcn >= 0)
            {
                output.Add((lcn, clusterCount));
            }
        }
    }

    /// <summary>Reads file contents from data runs on an open volume handle.</summary>
    public static byte[]? ReadFromRuns(SafeFileHandle volume, RecoverableFileInfo file, int maxBytes = 50 * 1024 * 1024)
    {
        if (file.DataRuns == null || file.DataRuns.Count == 0 || file.BytesPerCluster <= 0)
        {
            return null;
        }

        long remaining = file.EstimatedSize > 0 ? Math.Min(file.EstimatedSize, maxBytes) : maxBytes;
        using var ms = new MemoryStream();
        var buffer = new byte[Math.Min(file.BytesPerCluster * 64, 1024 * 1024)];
        foreach (var (lcn, clusters) in file.DataRuns)
        {
            long bytes = clusters * file.BytesPerCluster;
            long toCopy = Math.Min(bytes, remaining);
            long offset = lcn * file.BytesPerCluster;
            long copied = 0;
            while (copied < toCopy)
            {
                int chunk = (int)Math.Min(buffer.Length, toCopy - copied);
                if (!SetFilePointerEx(volume, offset + copied, out _, 0))
                {
                    return ms.Length > 0 ? ms.ToArray() : null;
                }

                if (!ReadFile(volume, buffer, chunk, out int read, IntPtr.Zero) || read <= 0)
                {
                    return ms.Length > 0 ? ms.ToArray() : null;
                }

                ms.Write(buffer, 0, read);
                copied += read;
            }

            remaining -= toCopy;
            if (remaining <= 0)
            {
                break;
            }
        }

        return ms.ToArray();
    }

    private static bool ReadAt(SafeFileHandle handle, long offset, byte[] buffer)
    {
        if (!SetFilePointerEx(handle, offset, out _, 0))
        {
            return false;
        }

        return ReadFile(handle, buffer, buffer.Length, out int read, IntPtr.Zero) && read == buffer.Length;
    }

    private static long ReadLittleEndian(ReadOnlySpan<byte> data)
    {
        long v = 0;
        for (int i = 0; i < data.Length; i++)
        {
            v |= (long)data[i] << (8 * i);
        }

        return v;
    }

    private static long ReadSignedLittleEndian(ReadOnlySpan<byte> data)
    {
        long v = ReadLittleEndian(data);
        int bits = data.Length * 8;
        if (bits < 64 && (v & (1L << (bits - 1))) != 0)
        {
            v |= -1L << bits;
        }

        return v;
    }
}
