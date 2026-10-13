using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TS3PackageFusion;

public static class Sims3PackExtractor
{
    private const int CopyBufferSize = 128 * 1024;

    public static IReadOnlyList<string> ExtractPackages(string sourcePath, string destinationRoot)
    {
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(source, Encoding.ASCII, leaveOpen: true);
        if (source.Length < 17) throw new InvalidDataException("Fichier Sims3Pack trop court.");
        _ = reader.ReadUInt32();
        if (Encoding.ASCII.GetString(reader.ReadBytes(7)) != "TS3Pack") throw new InvalidDataException("Ce fichier n’est pas un Sims3Pack valide.");
        _ = reader.ReadUInt16();
        var manifestLength = reader.ReadInt32();
        if (manifestLength < 1 || manifestLength > source.Length - source.Position || manifestLength > 64 * 1024 * 1024)
            throw new InvalidDataException("Manifeste Sims3Pack invalide.");

        var manifest = reader.ReadBytes(manifestLength);
        var dataStart = source.Position;
        var entries = ReadManifest(manifest, source.Length - dataStart);
        var folderName = SafeFileName(Path.GetFileNameWithoutExtension(sourcePath));
        var destination = Path.Combine(destinationRoot, string.IsNullOrWhiteSpace(folderName) ? "Imported Sims3Pack" : folderName);
        Directory.CreateDirectory(destination);

        var written = new List<string>();
        foreach (var entry in entries)
        {
            var target = UniquePackagePath(destination, SafeFileName(Path.GetFileName(entry.Name)));
            source.Position = checked(dataStart + entry.Offset);
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            CopyExact(source, output, entry.Length);
            written.Add(target);
        }
        return written;
    }

    private static List<Sims3PackEntry> ReadManifest(byte[] manifest, long dataLength)
    {
        using var stream = new MemoryStream(manifest, writable: false);
        using var xmlReader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64 * 1024 * 1024 });
        var document = XDocument.Load(xmlReader, LoadOptions.None);
        var entries = new List<Sims3PackEntry>();
        foreach (var node in document.Descendants().Where(x => x.Name.LocalName == "PackagedFile"))
        {
            var name = node.Elements().FirstOrDefault(x => x.Name.LocalName == "Name")?.Value ?? "";
            if (!name.EndsWith(".package", StringComparison.OrdinalIgnoreCase)) continue;
            var offsetText = node.Elements().FirstOrDefault(x => x.Name.LocalName == "Offset")?.Value;
            var lengthText = node.Elements().FirstOrDefault(x => x.Name.LocalName == "Length")?.Value;
            if (!long.TryParse(offsetText, out var offset) || !long.TryParse(lengthText, out var length) || offset < 0 || length <= 0 || offset > dataLength || length > dataLength - offset)
                throw new InvalidDataException($"Entrée Sims3Pack invalide : {name}.");
            entries.Add(new Sims3PackEntry(name, offset, length));
        }
        if (entries.Count == 0) throw new InvalidDataException("Ce Sims3Pack ne contient aucun fichier .package.");
        return entries;
    }

    private static string UniquePackagePath(string directory, string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrWhiteSpace(stem)) stem = "package";
        var candidate = Path.Combine(directory, stem + ".package");
        for (var number = 2; File.Exists(candidate); number++) candidate = Path.Combine(directory, $"{stem} ({number}).package");
        return candidate;
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(value.Where(character => !invalid.Contains(character)).ToArray()).Trim();
        return clean.Length > 120 ? clean[..120] : clean;
    }

    private static void CopyExact(Stream input, Stream output, long length)
    {
        var buffer = new byte[CopyBufferSize];
        while (length > 0)
        {
            var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, length));
            if (read == 0) throw new EndOfStreamException("Sims3Pack tronqué pendant l’extraction.");
            output.Write(buffer, 0, read);
            length -= read;
        }
    }

    private sealed record Sims3PackEntry(string Name, long Offset, long Length);
}
