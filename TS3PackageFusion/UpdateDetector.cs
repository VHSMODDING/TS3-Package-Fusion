using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TS3PackageFusion;

/// <summary>Niveau de certitude d'une mise à jour détectée.</summary>
public enum UpdateConfidence { Probable, Confident }

/// <summary>Un package qui paraît être une version plus ancienne d'un autre package de la liste.</summary>
public sealed record PackageUpdate(PackageDocument Older, PackageDocument Newer, UpdateConfidence Confidence, double SharedRatio, string Reason)
{
    public string SharedText => SharedRatio <= 0 ? "aucune ressource commune" : $"{SharedRatio * 100:N0} % de ressources communes";
    public override string ToString() => $"{Newer.Name} remplace {Older.Name} — {Reason}, {SharedText}";
}

/// <summary>
/// Repère les packages de la liste qui sont deux versions du même mod : même nom à la version près,
/// ou large recouvrement de clés de ressources. Aucun contenu n'est lu, seuls l'index et le nom comptent.
/// </summary>
public static class UpdateDetector
{
    // « v2 », « V 1.0 », « version 3 », « 1.1.2 » : ce qui distingue deux versions d'un même nom.
    private static readonly Regex VersionToken = new(
        @"(?<![a-z0-9])v(?:er|ersion)?[ ._-]?(\d+(?:[._-]\d+)*)(?![a-z0-9])|(?<![a-z0-9._-])(\d+(?:[._]\d+)+)(?![a-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // mots qui ne disent rien de l'identité du mod
    private static readonly Regex NoiseWord = new(
        @"\b(updated?|update|final|latest|fixed|copy|copie|nouveau|nouvelle|maj)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // suffixes de doublon : « (1) », « - 2 », « .package.package »
    private static readonly Regex DuplicateSuffix = new(@"\((\d+)\)\s*$|[ _-]+\d+\s*$", RegexOptions.Compiled);

    private const double MinSharedRatioAlone = 0.75;   // sans indice dans le nom, il faut un très large recouvrement
    private const double MinSharedRatioNearName = 0.2; // nom proche : un recouvrement partiel suffit
    private const int MinSharedResources = 4;          // deux mods minuscules peuvent partager une ressource par hasard

    public static List<PackageUpdate> Find(IReadOnlyList<PackageDocument> packages)
    {
        var updates = new List<PackageUpdate>();
        if (packages.Count < 2) return updates;

        var keys = packages.Select(p => new HashSet<ResourceKey>(p.Entries.Select(e => e.Key))).ToArray();
        var canonical = packages.Select(p => Canonical(p.Name)).ToArray();
        var versions = packages.Select(p => ParseVersion(p.Name)).ToArray();
        var times = packages.Select(WriteTime).ToArray();

        // paires candidates + composantes connexes : trois versions d'un mod forment un seul groupe
        var pairs = new Dictionary<(int, int), (UpdateConfidence Confidence, double Ratio)>();
        var parent = Enumerable.Range(0, packages.Count).ToArray();
        for (var a = 0; a < packages.Count; a++)
        for (var b = a + 1; b < packages.Count; b++)
        {
            var verdict = Compare(canonical[a], canonical[b], keys[a], keys[b]);
            if (verdict is null) continue;
            pairs[(a, b)] = verdict.Value;
            Union(parent, a, b);
        }
        if (pairs.Count == 0) return updates;

        foreach (var group in Enumerable.Range(0, packages.Count).GroupBy(i => Find(parent, i)))
        {
            var members = group.ToArray();
            if (members.Length < 2) continue;
            var newest = members[0];
            foreach (var member in members.Skip(1))
                if (IsNewer(member, newest, versions, times)) newest = member;

            foreach (var older in members.Where(index => index != newest))
            {
                var pair = pairs.TryGetValue((Math.Min(older, newest), Math.Max(older, newest)), out var found)
                    ? found
                    : (Confidence: UpdateConfidence.Probable, Ratio: SharedRatio(keys[older], keys[newest]));
                updates.Add(new PackageUpdate(packages[older], packages[newest], pair.Confidence, pair.Ratio,
                    Reason(older, newest, versions, times)));
            }
        }
        return updates
            .OrderByDescending(update => update.Confidence)
            .ThenBy(update => update.Newer.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Décide si deux packages sont deux versions du même mod, et avec quelle certitude.</summary>
    private static (UpdateConfidence, double)? Compare(string firstName, string secondName, HashSet<ResourceKey> firstKeys, HashSet<ResourceKey> secondKeys)
    {
        var ratio = SharedRatio(firstKeys, secondKeys);
        var shared = firstKeys.Count(secondKeys.Contains);
        var sameName = firstName.Length >= 4 && firstName == secondName;
        var nearName = !sameName && firstName.Length >= 8 && secondName.Length >= 8
                       && (firstName.StartsWith(secondName, StringComparison.Ordinal) || secondName.StartsWith(firstName, StringComparison.Ordinal));

        if (sameName) return (UpdateConfidence.Confident, ratio);
        if (nearName && ratio >= MinSharedRatioNearName) return (UpdateConfidence.Confident, ratio);
        if (ratio >= MinSharedRatioAlone && shared >= MinSharedResources) return (UpdateConfidence.Probable, ratio);
        if (nearName) return (UpdateConfidence.Probable, ratio);
        return null;
    }

    private static double SharedRatio(HashSet<ResourceKey> first, HashSet<ResourceKey> second)
    {
        var smaller = Math.Min(first.Count, second.Count);
        if (smaller == 0) return 0;
        return (double)first.Count(second.Contains) / smaller;
    }

    private static bool IsNewer(int candidate, int reference, IReadOnlyList<int[]?> versions, IReadOnlyList<DateTime?> times)
    {
        var byVersion = CompareVersions(versions[candidate], versions[reference]);
        if (byVersion != 0) return byVersion > 0;
        if (times[candidate] is { } a && times[reference] is { } b && Math.Abs((a - b).TotalSeconds) > 1) return a > b;
        if (versions[candidate] is not null && versions[reference] is null) return true;
        if (versions[candidate] is null && versions[reference] is not null) return false;
        return candidate > reference;   // à défaut : le dernier ajouté
    }

    private static string Reason(int older, int newer, IReadOnlyList<int[]?> versions, IReadOnlyList<DateTime?> times)
    {
        if (CompareVersions(versions[newer], versions[older]) > 0)
            return $"version {Format(versions[newer])} au lieu de {Format(versions[older])}";
        if (times[newer] is { } a && times[older] is { } b && Math.Abs((a - b).TotalSeconds) > 1)
            return $"fichier plus récent ({a:dd/MM/yyyy})";
        if (versions[newer] is not null && versions[older] is null) return $"version {Format(versions[newer])} face à un fichier sans version";
        return "ajouté après";
    }

    private static int CompareVersions(int[]? first, int[]? second)
    {
        if (first is null || second is null) return 0;
        for (var i = 0; i < Math.Max(first.Length, second.Length); i++)
        {
            var a = i < first.Length ? first[i] : 0;
            var b = i < second.Length ? second[i] : 0;
            if (a != b) return a.CompareTo(b);
        }
        return 0;
    }

    private static string Format(int[]? version) => version is null ? "?" : string.Join(".", version);

    /// <summary>Numéro de version lu dans le nom de fichier (le dernier rencontré, celui qui qualifie le mod).</summary>
    public static int[]? ParseVersion(string fileName)
    {
        var name = StripExtensions(fileName);
        int[]? result = null;
        foreach (Match match in VersionToken.Matches(name))
        {
            var digits = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            var parts = digits.Split('.', '_', '-').Select(part => int.TryParse(part, out var value) ? value : 0).ToArray();
            if (parts.Length > 0) result = parts;
        }
        return result;
    }

    /// <summary>Nom réduit à l'identité du mod : sans extension, version, mot creux ni ponctuation.</summary>
    public static string Canonical(string fileName)
    {
        var name = StripExtensions(fileName).ToLowerInvariant();
        name = VersionToken.Replace(name, " ");
        name = NoiseWord.Replace(name, " ");
        name = DuplicateSuffix.Replace(name, " ");
        var builder = new StringBuilder(name.Length);
        foreach (var character in name) if (char.IsLetterOrDigit(character)) builder.Append(character);
        return builder.ToString();
    }

    private static string StripExtensions(string fileName)
    {
        var name = fileName;
        // certains mods arrivent en « Mod.package.package »
        while (name.EndsWith(".package", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".sims3pack", StringComparison.OrdinalIgnoreCase))
            name = Path.GetFileNameWithoutExtension(name);
        return name;
    }

    private static DateTime? WriteTime(PackageDocument package)
    {
        try { return package.Path.Length > 0 && File.Exists(package.Path) ? File.GetLastWriteTimeUtc(package.Path) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static int Find(int[] parent, int index)
    {
        while (parent[index] != index) index = parent[index] = parent[parent[index]];
        return index;
    }

    private static void Union(int[] parent, int first, int second)
    {
        var a = Find(parent, first);
        var b = Find(parent, second);
        if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
    }
}
