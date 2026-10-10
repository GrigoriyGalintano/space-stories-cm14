using System.Globalization;
using System.Numerics;
using Content.Client._RMC14.UserInterface.Crt;
using Content.Shared._RMC14.MilitaryPolice;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._RMC14.MilitaryPolice;

/// <summary>
/// Shared CRT composition for the MP terminals. Both columns constrain wrapping content to their arranged width.
/// </summary>
public abstract class RMCPoliceWindow : DefaultWindow
{
    protected readonly BoxContainer Toolbar = Vertical();
    protected readonly BoxContainer Left = Vertical();
    protected readonly BoxContainer Right = Vertical();
    protected readonly RMCCrtLabel Connection = new() { Tone = RMCCrtTone.Warning, TextFontSize = 10 };
    private DefaultWindow? _editor;

    protected RMCPoliceWindow(LocId title)
    {
        Title = Loc.GetString(title);
        MinSize = new Vector2(720, 440);
        SetSize = new Vector2(1060, 660);

        var theme = new RMCCrtThemeScope
        {
            Palette = RMCCrtPalettePreset.Red,
            Effects = RMCCrtEffects.HorizontalScanlines,
            ScanlineOpacity = 0.1f,
        };
        Contents.AddChild(theme);
        var layout = Vertical();
        layout.Margin = new Thickness(10);
        theme.AddChild(layout);
        layout.AddChild(Toolbar);
        layout.AddChild(Connection);
        Connection.Visible = false;
        layout.AddChild(new RMCCrtSeparator { Margin = new Thickness(0, 2) });

        var columns = new RMCCrtTwoColumnContainer { VerticalExpand = true, HorizontalExpand = true };
        layout.AddChild(columns);
        columns.AddChild(ScrollPanel(Left));
        columns.AddChild(new RMCCrtSeparator
        {
            Orientation = RMCCrtSeparatorOrientation.Vertical,
            MinWidth = 1,
            MaxWidth = 1,
        });
        columns.AddChild(ScrollPanel(Right));
        OnClose += () =>
        {
            _editor?.Close();
            _editor = null;
        };
    }

