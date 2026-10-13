using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TS3PackageFusion;

public readonly record struct ResourceKey(uint Type, uint Group, uint High, uint Low)
{
    public override string ToString() => $"T:{Type:X8} G:{Group:X8} I:{High:X8}{Low:X8}";
}

public sealed class PackageEntry
{
    public ResourceKey Key { get; init; }
    public uint Offset { get; set; }
    public uint Size { get; init; }
    public uint MemorySize { get; init; }
    public uint Packed { get; init; }
}

public sealed class PackageDocument
{
    public string Path { get; init; } = "";
    // Utilisé uniquement par les tests et les documents créés en mémoire.
    public byte[] Data { get; init; } = [];
    public List<PackageEntry> Entries { get; init; } = [];
    public string Name => System.IO.Path.GetFileName(Path);
    public override string ToString() => $"{Name} - {Entries.Count:N0} ressources";
    public static PackageDocument Read(string path) => Dbpf.Read(path);
}

public sealed record MergeResult(int Resources, int Conflicts, int Files = 1);
public sealed record MergeProgress(ulong CopiedBytes, ulong TotalBytes, int CurrentFile, int TotalFiles);
public enum ConflictAction { KeepExisting, KeepNew }

public static class Dbpf
{
    private const ulong MaxDbpfBytes = uint.MaxValue;
    private const int BufferSize = 1024 * 1024;

