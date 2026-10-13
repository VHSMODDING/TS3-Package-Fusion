using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using System.Threading.Tasks;

namespace TS3PackageFusion;

public sealed class SplitDialog : Window
{
    private SplitDialog()
    {
        Title = "Fusion trop volumineuse";
        Width = 540;
        Height = 230;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        RequestedThemeVariant = ThemeVariant.Light;
        Background = Brush.Parse("#D8ECFF");
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 14, Background = Brush.Parse("#D8ECFF") };
        panel.Children.Add(new Border
        {
            Background = Brush.Parse("#FFF3C5"),
            BorderBrush = Brush.Parse("#D9A82D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Child = new TextBlock
            {
                Text = "Le package fusionné dépasserait 2 Go, la limite recommandée pour Les Sims 3.",
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeight.Bold,
                Foreground = Brush.Parse("#735500")
            }
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Voulez-vous créer automatiquement plusieurs packages numérotés dans le même dossier ? Les Sims 3 les chargera ensemble.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush.Parse("#173A76")
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button
        {
            Content = "Annuler",
            Padding = new Thickness(14, 8),
            Background = Brush.Parse("#DFE9F7"),
            BorderBrush = Brush.Parse("#9AB7DA"),
            Foreground = Brush.Parse("#173A76")
        };
        cancel.Click += (_, _) => Close(false);
        buttons.Children.Add(cancel);
        var split = new Button
        {
            Content = "Diviser et fusionner",
            Padding = new Thickness(14, 8),
            Background = Brush.Parse("#78D9A3"),
            BorderBrush = Brush.Parse("#D5FFE6"),
            Foreground = Brush.Parse("#113D2A")
        };
        split.Click += (_, _) => Close(true);
        buttons.Children.Add(split);
        panel.Children.Add(buttons);
        Content = panel;
    }

    public static Task<bool> Ask(Window owner) => new SplitDialog().ShowDialog<bool>(owner);
}
