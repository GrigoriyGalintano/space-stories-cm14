using Robust.Client.UserInterface.Controls;

namespace Content.Client._RMC14.UserInterface.Crt;

/// <summary>
/// A flat, wrapping disclosure row for optional details. Its contents inherit the surrounding theme.
/// </summary>
public sealed class RMCCrtCollapsibleSection : BoxContainer
{
    public readonly RMCCrtLabel Summary = new()
    {
        TextFontSize = 10,
        HorizontalExpand = true,
        Margin = new Thickness(18, 0, 4, 4),
        Visible = false,
    };
    public readonly BoxContainer Body = new()
    {
        Orientation = LayoutOrientation.Vertical,
        SeparationOverride = 6,
        HorizontalExpand = true,
        Margin = new Thickness(10, 0, 4, 8),
        Visible = false,
    };
    private readonly RMCCrtActionButton _header = new()
    {
        Variant = RMCCrtButtonVariant.Flat,
        ContentAlignment = RMCCrtContentAlignment.Left,
        TextFontSize = 10,
        HorizontalExpand = true,
    };
    private string _title = string.Empty;
    private bool _expanded;

    public event Action<bool>? OnToggled;

    public string Title
    {
        get => _title;
        set
        {
            _title = value;
            UpdateHeader();
        }
    }

    public bool Expanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value)
                return;
            _expanded = value;
            Body.Visible = value;
            UpdateHeader();
            OnToggled?.Invoke(value);
        }
    }

    public RMCCrtCollapsibleSection()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 4;
        HorizontalExpand = true;
        _header.Background.ScanlineOpacity = 0.08f;
        AddChild(_header);
        AddChild(Summary);
        AddChild(Body);
        _header.OnPressed += _ => Expanded = !Expanded;
        UpdateHeader();
    }

    private void UpdateHeader()
    {
        _header.Text = $"{(Expanded ? "▾" : "▸")} {Title}";
        _header.Selected = Expanded;
    }
}