    public static bool ResourcesEquivalent(PackageDocument firstDocument, PackageEntry first, PackageDocument secondDocument, PackageEntry second)
    {
        if (first.Size != second.Size || first.MemorySize != second.MemorySize || first.Packed != second.Packed) return false;

        using var firstStream = OpenResourceStream(firstDocument);
        using var secondStream = OpenResourceStream(secondDocument);
        firstStream.Position = first.Offset;
        secondStream.Position = second.Offset;
        var firstBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var secondBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var remaining = (ulong)first.Size;
            while (remaining > 0)
            {
                var requested = (int)Math.Min((ulong)BufferSize, remaining);
                ReadExactly(firstStream, firstBuffer, requested);
                ReadExactly(secondStream, secondBuffer, requested);
                if (!firstBuffer.AsSpan(0, requested).SequenceEqual(secondBuffer.AsSpan(0, requested))) return false;
                remaining -= (uint)requested;
            }
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(firstBuffer);
            ArrayPool<byte>.Shared.Return(secondBuffer);
        }
    }

    public static byte[] ReadResource(PackageDocument document, PackageEntry entry)
    {
        if (entry.Size > int.MaxValue) throw new InvalidDataException("La ressource est trop grande pour être chargée en mémoire.");
        var bytes = new byte[(int)entry.Size];
        using var stream = OpenResourceStream(document);
        stream.Position = entry.Offset;
        ReadExactly(stream, bytes, bytes.Length);
        return bytes;
    }

    private static uint U(byte[] bytes, int position)
    {
        if (position < 0 || position > bytes.Length - 4) throw new InvalidDataException("Index DBPF tronqué.");
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position, 4));
    }

    public static PackageDocument Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Fichier introuvable.", path);
        if ((ulong)info.Length > MaxDbpfBytes) throw new InvalidDataException("Le package dépasse la limite de 4 Go du format DBPF.");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
        var header = new byte[96];
        ReadExactly(stream, header, header.Length);
        if (!header.AsSpan(0, 4).SequenceEqual("DBPF"u8)) throw new InvalidDataException($"{System.IO.Path.GetFileName(path)} n’est pas un package DBPF valide.");

        var count = U(header, 36);
        var size = U(header, 44);
        var indexOffset = U(header, 64);
        var start = indexOffset != 0 ? indexOffset : U(header, 40);
        if (count == 0) return new PackageDocument { Path = path };
        if (start < 96 || (ulong)start > (ulong)info.Length || (ulong)size > (ulong)info.Length - start) throw new InvalidDataException("Index DBPF invalide.");
        if (size > int.MaxValue) throw new InvalidDataException("L’index DBPF est trop grand pour être chargé.");

        stream.Position = start;
        var index = new byte[(int)size];
        ReadExactly(stream, index, index.Length);
        var flags = U(index, 0);
        if ((flags & ~7u) != 0) throw new InvalidDataException("Variante DBPF non prise en charge.");
        var shared = ((flags & 1) != 0 ? 1 : 0) + ((flags & 2) != 0 ? 1 : 0) + ((flags & 4) != 0 ? 1 : 0);
        var minimumSize = 4L + shared * 4L + (long)count * (32L - shared * 4L);
        if (size < minimumSize) throw new InvalidDataException("Index DBPF tronqué ou nombre de ressources incohérent.");

        var position = 4;
        uint? type = null, group = null, high = null;
        if ((flags & 1) != 0) { type = U(index, position); position += 4; }
        if ((flags & 2) != 0) { group = U(index, position); position += 4; }
        if ((flags & 4) != 0) { high = U(index, position); position += 4; }
        var entries = new List<PackageEntry>();
        for (uint i = 0; i < count; i++)
        {
            var resourceType = type ?? U(index, position); if (type is null) position += 4;
            var resourceGroup = group ?? U(index, position); if (group is null) position += 4;
            var resourceHigh = high ?? U(index, position); if (high is null) position += 4;
            var low = U(index, position); position += 4;
            var offset = U(index, position); position += 4;
            var fileSize = U(index, position) & 0x7fffffff; position += 4;
            var memorySize = U(index, position); position += 4;
            var packed = U(index, position); position += 4;
            if ((ulong)offset + fileSize > (ulong)info.Length) throw new InvalidDataException("Ressource DBPF hors limites.");
            entries.Add(new PackageEntry { Key = new ResourceKey(resourceType, resourceGroup, resourceHigh, low), Offset = offset, Size = fileSize, MemorySize = memorySize, Packed = packed });
        }
        return new PackageDocument { Path = path, Entries = entries };
    }

    public static MergeResult MergeAtomically(IReadOnlyList<PackageDocument> documents, string output, IReadOnlyDictionary<int, ConflictAction> choices, IProgress<MergeProgress>? progress = null)
    {
        var kept = ResolveResources(documents, choices, out var conflicts);
        if (EstimateSize(kept) > MaxDbpfBytes) throw new InvalidDataException("La fusion dépasse 4 Go : crée plusieurs packages.");
        var totalBytes = PayloadSize(kept);
        ulong copiedBytes = 0;
        ulong lastReported = 0;
        progress?.Report(new MergeProgress(0, totalBytes, 1, 1));
        WritePackage(kept, output, copied =>
        {
            copiedBytes += copied;
            if (copiedBytes == totalBytes || copiedBytes - lastReported >= 16UL * 1024 * 1024)
            {
                lastReported = copiedBytes;
                progress?.Report(new MergeProgress(copiedBytes, totalBytes, 1, 1));
            }
        });
        return new MergeResult(kept.Count, conflicts);
    }

    public static bool RequiresSplit(IReadOnlyList<PackageDocument> documents, IReadOnlyDictionary<int, ConflictAction> choices) => EstimateSize(ResolveResources(documents, choices, out _)) > MaxDbpfBytes;

    public static MergeResult MergeSplitAtomically(IReadOnlyList<PackageDocument> documents, string output, IReadOnlyDictionary<int, ConflictAction> choices, IProgress<MergeProgress>? progress = null)
    {
        var kept = ResolveResources(documents, choices, out var conflicts);
        var groups = new List<List<(PackageDocument Document, PackageEntry Entry)>>();
        var group = new List<(PackageDocument Document, PackageEntry Entry)>();
        ulong payload = 0;
        foreach (var resource in kept)
        {
            var candidate = 96UL + 4UL + (ulong)(group.Count + 1) * 32UL + payload + resource.Entry.Size;
            if (candidate > MaxDbpfBytes && group.Count > 0)
            {
                groups.Add(group);
                group = [];
                payload = 0;
            }
            group.Add(resource);
            payload += resource.Entry.Size;
        }
        if (group.Count > 0) groups.Add(group);
        if (groups.Count <= 1) return MergeAtomically(documents, output, choices, progress);

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(output))!;
        var stem = System.IO.Path.GetFileNameWithoutExtension(output);
        var extension = System.IO.Path.GetExtension(output);
        var totalBytes = PayloadSize(kept);
        ulong copiedBytes = 0;
        ulong lastReported = 0;
        progress?.Report(new MergeProgress(0, totalBytes, 1, groups.Count));
        for (var index = 0; index < groups.Count; index++)
        {
            var currentFile = index + 1;
            WritePackage(groups[index], System.IO.Path.Combine(directory, $"{stem}_{currentFile:00}{extension}"), copied =>
            {
                copiedBytes += copied;
                if (copiedBytes == totalBytes || copiedBytes - lastReported >= 16UL * 1024 * 1024)
                {
                    lastReported = copiedBytes;
                    progress?.Report(new MergeProgress(copiedBytes, totalBytes, currentFile, groups.Count));
                }
            });
        }
        return new MergeResult(kept.Count, conflicts, groups.Count);
    }

    private static List<(PackageDocument Document, PackageEntry Entry)> ResolveResources(IReadOnlyList<PackageDocument> documents, IReadOnlyDictionary<int, ConflictAction> choices, out int conflicts)
    {
        var kept = new List<(PackageDocument Document, PackageEntry Entry)>();
        var positions = new Dictionary<ResourceKey, int>();
        conflicts = 0;
        foreach (var document in documents)
        foreach (var entry in document.Entries)
        {
            if (positions.TryGetValue(entry.Key, out var existing))
            {
                var current = kept[existing];
                if (ResourcesEquivalent(current.Document, current.Entry, document, entry)) continue;
                if (!choices.TryGetValue(conflicts, out var action)) throw new InvalidOperationException("Décision de conflit manquante.");
                if (action == ConflictAction.KeepNew) kept[existing] = (document, entry);
                conflicts++;
            }
            else { positions[entry.Key] = kept.Count; kept.Add((document, entry)); }
        }
        return kept;
    }

    private static ulong EstimateSize(IReadOnlyList<(PackageDocument Document, PackageEntry Entry)> entries)
    {
        ulong total = 96UL + 4UL + (ulong)entries.Count * 32UL;
        foreach (var (_, entry) in entries) total += entry.Size;
        return total;
    }

    private static ulong PayloadSize(IReadOnlyList<(PackageDocument Document, PackageEntry Entry)> entries)
    {
        ulong total = 0;
        foreach (var (_, entry) in entries) total += entry.Size;
        return total;
    }

    private static void WritePackage(IReadOnlyList<(PackageDocument Document, PackageEntry Entry)> resources, string output, Action<uint>? onBytesCopied = null)
    {
        var fullOutput = System.IO.Path.GetFullPath(output);
        var directory = System.IO.Path.GetDirectoryName(fullOutput) ?? throw new InvalidOperationException("Dossier de sortie invalide.");
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        var temporary = System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(output)}.{Guid.NewGuid():N}.ts3pf-tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[96]);
                var written = new List<PackageEntry>(resources.Count);
                foreach (var (document, entry) in resources)
                {
                    var copy = new PackageEntry { Key = entry.Key, Offset = checked((uint)stream.Position), Size = entry.Size, MemorySize = entry.MemorySize, Packed = entry.Packed };
                    CopyResource(document, entry, stream, onBytesCopied);
                    written.Add(copy);
                }
                var index = checked((uint)stream.Position);
                writer.Write(0u);
                foreach (var entry in written)
                {
                    writer.Write(entry.Key.Type); writer.Write(entry.Key.Group); writer.Write(entry.Key.High); writer.Write(entry.Key.Low);
                    writer.Write(entry.Offset); writer.Write(entry.Size | 0x80000000); writer.Write(entry.MemorySize); writer.Write(entry.Packed);
                }
                stream.Position = 0;
                writer.Write("DBPF"u8); writer.Write(2u); writer.Write(0u);
                stream.Position = 36; writer.Write(written.Count);
                stream.Position = 44; writer.Write(checked((uint)(4L + written.Count * 32L)));
                stream.Position = 60; writer.Write(3u);
                stream.Position = 64; writer.Write(index);
                writer.Flush(); stream.Flush(true);
            }
            _ = Read(temporary);
            File.Move(temporary, fullOutput, true);
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    private static Stream OpenResourceStream(PackageDocument document)
    {
        if (document.Data.Length > 0) return new MemoryStream(document.Data, writable: false);
        return new FileStream(document.Path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
    }

    private static void CopyResource(PackageDocument document, PackageEntry entry, Stream destination, Action<uint>? onBytesCopied)
    {
        using var source = OpenResourceStream(document);
        source.Position = entry.Offset;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var remaining = (ulong)entry.Size;
            while (remaining > 0)
            {
                var requested = (int)Math.Min((ulong)BufferSize, remaining);
                ReadExactly(source, buffer, requested);
                destination.Write(buffer, 0, requested);
                onBytesCopied?.Invoke((uint)requested);
                remaining -= (uint)requested;
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static void ReadExactly(Stream stream, byte[] buffer, int count)
    {
        var offset = 0;
        while (offset < count)
        {
            var read = stream.Read(buffer, offset, count - offset);
            if (read == 0) throw new EndOfStreamException("Lecture DBPF incomplète.");
            offset += read;
        }
    }
}