    protected static BoxContainer Vertical() => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical,
        SeparationOverride = 6,
        HorizontalExpand = true,
    };

    protected static BoxContainer Horizontal() => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Horizontal,
        SeparationOverride = 6,
        HorizontalExpand = true,
    };

    private static RMCCrtPanel ScrollPanel(Control content)
    {
        var scroll = new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        content.Margin = new Thickness(8);
        scroll.AddChild(content);
        var panel = new RMCCrtPanel
        {
            Variant = RMCCrtPanelVariant.Transparent,
            BorderThickness = 0,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        panel.AddChild(scroll);
        return panel;
    }

    protected static RMCCrtLabel Heading(LocId key) => new()
    {
        Text = Loc.GetString(key),
        Heading = true,
        TextFontSize = 11,
        HorizontalExpand = true,
    };

    protected static RMCCrtLabel Text(string text, RMCCrtTone tone = RMCCrtTone.Default) => new()
    {
        Text = text,
        Tone = tone,
        TextFontSize = 10,
        HorizontalExpand = true,
    };

    protected static RMCCrtActionButton Button(LocId key, Action action, string? icon = null,
        bool danger = false, bool flat = false)
    {
        var button = new RMCCrtActionButton
        {
            Text = Loc.GetString(key),
            TextFontSize = 10,
            MinHeight = 30,
            IconState = icon,
            IconSize = 14,
            ContentMargin = new Thickness(7, 4),
            HorizontalExpand = true,
            Variant = danger ? RMCCrtButtonVariant.Danger :
                flat ? RMCCrtButtonVariant.Flat : RMCCrtButtonVariant.Outline,
        };
        button.Background.ScanlineOpacity = 0.08f;
        button.OnPressed += _ => action();
        return button;
    }

    protected void Edit(LocId title, string value, Action<string> submit, int maxLength = 4096,
        bool multiline = true, Func<string, string?>? validate = null)
    {
        var editor = new RMCCrtInputDialog(Loc.GetString(title), value, maxLength, multiline)
        {
            Validate = validate,
            FormatCounter = (length, max) => Loc.GetString("rmc-police-text-length", ("length", length), ("max", max)),
        };
        editor.ThemeScope.Palette = RMCCrtPalettePreset.Red;
        editor.Save.Text = Loc.GetString("rmc-police-save");
        editor.Cancel.Text = Loc.GetString("rmc-police-cancel");
        editor.Submitted += submit;
        OpenEditor(editor);
    }

    protected void Confirm(LocId title, Action action)
    {
        _editor?.Close();
        var editor = new DefaultWindow { Title = Loc.GetString(title), SetSize = new Vector2(420, 170) };
        var theme = new RMCCrtThemeScope { Palette = RMCCrtPalettePreset.Red };
        editor.Contents.AddChild(theme);
        var layout = Vertical();
        layout.Margin = new Thickness(8);
        theme.AddChild(layout);
        layout.AddChild(Text(Loc.GetString("rmc-police-confirm-description"), RMCCrtTone.Warning));
        var actions = Horizontal();
        actions.AddChild(Button("rmc-police-confirm", () => { action(); editor.Close(); }, RMCCrtIcons.Ban, true));
        actions.AddChild(Button("rmc-police-cancel", () => editor.Close()));
        layout.AddChild(actions);
        OpenEditor(editor);
    }

    protected void OpenEditor(DefaultWindow editor)
    {
        _editor?.Close();
        _editor = editor;
        editor.OpenCentered();
    }

    protected static string Sentence(RMCIncidentReport report, IPrototypeManager prototypes)
    {
        var sentence = report.CalculateSentence(prototypes);
        return sentence >= RMCIncidentReport.PermanentSentence
            ? Loc.GetString("rmc-police-permanent")
            : Loc.GetString("rmc-police-minutes", ("minutes", sentence.ToString("0.##", CultureInfo.InvariantCulture)));
    }

    protected static void IncidentDetails(BoxContainer container, RMCIncidentReport report,
        IPrototypeManager prototypes)
    {
        container.AddChild(Text(Loc.GetString("rmc-police-suspect-value", ("name", report.Suspect))));
        container.AddChild(Text(Loc.GetString("rmc-police-sentence-value",
            ("sentence", Sentence(report, prototypes))), RMCCrtTone.Warning));
        container.AddChild(new RMCCrtSeparator());
        container.AddChild(Heading("rmc-police-summary"));
        container.AddChild(Text(string.IsNullOrWhiteSpace(report.Summary)
            ? Loc.GetString("rmc-police-none") : report.Summary));
        container.AddChild(Heading("rmc-police-incident-charges"));
        foreach (var charge in report.Charges)
        {
            if (prototypes.TryIndex(charge, out var law))
            {
                container.AddChild(Text($"• {Loc.GetString(law.Name)}"));
                if (law.SpecialPunishment is { } punishment)
                    container.AddChild(Text(Loc.GetString(punishment), RMCCrtTone.Warning));
                if (law.Conditions is { } conditions)
                    container.AddChild(Text(Loc.GetString(conditions), RMCCrtTone.Muted));
            }
        }
        if (report.Charges.Count == 0)
            container.AddChild(Text(Loc.GetString("rmc-police-none"), RMCCrtTone.Muted));
        container.AddChild(Heading("rmc-police-witnesses"));
        foreach (var entry in report.Witnesses)
            IncidentEntry(container, entry);
        if (report.Witnesses.Count == 0)
            container.AddChild(Text(Loc.GetString("rmc-police-none"), RMCCrtTone.Muted));
        container.AddChild(Heading("rmc-police-evidence"));
        foreach (var entry in report.Evidence)
            IncidentEntry(container, entry);
        if (report.Evidence.Count == 0)
            container.AddChild(Text(Loc.GetString("rmc-police-none"), RMCCrtTone.Muted));
    }

    private static void IncidentEntry(BoxContainer container, RMCIncidentEntry entry)
    {
        container.AddChild(Text($"• {entry.Name}"));
        if (!string.IsNullOrWhiteSpace(entry.Notes))
        {
            var notes = Text(entry.Notes, RMCCrtTone.Muted);
            notes.Margin = new Thickness(12, 0, 0, 4);
            container.AddChild(notes);
        }
    }
}
