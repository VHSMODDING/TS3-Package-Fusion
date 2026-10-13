using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TS3PackageFusion;

public sealed record InitialConflict(ResourceKey Key, IReadOnlyList<string> Packages)
{
    public override string ToString() => $"{Key} - {string.Join(" / ", Packages)}";
}

public sealed class InitialConflictsDialog : Window
{
    private InitialConflictsDialog(IReadOnlyList<InitialConflict> conflicts)
    {
        Title = "Ressources similaires détectées";
        Width = 850;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        RequestedThemeVariant = ThemeVariant.Light;
        Background = Brush.Parse("#D8ECFF");

        var panel = new Grid
        {
            Margin = new Thickness(20),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 12,
            Background = Brush.Parse("#D8ECFF")
        };
        panel.Children.Add(new Border
        {
            Background = Brush.Parse("#405F9F"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10),
            Child = new TextBlock
            {
                Text = $"{conflicts.Count:N0} ressource(s) existent dans plusieurs packages. La fusion demandera quoi conserver.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.White,
                FontWeight = FontWeight.SemiBold
            }
        });
        var list = new ListBox
        {
            ItemsSource = conflicts,
            Background = Brush.Parse("#B9DAFA"),
            BorderBrush = Brush.Parse("#527FC2"),
            BorderThickness = new Thickness(1),
            Foreground = Brush.Parse("#173A76")
        };
        Grid.SetRow(list, 1);
        panel.Children.Add(list);
        var close = new Button
        {
            Content = "Fermer",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(14, 8),
            Background = Brush.Parse("#A8D1FF"),
            BorderBrush = Brush.Parse("#E2F0FF"),
            Foreground = Brush.Parse("#173A76")
        };
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 2);
        panel.Children.Add(close);
        Content = panel;
    }

    public static Task Show(Window owner, IReadOnlyList<InitialConflict> conflicts) =>
        new InitialConflictsDialog(conflicts).ShowDialog(owner);
}
