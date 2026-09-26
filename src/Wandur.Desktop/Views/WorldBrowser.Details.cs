using L = Wandur.Core.Localization.Strings;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Wandur.Core.Discovery;

namespace Wandur.Desktop.Views;

/// <summary>A world's own page, as the site's /worlds/{id}: hero, chips bar, About beside World details, a glimpse inside.</summary>
public sealed partial class WorldBrowserView
{
    private Border? _glimpse;

    private void BuildWorldPage(WorldListing world)
    {
        _artStatus.Text = world.HasSuppliedArtwork && !world.HasGeneratedArtwork ? L.LoadingSuppliedArtwork : L.LoadingIllustration;
        _artPlaceholder.Text = Services.WorldThumbnails.Initials(world.Name);
        _artPlaceholder.IsVisible = true;
        _glimpse = null;

        // Breadcrumbs, as the site's band above the hero.
        var crumbs = new FlowPanel { Name = "DirectoryCrumbs", Gap = 8, Margin = new Thickness(0, 0, 0, 12) };
        var all = DirectoryLook.Link(L.BackToWorlds, BackToResults, 14, "MutedBrush", FontWeight.Normal);
        crumbs.Children.Add(all);
        if (world.Features.Theme.Length > 0) { crumbs.Children.Add(DirectoryLook.Label("/", 14, "MutedBrush")); crumbs.Children.Add(DirectoryLook.Label(world.Features.Theme, 14, "MutedBrush")); }
        crumbs.Children.Add(DirectoryLook.Label("/", 14, "MutedBrush"));
        var here = DirectoryLook.Label(world.Name, 14); here.TextWrapping = TextWrapping.NoWrap; here.TextTrimming = TextTrimming.CharacterEllipsis;
        crumbs.Children.Add(here);
        _details.Children.Add(crumbs);

        // The hero: the illustration under a gradient into the page, the name and tagline over it.
        var title = DirectoryLook.Label(world.Name, 46, weight: FontWeight.Bold);
        title.Name = "DirectoryWorldTitle"; title.LineHeight = 50; title.MaxLines = 2; title.TextTrimming = TextTrimming.CharacterEllipsis;
        var heroCopy = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(30, 0, 30, 24), Children = { title } };
        if (!string.IsNullOrWhiteSpace(world.Summary))
        {
            var tagline = DirectoryLook.Label(world.Summary.ReplaceLineEndings(" ").Trim(), 20, "MutedBrush");
            tagline.Name = "DirectoryWorldTagline"; tagline.MaxLines = 2; tagline.TextTrimming = TextTrimming.WordEllipsis; tagline.MaxWidth = 720;
            tagline.HorizontalAlignment = HorizontalAlignment.Left;
            heroCopy.Children.Add(tagline);
        }
        var hero = new Grid { Name = "DirectoryHero", Children = { _artFrame, heroCopy } };
        _details.Children.Add(hero);
        _artStatus.Margin = new Thickness(2, 8, 0, 0);
        _details.Children.Add(_artStatus);
        _narrowLayouts.Add(narrow =>
        {
            heroCopy.Margin = narrow ? new Thickness(18, 0, 18, 16) : new Thickness(30, 0, 30, 24);
            title.FontSize = narrow ? 34 : 46; title.LineHeight = narrow ? 38 : 50;
            if (heroCopy.Children.Count > 1 && heroCopy.Children[1] is TextBlock tagline) tagline.FontSize = narrow ? 17 : 20;
        });

