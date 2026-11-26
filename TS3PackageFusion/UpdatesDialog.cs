using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TS3PackageFusion;

/// <summary>Liste les mises à jour détectées et laisse choisir les anciennes versions à retirer.</summary>
public sealed class UpdatesDialog : Window
{
    private readonly List<(PackageUpdate Update, CheckBox Box)> _rows = [];
    private List<PackageUpdate>? _result;

    private UpdatesDialog(IReadOnlyList<PackageUpdate> updates, Func<PackageUpdate, bool> preselect)
    {
        Title = "Mises à jour détectées";
        Width = 880;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        RequestedThemeVariant = ThemeVariant.Light;
        Background = Brush.Parse("#D8ECFF");

        var panel = new Grid
        {
            Margin = new Thickness(20),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 12
        };
        panel.Children.Add(new Border
        {
            Background = Brush.Parse("#405F9F"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10),
            Child = new TextBlock
            {
                Text = updates.Count == 1
                    ? "Un package de la liste ressemble à une version plus ancienne d’un autre. Décoche ce que tu veux garder."
                    : $"{updates.Count:N0} packages de la liste ressemblent à des versions plus anciennes d’autres packages. Décoche ce que tu veux garder.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.White,
                FontWeight = FontWeight.SemiBold
            }
        });

        var list = new StackPanel { Spacing = 8 };
        foreach (var update in updates)
        {
            var box = new CheckBox
            {
                IsChecked = preselect(update),
                Foreground = Brush.Parse("#173A76"),
                VerticalAlignment = VerticalAlignment.Top
            };
            var details = new StackPanel { Spacing = 2 };
            details.Children.Add(new TextBlock
            {
                Text = $"Retirer  {update.Older.Name}",
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush.Parse("#173A76"),
                TextWrapping = TextWrapping.Wrap
            });
            details.Children.Add(new TextBlock
            {
                Text = $"remplacé par  {update.Newer.Name}",
                Foreground = Brush.Parse("#2A5390"),
                TextWrapping = TextWrapping.Wrap
            });
            details.Children.Add(new TextBlock
            {
                Text = update.Confidence == UpdateConfidence.Confident
                    ? $"Même mod : {update.Reason}, {update.SharedText}."
                    : $"À vérifier : {update.Reason}, {update.SharedText}.",
                FontSize = 12,
                Foreground = Brush.Parse(update.Confidence == UpdateConfidence.Confident ? "#3A6AA8" : "#8A6200"),
                TextWrapping = TextWrapping.Wrap
            });
            box.Content = details;
            list.Children.Add(new Border
            {
                Background = Brush.Parse("#B9DAFA"),
                BorderBrush = Brush.Parse("#527FC2"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8),
                Child = box
            });
            _rows.Add((update, box));
        }

        var scroll = new ScrollViewer { Content = list };
        Grid.SetRow(scroll, 1);
        panel.Children.Add(scroll);

        var remove = Button("Retirer les versions cochées", "#78D9A3", "#113D2A");
        remove.Click += (_, _) =>
        {
            _result = _rows.Where(row => row.Box.IsChecked == true).Select(row => row.Update).ToList();
            Close();
        };
        var keep = Button("Tout garder", "#A8D1FF", "#173A76");
        keep.Click += (_, _) => { _result = []; Close(); };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { keep, remove }
        };
        Grid.SetRow(buttons, 2);
        panel.Children.Add(buttons);
        Content = panel;
    }

    private static Button Button(string text, string background, string foreground) => new()
    {
        Content = text,
        Padding = new Thickness(14, 8),
        FontWeight = FontWeight.SemiBold,
        Background = Brush.Parse(background),
        BorderBrush = Brush.Parse("#E2F0FF"),
        Foreground = Brush.Parse(foreground)
    };

    /// <summary>
    /// Renvoie les mises à jour dont l'ancienne version doit être retirée (vide si rien à faire).
    /// <paramref name="preselect"/> dit lesquelles sont cochées d'avance : les paires déjà tranchées ne le sont plus.
    /// </summary>
    public static async Task<IReadOnlyList<PackageUpdate>> Show(Window owner, IReadOnlyList<PackageUpdate> updates, Func<PackageUpdate, bool> preselect)
    {
        var dialog = new UpdatesDialog(updates, preselect);
        await dialog.ShowDialog(owner);
        return dialog._result ?? [];
    }
}
