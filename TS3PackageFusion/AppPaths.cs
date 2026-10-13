using System;
using System.IO;

namespace TS3PackageFusion;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TS3PackageFusion");
    public static string Projects { get; } = Path.Combine(Root, "Projects");
    public static string ExtractedPackages { get; } = Path.Combine(Root, "Extracted Packages");
    public static string MergedPackages { get; } = Path.Combine(Root, "Merged Packages");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Projects);
        Directory.CreateDirectory(ExtractedPackages);
        Directory.CreateDirectory(MergedPackages);
    }
}
