using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

internal static class ArtworkConcepts
{
    public static void Render()
    {
        foreach (var (file, name, accent) in new[] { ("Adoulin", "Western Adoulin", "#D9B780"), ("Lufaise", "Lufaise Meadows", "#BED88E"), ("EasternAdoulin", "Eastern Adoulin", "#97C9D6"), ("Amano5", "Treasures of Aht Urhgan", "#D5C59D"), ("Maw", "Cavernous Maw", "#BBAFD0"), ("Ulbuka", "Character landscapes", "#98CAB5"), ("Arita0", "Crystal illustration", "#9FD9D4"), ("Illustration0", "Gallery illustration", "#D8C79C"), ("Illustration6", "Abyssea", "#A8BFDB") })
        {
            using var artwork = new Bitmap("artifacts/theme-concepts/source/" + file + ".jpg");
            var window = new Window { Width = 1120, Height = 1000, Background = Brush.Parse("#101820"), Foreground = Brush.Parse("#E7EDF2") };
            var portrait = file is "Amano5" or "Maw" or "Illustration0" or "Illustration6";
            var landscapes = file == "Ulbuka" || portrait;
            var root = new Grid { RowDefinitions = new RowDefinitions("220,Auto,*,Auto") };
            var hero = new Grid { ClipToBounds = true };
            if (!portrait) hero.Children.Add(new Image { Source = artwork, Stretch = Stretch.UniformToFill });
            hero.Children.Add(new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = new GradientStops { new GradientStop(Color.Parse("#E6101820"), 0), new GradientStop(Color.Parse("#99101820"), .45), new GradientStop(Color.Parse("#20101820"), 1) } } });
            var title = new StackPanel { Margin = new Thickness(32, 42), Spacing = 10, VerticalAlignment = VerticalAlignment.Top };
            title.Children.Add(Text("FFXI / ACCOUNT MANAGER", 12, accent));
            title.Children.Add(Text("Your characters. One place.", 30));
            title.Children.Add(Text("Original Vana’diel concept art", 14, "#C3CFD8"));
            hero.Children.Add(title);
            var themeControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 32, 32, 0) };
            themeControls.Children.Add(new ComboBox { Width = 230, ItemsSource = new[] { "FFXI · " + name }, SelectedIndex = 0 });
            themeControls.Children.Add(new Button { Content = "+ Add account" });
            hero.Children.Add(themeControls);
            var caption = Text(name + " · Concept artwork", 13, "#FFFFFF");
            caption.Margin = new Thickness(32, 0, 0, 20); caption.VerticalAlignment = VerticalAlignment.Bottom;
            hero.Children.Add(caption);
            var credit = Text(file == "Amano5" ? "©2006 YOSHITAKA AMANO · © SQUARE ENIX" : "© SQUARE ENIX CO., LTD. All Rights Reserved.", 10, "#FFFFFF");
            credit.Margin = new Thickness(0, 0, 32, 20); credit.HorizontalAlignment = HorizontalAlignment.Right; credit.VerticalAlignment = VerticalAlignment.Bottom;
            hero.Children.Add(credit);
            root.Children.Add(hero);
            var controls = new Grid { Background = Brush.Parse("#E6101820"), ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(32, 18, 32, 12) };
            controls.Children.Add(Text("Accounts · 3 configured", 19));
            var bulk = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            bulk.Children.Add(new Button { Content = "Bulk actions ▾" });
            bulk.Children.Add(new Button { Content = "Auto login enabled accounts", Background = Brush.Parse(accent), Foreground = Brush.Parse("#17232C") });
            Grid.SetColumn(bulk, 1); controls.Children.Add(bulk); Grid.SetRow(controls, 1); root.Children.Add(controls);
            var cards = new StackPanel { Margin = new Thickness(portrait ? 260 : 32, 0, 32, 0), Spacing = 12 };
            var cardIndex = 0;
            var cardArt = new List<Bitmap>();
            foreach (var (character, zone, weather) in new[] { ("Nanakia", "West Ronfaure", "Clear"), ("Minibubble", "Dho Gates", "Dust storms"), ("Luckystrikes", "Western Adoulin", "Fair skies") })
            {
                var body = new StackPanel { Spacing = 8 };
                var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                var identity = new StackPanel { Spacing = 3 };
                identity.Children.Add(Text("↕  " + character, 20));
                identity.Children.Add(Text("Asura · Low Res · SE ID configured", 12, "#A7B8C6"));
                heading.Children.Add(identity);
                var policies = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
                foreach (var label in new[] { "Auto login", "Maintain connection", "Maintenance" }) policies.Children.Add(new ToggleSwitch { Content = label, IsChecked = true, FontSize = portrait ? 10 : 14 });
                Grid.SetColumn(policies, 1); heading.Children.Add(policies); body.Children.Add(heading);
                body.Children.Add(Text("● Online · " + zone + " · HP 2100/2100 · MP 1800/1800", 13, accent));
                body.Children.Add(Text("Weather: " + weather + "  ·  Vana’diel: 19:44 Firesday  ·  Moon: Waxing Crescent (25%)", 13, "#CEDAE3"));
                body.Children.Add(new Border { Background = Brush.Parse("#111C27"), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6), Child = Text("Maintenance · Tue, Oct 13, 1:00–2:30 AM EDT · All worlds", 12, "#B5C5D3") });
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
                foreach (var label in new[] { "Launch profile", "Edit account", "Graceful shutdown", "FFXIAH", "Remove" }) actions.Children.Add(new Button { Content = label, Padding = new Thickness(10, 5), FontSize = 12 });
                body.Children.Add(actions);
                var card = new Border { Background = Brush.Parse("#1B2937"), CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12), Child = body };
                if (landscapes)
                {
                    var art = new Bitmap("artifacts/theme-concepts/source/" + new[] { "Ulbuka", "Adoulin", "Promyvion" }[cardIndex++] + ".jpg");
                    cardArt.Add(art);
                    var surface = new Grid { ClipToBounds = true };
                    surface.Children.Add(new Image { Source = art, Stretch = Stretch.UniformToFill });
                    surface.Children.Add(new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0,0,RelativeUnit.Relative), EndPoint = new RelativePoint(1,0,RelativeUnit.Relative), GradientStops = new GradientStops { new GradientStop(Color.Parse("#F0101820"),0), new GradientStop(Color.Parse("#D0101820"), .6), new GradientStop(Color.Parse("#60101820"),1) } } });
                    card.Background = Brushes.Transparent;
                    surface.Children.Add(card);
                    cards.Children.Add(new Border { Height = 210, CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = surface });
                }
                else cards.Children.Add(card);
            }
            Grid.SetRow(cards, 2); root.Children.Add(cards);
            var footer = Text("CONCEPT PREVIEW · sample session data · original artwork · saved theme and card assignments", 12, "#9AACBC");
            footer.Margin = new Thickness(32, 12, 32, 18); Grid.SetRow(footer, 3); root.Children.Add(footer);
            if (portrait)
            {
                var layered = new Grid();
                layered.Children.Add(new Image { Source = artwork, Stretch = Stretch.UniformToFill, Opacity = .22 });
                layered.Children.Add(new Border { Width = 260, Height = 1000, HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true, Child = new Image { Source = artwork, Stretch = Stretch.UniformToFill } });
                layered.Children.Add(new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0,0,RelativeUnit.Relative), EndPoint = new RelativePoint(1,0,RelativeUnit.Relative), GradientStops = new GradientStops { new GradientStop(Color.Parse("#40101820"),0), new GradientStop(Color.Parse("#40101820"),.22), new GradientStop(Color.Parse("#E6101820"),.7) } } });
                layered.Children.Add(root);
                window.Content = layered;
            }
            else window.Content = root;
            window.Show(); Dispatcher.UIThread.RunJobs();
            using (var frame = window.CaptureRenderedFrame()!) frame.Save("artifacts/theme-concepts/" + file + "-integration.png", PngBitmapEncoderOptions.Default);
            window.Close();
            foreach (var art in cardArt) art.Dispose();
        }
    }
    private static TextBlock Text(string value, double size, string color = "#E7EDF2") => new() { Text = value, FontSize = size, Foreground = Brush.Parse(color), TextWrapping = TextWrapping.Wrap };
}