        // The chips bar: theme, tags, beginner; the live count and the way in on the right.
        var chips = new FlowPanel { Name = "DirectoryWorldTags", Gap = 8, LineGap = 8, VerticalAlignment = VerticalAlignment.Center };
        var chipPadding = new Thickness(14, 4.5);
        foreach (var chip in new[] { world.Features.Theme }.Concat(world.Tags).Select(t => t.Trim()).Where(t => t.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase).Take(5))
            chips.Children.Add(DirectoryLook.Pill(chip, 14, chipPadding));
        if (world.BeginnerFriendly == true) chips.Children.Add(DirectoryLook.Beginner(14, chipPadding));
        if (world.IsOnline)
            _listingActions.Children.Add(DirectoryLook.Live(world.LivePlayerCount is { } players
                ? L.Format(L.PlayersOnlineCount, players) : L.StatusOnline, 15));
        var connect = new Button { Name = "ConnectDirectoryWorld", [!ContentControl.ContentProperty] = LocalizedText.Binding(nameof(L.Connect2)),
            Command = _model.ConnectCommand, IsEnabled = world.CanConnect, FontSize = 15, Padding = new Thickness(22, 10), CornerRadius = new CornerRadius(8) };
        connect.Classes.Add("app-button"); connect.Classes.Add("primary");
        var add = new Button { Name = "AddDirectoryWorld", [!ContentControl.ContentProperty] = LocalizedText.Binding(nameof(L.AddToMyWorlds)),
            Command = _model.SaveCommand, IsEnabled = world.CanConnect, FontSize = 14, Padding = new Thickness(14, 9), CornerRadius = new CornerRadius(8) };
        add.Classes.Add("app-button");
        var tls = new CheckBox { [!ContentControl.ContentProperty] = LocalizedText.Binding(nameof(L.UseTLS)), IsVisible = world.TlsPort.HasValue && world.CanConnect,
            IsChecked = _model.UseTls, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        tls.IsCheckedChanged += (_, _) => _model.UseTls = tls.IsChecked == true;
        foreach (var action in new Control[] { connect, add, tls }) { action.VerticalAlignment = VerticalAlignment.Center; _listingActions.Children.Add(action); }
        var chipsBar = new Grid { ColumnSpacing = 20, RowSpacing = 14, Children = { chips, _listingToolbar } };
        var barEdge = new Border { Name = "DirectoryChipsBar", BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 16), Margin = new Thickness(0, 8, 0, 22), Child = chipsBar };
        barEdge.Paint(Border.BorderBrushProperty, "LineBrush");
        _details.Children.Add(barEdge);
        _narrowLayouts.Add(narrow =>
        {
            chipsBar.ColumnDefinitions = narrow ? new ColumnDefinitions("*") : new ColumnDefinitions("*,Auto");
            chipsBar.RowDefinitions = narrow ? new RowDefinitions("Auto,Auto") : new RowDefinitions("Auto");
            Grid.SetColumn(_listingToolbar, narrow ? 0 : 1); Grid.SetRow(_listingToolbar, narrow ? 1 : 0);
        });

