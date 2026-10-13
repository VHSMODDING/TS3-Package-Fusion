using Avalonia.Controls;
using Avalonia.Layout;
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
        var panel = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 14 };
        panel.Children.Add(new TextBlock { Text = "Le package fusionné dépasserait 4 Go, la limite du format DBPF.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontWeight = Avalonia.Media.FontWeight.Bold });
        panel.Children.Add(new TextBlock { Text = "Voulez-vous créer automatiquement plusieurs packages numérotés dans le même dossier ? Les Sims 3 les chargera ensemble.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Annuler" }; cancel.Click += (_, _) => Close(false); buttons.Children.Add(cancel);
        var split = new Button { Content = "Diviser et fusionner" }; split.Click += (_, _) => Close(true); buttons.Children.Add(split);
        panel.Children.Add(buttons);
        Content = panel;
    }

    public static Task<bool> Ask(Window owner) => new SplitDialog().ShowDialog<bool>(owner);
}
