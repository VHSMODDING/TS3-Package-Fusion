using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace TS3PackageFusion;

public partial class MainWindow : Window
{
    private readonly List<PackageDocument> _packages = [];
    private readonly Dictionary<int, bool> _lastConflictChoices = [];
    private List<InitialConflict> _initialConflicts = [];

    public MainWindow()
    {
        AppPaths.EnsureCreated();
        InitializeComponent();
        Status.Text = $"Espace de travail : {AppPaths.Root}";
    }

    private async void AddPackages(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Packages Sims 3") { Patterns = ["*.package"] }]
        });
        foreach (var file in files) await AddPackage(file.TryGetLocalPath());
        Refresh();
        await UpdateInitialConflictsAsync();
    }

    private async Task AddPackage(string? path)
    {
        if (path is null || _packages.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase))) return;
        try
        {
            Status.Text = $"Lecture de {System.IO.Path.GetFileName(path)}…";
            _packages.Add(await Task.Run(() => PackageDocument.Read(path)));
            _lastConflictChoices.Clear();
        }
        catch (Exception ex) { Status.Text = $"Impossible d’ouvrir {System.IO.Path.GetFileName(path)} : {ex.Message}"; }
    }

    private async void ImportSims3Packs(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Sims3Pack") { Patterns = ["*.sims3pack"] }]
        });
        var imported = 0;
        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (path is null) continue;
            try
            {
                Status.Text = $"Extraction de {System.IO.Path.GetFileName(path)}…";
                var packages = await Task.Run(() => Sims3PackExtractor.ExtractPackages(path, AppPaths.ExtractedPackages));
                foreach (var package in packages) { await AddPackage(package); imported++; }
            }
            catch (Exception ex) { Status.Text = $"Import Sims3Pack impossible : {ex.Message}"; }
        }
        Refresh();
        await UpdateInitialConflictsAsync();
        if (imported > 0) Status.Text = $"{imported} package(s) extrait(s) et ajouté(s).";
    }

    private async void RemoveSelected(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (PackageList.SelectedItem is PackageDocument package)
        {
            _packages.Remove(package);
            _lastConflictChoices.Clear();
        }
        Refresh();
        await UpdateInitialConflictsAsync();
    }

    private async void SaveProject(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "MyMerge.ts3pf.json",
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(AppPaths.Projects),
            FileTypeChoices = [new FilePickerFileType("Projet TS3 Package Fusion") { Patterns = ["*.ts3pf.json"] }]
        });
        var path = file?.TryGetLocalPath();
        if (path is null) return;
        var project = new MergeProject
        {
            Packages = _packages.Select(p => new ProjectPackage { Path = p.Path, Sha256 = HashFile(p.Path) }).ToList(),
            ConflictChoices = new Dictionary<int, bool>(_lastConflictChoices)
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true }));
        Status.Text = "Projet enregistré.";
    }

    private async void OpenProject(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(AppPaths.Projects),
            FileTypeFilter = [new FilePickerFileType("Projet TS3 Package Fusion") { Patterns = ["*.ts3pf.json"] }]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;
        try
        {
            var project = JsonSerializer.Deserialize<MergeProject>(await File.ReadAllTextAsync(path)) ?? throw new InvalidDataException("Projet vide.");
            if (project.Version != 1) throw new InvalidDataException("Cette version de projet n’est pas prise en charge.");
            _packages.Clear(); _lastConflictChoices.Clear();
            foreach (var pair in project.ConflictChoices) _lastConflictChoices[pair.Key] = pair.Value;
            var changed = 0;
            foreach (var projectPackage in project.Packages)
            {
                if (!File.Exists(projectPackage.Path)) { changed++; continue; }
                var document = await Task.Run(() => PackageDocument.Read(projectPackage.Path));
                if (!string.Equals(HashFile(document.Path), projectPackage.Sha256, StringComparison.OrdinalIgnoreCase)) changed++;
                _packages.Add(document);
            }
            if (changed != 0) _lastConflictChoices.Clear();
            Refresh();
            await UpdateInitialConflictsAsync();
            Status.Text = changed == 0 ? "Projet ouvert." : $"Projet ouvert : {changed} fichier(s) manquant(s) ou modifié(s).";
        }
        catch (Exception ex) { Status.Text = $"Impossible d’ouvrir le projet : {ex.Message}"; }
    }

    private async void MergePackages(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_packages.Count == 0) { Status.Text = "Ajoutez au moins un package."; return; }
        var choices = new Dictionary<int, ConflictAction>();
        var winners = new Dictionary<ResourceKey, (int Source, PackageEntry Entry)>();
        var id = 0;
        var ignoreAllFollowing = false;
        for (var source = 0; source < _packages.Count; source++)
        foreach (var entry in _packages[source].Entries)
        {
            var key = entry.Key;
            if (!winners.TryGetValue(key, out var previous)) { winners[key] = (source, entry); continue; }
            if (Dbpf.ResourcesEquivalent(_packages[previous.Source], previous.Entry, _packages[source], entry)) continue;
            ConflictDecision? decision = _lastConflictChoices.TryGetValue(id, out var saved)
                ? new ConflictDecision(saved ? ConflictAction.KeepNew : ConflictAction.KeepExisting, false)
                : ignoreAllFollowing
                    ? new ConflictDecision(ConflictAction.KeepExisting, false)
                    : await ConflictDialog.Show(this, _packages[previous.Source].Name, _packages[source].Name, key, id + 1);
            if (decision is null) { Status.Text = "Fusion annulée."; return; }
            choices[id++] = decision.Action;
            if (decision.Action == ConflictAction.KeepNew) winners[key] = (source, entry);
            if (decision.IgnoreAllFollowing) ignoreAllFollowing = true;
        }
        var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "Merged.package",
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(AppPaths.MergedPackages),
            FileTypeChoices = [new FilePickerFileType("Package Sims 3") { Patterns = ["*.package"] }]
        });
        var output = target?.TryGetLocalPath();
        if (output is null) return;
        try
        {
            var split = await Task.Run(() => Dbpf.RequiresSplit(_packages, choices));
            if (split && !await SplitDialog.Ask(this)) { Status.Text = "Fusion annulée."; return; }
            MergeProgressArea.IsVisible = true;
            MergeProgressBar.Value = 0;
            MergeProgressPercent.Text = "0 %";
            MergeProgressText.Text = "Fusion en préparation...";
            var progress = new Progress<MergeProgress>(UpdateMergeProgress);
            Status.Text = "Fusion et vérification en cours...";
            var result = await Task.Run(() => split
                ? Dbpf.MergeSplitAtomically(_packages, output, choices, progress)
                : Dbpf.MergeAtomically(_packages, output, choices, progress));
            _lastConflictChoices.Clear();
            foreach (var pair in choices) _lastConflictChoices[pair.Key] = pair.Value == ConflictAction.KeepNew;
            Status.Text = $"Terminé : {result.Resources:N0} ressources, {result.Conflicts:N0} conflit(s), {result.Files} fichier(s).";
            MergeProgressBar.Value = 100;
            MergeProgressPercent.Text = "100 %";
            MergeProgressText.Text = "Fusion terminée.";
        }
        catch (Exception ex)
        {
            Status.Text = $"Fusion impossible : {ex.Message}";
            MergeProgressArea.IsVisible = false;
        }
    }

    private void Refresh()
    {
        PackageList.ItemsSource = null;
        PackageList.ItemsSource = _packages;
        Status.Text = $"{_packages.Count} package(s), {_packages.Sum(p => p.Entries.Count):N0} ressources";
    }

    private async Task UpdateInitialConflictsAsync()
    {
        var snapshot = _packages.ToArray();
        _initialConflicts = await Task.Run(() => FindInitialConflicts(snapshot));
        ConflictNotice.IsVisible = _initialConflicts.Count > 0;
        ConflictNoticeText.Text = $"⚠ {_initialConflicts.Count:N0} conflit(s) potentiel(s) détecté(s). Cliquer pour afficher les packages concernés.";
    }

    private static List<InitialConflict> FindInitialConflicts(IReadOnlyList<PackageDocument> packages)
    {
        var resources = new Dictionary<ResourceKey, List<(PackageDocument Document, PackageEntry Entry)>>();
        foreach (var package in packages)
        foreach (var entry in package.Entries)
        {
            if (!resources.TryGetValue(entry.Key, out var occurrences)) resources[entry.Key] = occurrences = [];
            occurrences.Add((package, entry));
        }
        return resources.Where(pair => pair.Value.Select(item => item.Document.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Where(pair => pair.Value.Skip(1).Any(item => !Dbpf.ResourcesEquivalent(pair.Value[0].Document, pair.Value[0].Entry, item.Document, item.Entry)))
            .Select(pair => new InitialConflict(pair.Key, pair.Value.Select(item => item.Document.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray()))
            .OrderBy(conflict => conflict.Key.Type).ThenBy(conflict => conflict.Key.Group).ThenBy(conflict => conflict.Key.High).ThenBy(conflict => conflict.Key.Low)
            .ToList();
    }

    private async void ShowInitialConflicts(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_initialConflicts.Count > 0) await InitialConflictsDialog.Show(this, _initialConflicts);
    }

    private void UpdateMergeProgress(MergeProgress progress)
    {
        var percent = progress.TotalBytes == 0 ? 100 : (int)Math.Min(100, progress.CopiedBytes * 100 / progress.TotalBytes);
        MergeProgressBar.Value = percent;
        MergeProgressPercent.Text = $"{percent} %";
        MergeProgressText.Text = progress.TotalFiles > 1
            ? $"Création du fichier {progress.CurrentFile} sur {progress.TotalFiles} : {FormatBytes(progress.CopiedBytes)} sur {FormatBytes(progress.TotalBytes)}"
            : $"Fusion : {FormatBytes(progress.CopiedBytes)} sur {FormatBytes(progress.TotalBytes)}";
    }

    private static string FormatBytes(ulong value)
    {
        string[] units = ["octets", "Ko", "Mo", "Go"];
        var amount = (double)value;
        var unit = 0;
        while (amount >= 1024 && unit < units.Length - 1) { amount /= 1024; unit++; }
        return unit == 0 ? $"{amount:N0} {units[unit]}" : $"{amount:N1} {units[unit]}";
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

public sealed class MergeProject
{
    public int Version { get; set; } = 1;
    public List<ProjectPackage> Packages { get; set; } = [];
    public Dictionary<int, bool> ConflictChoices { get; set; } = [];
}

public sealed class ProjectPackage { public string Path { get; set; } = ""; public string Sha256 { get; set; } = ""; }