        // About beside World details.
        var about = Card(Heading(L.Format(L.AboutWorld, world.Name)));
        var aboutBody = (StackPanel)about.Child!;
        var description = FormattedDescription(world.Description);
        description.Name = "DirectoryWorldDescription";
        aboutBody.Children.Add(description);
        var place = new[] { (L.Roleplaying, world.Features.Roleplaying), (L.PlayerKilling, world.Features.PlayerKilling), (L.WorldSize, world.Features.WorldSize) }
            .Where(p => !string.IsNullOrWhiteSpace(p.Item2)).ToArray();
        if (place.Length > 0)
        {
            var placeHeading = Heading(L.FindYourPlace);
            var placeEdge = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 18, 0, 0), Margin = new Thickness(0, 12, 0, 0), Child = placeHeading };
            placeEdge.Paint(Border.BorderBrushProperty, "LineBrush");
            aboutBody.Children.Add(placeEdge);
            var placeGrid = new Grid { Name = "DirectoryFindYourPlace", ColumnSpacing = 16, RowSpacing = 12 };
            var cells = place.Select((p, i) =>
            {
                var name = DirectoryLook.Label(p.Item1, 15, weight: FontWeight.SemiBold);
                var value = DirectoryLook.Label(p.Item2, 14, "MutedBrush");
                var cell = new Border { BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 0), Child = new StackPanel { Spacing = 4, Children = { name, value } } };
                cell.Paint(Border.BorderBrushProperty, "LineBrush");
                placeGrid.Children.Add(cell);
                return cell;
            }).ToArray();
            aboutBody.Children.Add(placeGrid);
            _narrowLayouts.Add(narrow =>
            {
                placeGrid.ColumnDefinitions.Clear(); placeGrid.RowDefinitions.Clear();
                for (var i = 0; i < cells.Length; i++)
                {
                    if (narrow) placeGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    else placeGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                    Grid.SetColumn(cells[i], narrow ? 0 : i); Grid.SetRow(cells[i], narrow ? i : 0);
                    cells[i].BorderThickness = new Thickness(narrow || i == 0 ? 0 : 1, 0, 0, 0);
                    cells[i].Padding = new Thickness(narrow || i == 0 ? 0 : 16, 0, 0, 0);
                }
            });
        }

        var details = Card(Heading(L.WorldDetails));
        details.Name = "DirectoryWorldDetails";
        var detailsBody = (StackPanel)details.Child!;
        detailsBody.Children.Add(Facts(world));
        detailsBody.Children.Add(Subheading(L.ConnectWithAnyClient));
        detailsBody.Children.Add(AddressBox(world));
        var links = new FlowPanel { Gap = 16, LineGap = 6 };
        AddLink(links, L.Website, world.WebsiteUrl); AddLink(links, L.Discord, world.DiscordUrl);
        AddLink(links, L.PlayInBrowser, world.PlayUrl);
        AddLink(links, L.Format(L.SourceListing, world.Source.Name), world.Source.ListingUrl);
        if (links.Children.Count > 0) detailsBody.Children.Add(links);
        detailsBody.Children.Add(QuietFacts(world));
        var attribution = world.Source.UpdatedAt is { } updated
            ? L.Format(L.SourceDetailsUpdated, world.Source.Name, updated.ToLocalTime()) : L.Format(L.SourceDetails, world.Source.Name);
        detailsBody.Children.Add(DirectoryLook.Label(attribution, 12, "MutedBrush"));

        var split = new Grid { Name = "DirectoryWorldSplit", ColumnSpacing = 18, RowSpacing = 18, Children = { about, details } };
        _details.Children.Add(split);
        _narrowLayouts.Add(narrow =>
        {
            split.ColumnDefinitions = narrow ? new ColumnDefinitions("*") : new ColumnDefinitions("*,380");
            split.RowDefinitions = narrow ? new RowDefinitions("Auto,Auto") : new RowDefinitions("Auto");
            Grid.SetColumn(details, narrow ? 0 : 1); Grid.SetRow(details, narrow ? 1 : 0);
        });
    }

    /// <summary>"A glimpse inside": the banner the listing supplied, when the hero is the generated illustration.
    /// The client has no player chart, so the banner stands alone, as the site's single-column glimpse.</summary>
    private void ShowGlimpse(WorldListing world)
    {
        if (_glimpse is not null || _banner.Source is null) return;
        _banner.MaxHeight = 420; _banner.HorizontalAlignment = HorizontalAlignment.Left;
        var frame = new Border { ClipToBounds = true, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = _banner, HorizontalAlignment = HorizontalAlignment.Left };
        frame.Paint(Border.BorderBrushProperty, "LineBrush");
        var caption = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16, Margin = new Thickness(0, 8, 0, 0),
            Children = { DirectoryLook.Label(L.AroundTheWorld, 13, "MutedBrush") } };
        var credit = DirectoryLook.Label(L.Format(L.SuppliedArtwork, world.Source.Name), 13, "MutedBrush");
        Grid.SetColumn(credit, 1); caption.Children.Add(credit);
        _glimpse = Card(Heading(L.AGlimpseInside));
        _glimpse.Name = "DirectoryGlimpse"; _glimpse.Margin = new Thickness(0, 18, 0, 0);
        ((StackPanel)_glimpse.Child!).Children.Add(new StackPanel { Children = { frame, caption } });
        _details.Children.Add(_glimpse);
    }

    private static Border Card(Control heading)
    {
        var card = new Border
        {
            Classes = { "directory-card" }, Padding = new Thickness(24, 22), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            VerticalAlignment = VerticalAlignment.Top, Child = new StackPanel { Spacing = 12, Children = { heading } }
        };
        return card.Paint(Border.BackgroundProperty, "PanelBrush").Paint(Border.BorderBrushProperty, "LineBrush");
    }

    private static TextBlock Heading(string text) => DirectoryLook.Label(text, 22, weight: FontWeight.SemiBold);
    private static TextBlock Subheading(string text)
    {
        var heading = DirectoryLook.Label(text, 15, weight: FontWeight.SemiBold);
        heading.Margin = new Thickness(0, 8, 0, -4);
        return heading;
    }

    /// <summary>The card's facts: Status, Language, Play style, Established, Codebase, Player killing, and live players.</summary>
    private static Grid Facts(WorldListing world)
    {
        var grid = new Grid { Name = "DirectoryWorldFacts", ColumnDefinitions = new ColumnDefinitions("120,*"), ColumnSpacing = 12, RowSpacing = 10 };
        void Fact(string label, Control? value)
        {
            if (value is null) return;
            var row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = DirectoryLook.Label(label, 15, "MutedBrush");
            Grid.SetRow(name, row); Grid.SetRow(value, row); Grid.SetColumn(value, 1);
            grid.Children.Add(name); grid.Children.Add(value);
        }
        Control? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : DirectoryLook.Label(value, 15);
        var status = world.Availability.Archived == true ? L.ArchivedListing
            : world.Availability.Online switch { true => L.StatusOnline, false => L.StatusOffline, _ => L.AvailabilityUnknown };
        Fact(L.WorldStatus, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { DirectoryLook.Dot(world.IsOnline, 7), DirectoryLook.Label(status, 15) } });
        Fact(L.Language, Text(world.Features.Language));
        Fact(L.PlayStyle, Text(world.Features.Roleplaying.Length > 0 ? L.Format(L.RoleplayStyle, world.Features.Roleplaying.ToLower(CultureInfo.CurrentCulture)) : null));
        Fact(L.Established, Text(world.EstablishedAt?.Year.ToString(CultureInfo.InvariantCulture)));
        Fact(L.Codebase, Text(world.Features.Codebase));
        Fact(L.PlayerKilling, Text(world.Features.PlayerKilling));
        if (world.LivePlayerCount is { } players)
            Fact(L.LastObservedPlayers, Text(world.Population.ObservedAt is { } seen
                ? L.Format(L.PlayersObservedAgo, players, seen.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)) : players.ToString(CultureInfo.CurrentCulture)));
        return grid;
    }

    /// <summary>What the site folds under the card as quieter facts: the rest of what the directory knows.</summary>
    private static Control QuietFacts(WorldListing world)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), ColumnSpacing = 12, RowSpacing = 6 };
        void Fact(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var row = grid.RowDefinitions.Count; grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = DirectoryLook.Label(label, 13, "MutedBrush"); var content = DirectoryLook.Label(value, 13);
            Grid.SetRow(name, row); Grid.SetRow(content, row); Grid.SetColumn(content, 1); grid.Children.Add(name); grid.Children.Add(content);
        }
        var source = world.Source.Name.Length > 0 ? world.Source.Name : L.Directory;
        Fact(L.GameType, world.Features.Kind);
        Fact(L.Location, world.Features.Location);
        Fact(L.Development, world.Features.DevelopmentStatus);
        Fact(L.TLSConnection, world.TlsPort is { } port ? $"{world.Host}:{port}" : null);
        Fact(L.DirectoryRating, world.Community.RatingCount is not null || world.Community.Rating is not null ? world.RatingSummary : null);
        Fact(L.Format(L.SourceRank, source), world.Community.Rank is { } rank ? "#" + rank : null);
        Fact(L.Format(L.SourceReviews, source), world.Community.ReviewCount?.ToString(CultureInfo.CurrentCulture));
        Fact(L.MonthlyVotes, world.Community.MonthlyVotes?.ToString(CultureInfo.CurrentCulture));
        Fact(L.ListedPlayerRange, world.Population.ReportedRange);
        Fact(L.AveragePlayers, world.Population.AverageCount?.ToString("0.#", CultureInfo.CurrentCulture));
        // A count another listing reported stays a quiet, dated fact; only Wandur's own count is shown as live above.
        if (world.LivePlayerCount is null && world.Population.LatestCount is { } reported)
        {
            Fact(L.LastObservedPlayers, reported.ToString(CultureInfo.CurrentCulture));
            Fact(L.PlayersObserved, world.Population.ObservedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        }
        Fact(L.StatusChecked, world.Availability.CheckedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        Fact(L.LastReached, world.Availability.LastOnlineAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        if (world.Availability.Archived == true) Fact(L.ArchiveReason, world.Availability.ArchiveReason);
        Fact(L.ListingUpdated, world.Source.UpdatedAt?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture));
        var edge = new Border { Name = "DirectoryQuietFacts", BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 12, 0, 0), Margin = new Thickness(0, 4, 0, 0), Child = grid };
        edge.IsVisible = grid.Children.Count > 0;
        return edge.Paint(Border.BorderBrushProperty, "LineBrush");
    }

    private Control AddressBox(WorldListing world)
    {
        var address = new SelectableTextBlock
        {
            Text = world.Address, FontSize = 14, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Menlo, Consolas, DejaVu Sans Mono, monospace")
        }.Paint(TextBlock.ForegroundProperty, "TextBrush");
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10, Children = { address } };
        if (world.CanConnect)
        {
            var copy = DirectoryLook.Link(L.Copy, async () =>
            {
                try { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(world.Address); }
                catch (Exception) { }
            }, 13, "AccentTextBrush", FontWeight.Normal);
            copy.Name = "DirectoryCopyAddress";
            Avalonia.Automation.AutomationProperties.SetName(copy, L.CopyAddress);
            ToolTip.SetTip(copy, L.CopyAddress);
            Grid.SetColumn(copy, 1); row.Children.Add(copy);
        }
        var box = new Border { Name = "DirectoryAddress", Padding = new Thickness(14, 11), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = row };
        return box.Paint(Border.BackgroundProperty, "ShellBrush").Paint(Border.BorderBrushProperty, "LineBrush");
    }

    private static Control FormattedDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return DirectoryLook.Label(L.NoFurtherDescriptionProvided, 15, "MutedBrush");
        var panel = new StackPanel { Spacing = 10 };
        var paragraph = new List<string>();
        SelectableTextBlock Prose(string text) => new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 15.5, LineHeight = 25 }
            .Paint(TextBlock.ForegroundProperty, "MutedBrush");
        void Flush()
        {
            if (paragraph.Count == 0) return;
            panel.Children.Add(Prose(string.Join("\n", paragraph)));
            paragraph.Clear();
        }
        // Interpret only plain-text paragraph, list and heading conventions. No HTML execution.
        foreach (var raw in description.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) { Flush(); continue; }
            var bullet = Regex.Match(line, @"^(?:[-*•]\s+|(?<number>\d+[.)])\s+)(?<text>.+)$", RegexOptions.None, TimeSpan.FromMilliseconds(100));
            if (bullet.Success)
            {
                Flush();
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*"), ColumnSpacing = 4 };
                row.Children.Add(DirectoryLook.Label(bullet.Groups["number"].Success ? bullet.Groups["number"].Value : "•", 15.5, "MutedBrush"));
                var text = Prose(bullet.Groups["text"].Value);
                row.Children.Add(text); Grid.SetColumn(text, 1); panel.Children.Add(row);
            }
            else if (line.Length <= 80 && (line.EndsWith(':') || line.StartsWith("## ")))
            {
                Flush(); var heading = DirectoryLook.Label(line.TrimStart('#', ' ').TrimEnd(':'), 16, weight: FontWeight.SemiBold);
                heading.Margin = new Thickness(0, 8, 0, 0); panel.Children.Add(heading);
            }
            else paragraph.Add(raw);
        }
        Flush(); return panel;
    }
}
