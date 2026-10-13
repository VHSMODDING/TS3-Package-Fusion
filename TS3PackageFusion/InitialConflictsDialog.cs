using Avalonia.Controls;
using Avalonia.Layout;
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
        var panel = new Grid { Margin = new Avalonia.Thickness(20), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12 };
        panel.Children.Add(new TextBlock { Text = $"{conflicts.Count:N0} ressource(s) existent dans plusieurs packages. La fusion te demandera quoi conserver.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var list = new ListBox { ItemsSource = conflicts };
        Grid.SetRow(list, 1);
        panel.Children.Add(list);
        var close = new Button { Content = "Fermer", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 2);
        panel.Children.Add(close);
        Content = panel;
    }

    public static Task Show(Window owner, IReadOnlyList<InitialConflict> conflicts) =>
        new InitialConflictsDialog(conflicts).ShowDialog(owner);
}
