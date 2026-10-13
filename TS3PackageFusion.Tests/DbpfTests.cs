using TS3PackageFusion;
using Xunit;

namespace TS3PackageFusion.Tests;

public sealed class DbpfTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "TS3PackageFusion-tests-" + Guid.NewGuid().ToString("N"));
    public DbpfTests() => Directory.CreateDirectory(_folder);

    [Fact]
    public void Merge_writes_a_valid_package_and_preserves_unique_resources()
    {
        var first = Document("first.package", new ResourceKey(1, 2, 3, 4), 0x11);
        var second = Document("second.package", new ResourceKey(5, 6, 7, 8), 0x22);
        var target = Path.Combine(_folder, "merged.package");

        var result = Dbpf.MergeAtomically([first, second], target, new Dictionary<int, ConflictAction>());
        var merged = Dbpf.Read(target);

        Assert.Equal(2, result.Resources);
        Assert.Equal(0, result.Conflicts);
        Assert.Equal(2, merged.Entries.Count);
        Assert.Equal((byte)0x11, Dbpf.ReadResource(merged, merged.Entries.Single(x => x.Key == first.Entries[0].Key))[0]);
        Assert.Equal((byte)0x22, Dbpf.ReadResource(merged, merged.Entries.Single(x => x.Key == second.Entries[0].Key))[0]);
    }

    [Theory]
    [InlineData(false, 0x11)]
    [InlineData(true, 0x22)]
    public void Merge_honours_conflict_choice(bool useSecond, byte expected)
    {
        var key = new ResourceKey(1, 2, 3, 4);
        var target = Path.Combine(_folder, $"choice-{useSecond}.package");
        var action = useSecond ? ConflictAction.KeepNew : ConflictAction.KeepExisting;
        var result = Dbpf.MergeAtomically([Document("a.package", key, 0x11), Document("b.package", key, 0x22)], target, new Dictionary<int, ConflictAction> { [0] = action });
        var merged = Dbpf.Read(target);

        Assert.Equal(1, result.Resources);
        Assert.Equal(1, result.Conflicts);
        Assert.Equal(expected, Dbpf.ReadResource(merged, merged.Entries[0])[0]);
    }

    [Fact]
    public void Merge_automatically_deduplicates_identical_resources()
    {
        var key = new ResourceKey(1, 2, 3, 4);
        var target = Path.Combine(_folder, "identical.package");

        var result = Dbpf.MergeAtomically([Document("a.package", key, 0x11), Document("b.package", key, 0x11)], target, new Dictionary<int, ConflictAction>());

        Assert.Equal(1, result.Resources);
        Assert.Equal(0, result.Conflicts);
    }

    [Fact]
    public void Read_rejects_an_index_with_an_impossible_resource_count()
    {
        var file = Path.Combine(_folder, "invalid.package");
        var bytes = new byte[100];
        "DBPF"u8.CopyTo(bytes);
        BitConverter.GetBytes(2u).CopyTo(bytes, 4);
        BitConverter.GetBytes(1u).CopyTo(bytes, 36);
        BitConverter.GetBytes(96u).CopyTo(bytes, 64);
        BitConverter.GetBytes(4u).CopyTo(bytes, 44);
        File.WriteAllBytes(file, bytes);

        Assert.Throws<InvalidDataException>(() => Dbpf.Read(file));
    }

    [Fact]
    public void Sims3Pack_extraction_writes_its_embedded_package()
    {
        var source = Path.Combine(_folder, "test.sims3pack");
        var destination = Path.Combine(_folder, "extracted");
        var manifest = "<Root><PackagedFile><Name>My Mod.package</Name><Offset>0</Offset><Length>4</Length></PackagedFile></Root>"u8.ToArray();
        using (var stream = new FileStream(source, FileMode.CreateNew, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(0u); writer.Write("TS3Pack"u8); writer.Write((ushort)0); writer.Write(manifest.Length); writer.Write(manifest); writer.Write([1, 2, 3, 4]);
        }

        var extracted = Sims3PackExtractor.ExtractPackages(source, destination);

        var file = Assert.Single(extracted);
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(file));
    }

    private static PackageDocument Document(string name, ResourceKey key, byte payload) => new()
    {
        Path = name,
        Data = [payload],
        Entries = [new PackageEntry { Key = key, Offset = 0, Size = 1, MemorySize = 1, Packed = 0 }]
    };

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }
}
