using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TS3PackageFusion;

public partial class MainWindow : Window
{
    private readonly List<PackageDocument> _packages = [];
    private readonly Dictionary<int, bool> _lastConflictChoices = [];
    private List<InitialConflict> _initialConflicts = [];

    // Dossier temporaire (propre à cette session) pour les packages extraits des Sims3Pack.
    // Il est supprimé à la fermeture : l'application ne crée rien dans Documents.
    private static readonly string ExtractionRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TS3PackageFusion", Guid.NewGuid().ToString("N"));

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropOverlay.IsVisible = false);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Closed += (_, _) => CleanupExtractedPackages();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var accepted = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.IsVisible = accepted;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        e.Handled = true;
        var paths = e.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
        await AddPathsAsync(paths);
    }

    /// <summary>Ajoute des fichiers .package, .sims3pack ou des dossiers (parcourus récursivement).</summary>
    public async Task AddPathsAsync(IEnumerable<string> paths)
    {
        var files = new List<string>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                try { files.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Where(IsSupportedFile).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)); }
                catch (Exception ex) { Status.Text = $"Lecture du dossier impossible : {ex.Message}"; }
            }
            else if (File.Exists(path) && IsSupportedFile(path)) files.Add(path);
        }
        if (files.Count == 0) { Status.Text = "Aucun fichier .package ou .sims3pack trouvé."; return; }

        var before = _packages.Count;
        foreach (var file in files)
        {
            if (file.EndsWith(".sims3pack", StringComparison.OrdinalIgnoreCase)) await ImportSims3Pack(file);
            else await AddPackage(file);
        }
        Refresh();
        await UpdateInitialConflictsAsync();
        Status.Text = $"{_packages.Count - before} package(s) ajouté(s). Total : {_packages.Count} package(s), {_packages.Sum(p => p.Entries.Count):N0} ressources";
    }

    private static bool IsSupportedFile(string path) =>
        path.EndsWith(".package", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".sims3pack", StringComparison.OrdinalIgnoreCase);

    private async Task<int> ImportSims3Pack(string path)
    {
        var imported = 0;
        try
        {
            Status.Text = $"Extraction de {System.IO.Path.GetFileName(path)}…";
            var packages = await Task.Run(() => Sims3PackExtractor.ExtractPackages(path, ExtractionRoot));
            foreach (var package in packages) { await AddPackage(package); imported++; }
        }
        catch (Exception ex) { Status.Text = $"Import Sims3Pack impossible : {ex.Message}"; }
        return imported;
    }

    private static void CleanupExtractedPackages()
    {
        try { if (Directory.Exists(ExtractionRoot)) Directory.Delete(ExtractionRoot, recursive: true); }
        catch { /* best effort : le système nettoiera le dossier temporaire */ }
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
            if (path is not null) imported += await ImportSims3Pack(path);
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

}
