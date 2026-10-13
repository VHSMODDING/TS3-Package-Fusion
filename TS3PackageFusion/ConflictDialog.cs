using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace TS3PackageFusion;

public sealed record ConflictDecision(ConflictAction Action, bool IgnoreAllFollowing);

public sealed class ConflictDialog : Window
{
    private readonly CheckBox _ignoreAll = new()
    {
        Content = "Ne rien faire pour tous les conflits suivants",
        IsChecked = true
    };

    private readonly Border _helpPanel = new()
    {
        IsVisible = false,
        Background = Brush.Parse("#EAF4FF"),
        BorderBrush = Brush.Parse("#7AA9DA"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(12)
    };

    private ConflictDialog(string first, string second, ResourceKey key, int number)
    {
        Title = "Conflit de package";
        Width = 720;
        Height = 385;
        MinWidth = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(22), Spacing = 12 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock
        {
            Text = $"Conflit {number}",
            FontWeight = FontWeight.Bold,
            FontSize = 19,
            Foreground = Brush.Parse("#173A76")
        });
        var help = new Button
        {
            Content = "?",
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            FontWeight = FontWeight.Bold
        };
        ToolTip.SetTip(help, "Aide : quel choix utiliser ?");
        help.Click += (_, _) => _helpPanel.IsVisible = !_helpPanel.IsVisible;
        Grid.SetColumn(help, 1);
        header.Children.Add(help);
        panel.Children.Add(header);

        panel.Children.Add(new TextBlock
        {
            Text = "Deux packages contiennent une version différente de la même ressource.",
            TextWrapping = TextWrapping.Wrap
        });

        var files = new Border
        {
            Background = Brush.Parse("#F2F8FF"),
            BorderBrush = Brush.Parse("#B7D3EE"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(11),
            Child = new TextBlock
            {
                Text = $"A - {first}\nB - {second}\n\nRessource : {key}",
                TextWrapping = TextWrapping.Wrap
            }
        };
        panel.Children.Add(files);

        _helpPanel.Child = new TextBlock
        {
            Text = "Vous ne savez pas quoi choisir ? Cliquez simplement sur « Continuer ». " +
                   "Cela garde la ressource A dans le nouveau package et ignore le doublon B. " +
                   "Vos fichiers d’origine ne sont jamais modifiés.\n\n" +
                   "Garder A ou Garder B est utile seulement si vous savez quelle version doit remplacer l’autre.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush.Parse("#173A76")
        };
        panel.Children.Add(_helpPanel);
        panel.Children.Add(_ignoreAll);

        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        AddButton(buttons, "Continuer (ne rien faire)", ConflictAction.KeepExisting, true);
        AddButton(buttons, "Garder A", ConflictAction.KeepExisting, false);
        AddButton(buttons, "Garder B", ConflictAction.KeepNew, false);
        AddCancelButton(buttons);
        panel.Children.Add(buttons);
        Content = panel;
    }

    private void AddButton(Panel buttons, string label, ConflictAction action, bool recommended)
    {
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(0, 0, 8, 4),
            Padding = new Thickness(12, 8),
            Background = recommended ? Brush.Parse("#78D9A3") : null,
            FontWeight = recommended ? FontWeight.SemiBold : FontWeight.Normal
        };
        button.Click += (_, _) => Close(new ConflictDecision(action, _ignoreAll.IsChecked == true));
        buttons.Children.Add(button);
    }

    private void AddCancelButton(Panel buttons)
    {
        var button = new Button { Content = "Annuler la fusion", Margin = new Thickness(0, 0, 8, 4), Padding = new Thickness(12, 8) };
        button.Click += (_, _) => Close(null);
        buttons.Children.Add(button);
    }

    public static Task<ConflictDecision?> Show(Window owner, string first, string second, ResourceKey key, int number) =>
        new ConflictDialog(first, second, key, number).ShowDialog<ConflictDecision?>(owner);
}
