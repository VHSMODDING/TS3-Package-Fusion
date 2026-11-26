using TS3PackageFusion;
using Xunit;

namespace TS3PackageFusion.Tests;

public sealed class UpdateDetectorTests
{
    // Les documents ne pointent sur aucun fichier : la détection ne lit que l'index et le nom.
    private static PackageDocument Package(string name, params int[] resources) => new()
    {
        Path = Path.Combine(Path.GetTempPath(), "TS3PackageFusion-detector", name),
        Entries = resources.Select(id => new PackageEntry { Key = new ResourceKey(0x034AEECB, 0, 0, (uint)id), Size = 4, MemorySize = 4 }).ToList()
    };

    private static int[] Range(int start, int count) => Enumerable.Range(start, count).ToArray();

    [Fact]
    public void Detects_a_new_version_of_the_same_mod()
    {
        var older = Package("Yoga Mod [by Twinsimming] V2.0.package", Range(1, 10));
        var newer = Package("Yoga Mod [by Twinsimming] V3.0.package", Range(1, 10));

        var update = Assert.Single(UpdateDetector.Find([older, newer]));

        Assert.Same(older, update.Older);
        Assert.Same(newer, update.Newer);
        Assert.Equal(UpdateConfidence.Confident, update.Confidence);
        Assert.Contains("3.0", update.Reason);
    }

    [Fact]
    public void Keeps_the_highest_version_of_a_chain_and_reports_the_others()
    {
        var v1 = Package("Route Fix v1.package", Range(1, 20));
        var v9 = Package("Route Fix v9.package", Range(1, 20));
        var v10 = Package("Route Fix v10.package", Range(1, 20));

        var updates = UpdateDetector.Find([v1, v10, v9]);

        Assert.Equal(2, updates.Count);
        Assert.All(updates, update => Assert.Same(v10, update.Newer));
        Assert.Equal([v1, v9], updates.Select(update => update.Older).OrderBy(package => package.Name).ToArray());
    }

    [Fact]
    public void Detects_a_renamed_update_from_shared_resources()
    {
        var older = Package("AwesomeMod.package", Range(1, 20));
        var newer = Package("Totalement autre chose.package", Range(1, 19));

        var update = Assert.Single(UpdateDetector.Find([older, newer]));

        Assert.Equal(UpdateConfidence.Probable, update.Confidence);
        Assert.True(update.SharedRatio > 0.9);
    }

    [Fact]
    public void Ignores_unrelated_mods_that_share_one_override()
    {
        var first = Package("NoTaxiBoat.package", Range(1, 40));
        var second = Package("SmoothPatchMac.package", [40, .. Range(100, 39)]);

        Assert.Empty(UpdateDetector.Find([first, second]));
    }

    [Fact]
    public void Ignores_unrelated_mods_without_shared_resources()
    {
        Assert.Empty(UpdateDetector.Find([Package("ObjectHeight.package", Range(1, 5)), Package("FloTheory_SeatedOnly.package", Range(50, 5))]));
    }

    [Fact]
    public void Detects_a_redownloaded_copy_with_the_same_name()
    {
        var first = Package("Overwatch.package", Range(1, 30));
        var second = Package("Overwatch (1).package", Range(1, 30));

        var update = Assert.Single(UpdateDetector.Find([first, second]));

        Assert.Same(first, update.Older);      // à versions égales, le dernier ajouté gagne
        Assert.Same(second, update.Newer);
        Assert.Equal(UpdateConfidence.Confident, update.Confidence);
    }

    [Theory]
    [InlineData("Yoga Mod V3.0.package", "yogamod")]
    [InlineData("Yoga Mod [by Alunn].package.package", "yogamodbyalunn")]
    [InlineData("Overwatch (1).package", "overwatch")]
    [InlineData("Mod - updated v1.2.3.package", "mod")]
    public void Canonical_name_drops_version_noise_and_punctuation(string fileName, string expected) =>
        Assert.Equal(expected, UpdateDetector.Canonical(fileName));

    [Theory]
    [InlineData("Mod v9.package", new[] { 9 })]
    [InlineData("Mod V3.0.package", new[] { 3, 0 })]
    [InlineData("Mod 1.1.2.package", new[] { 1, 1, 2 })]
    [InlineData("Route Fix Flavor 4.package", null)]
    public void Version_is_read_from_the_file_name(string fileName, int[]? expected) =>
        Assert.Equal(expected, UpdateDetector.ParseVersion(fileName));

    [Fact]
    public void A_single_package_is_never_an_update() => Assert.Empty(UpdateDetector.Find([Package("Mod.package", Range(1, 5))]));
}
