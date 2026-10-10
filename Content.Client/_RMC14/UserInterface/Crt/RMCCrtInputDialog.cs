using System.Numerics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._RMC14.UserInterface.Crt;

/// <summary>
/// A reusable single-line or multiline editor with length and domain validation.
/// Callers supply localized labels, validation errors, and the counter formatter.
/// </summary>
public sealed class RMCCrtInputDialog : DefaultWindow
{
    public readonly RMCCrtThemeScope ThemeScope = new();
    public readonly RMCCrtActionButton Save = new()
    {
        TextFontSize = 10,
        HorizontalExpand = true,
        MinHeight = 32,
        Variant = RMCCrtButtonVariant.Filled,
    };
    public readonly RMCCrtActionButton Cancel = new() { TextFontSize = 10, HorizontalExpand = true, MinHeight = 32 };
    private readonly RMCCrtLabel _validation = new() { Tone = RMCCrtTone.Danger, TextFontSize = 10 };
    private readonly RMCCrtLabel _counter = new() { Tone = RMCCrtTone.Muted, TextFontSize = 10 };
    private readonly RMCCrtLineEdit? _line;
    private readonly RMCCrtTextEdit? _multiline;
    private readonly int _maxLength;

    public event Action<string>? Submitted;
    public Func<string, string?>? Validate { get; set; }
    public Func<int, int, string>? FormatCounter { get; set; }
    public string Value => _line?.Text ?? _multiline!.Text;

    public RMCCrtInputDialog(string title, string value, int maxLength, bool multiline = true)
    {
        Title = title;
        _maxLength = maxLength;
        MinSize = new Vector2(320, multiline ? 260 : 200);
        SetSize = new Vector2(multiline ? 540 : 420, multiline ? 330 : 200);
        Contents.AddChild(ThemeScope);
        var layout = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            Margin = new Thickness(10),
        };
        ThemeScope.AddChild(layout);
        if (multiline)
        {
            _multiline = new RMCCrtTextEdit { Text = value, HorizontalExpand = true, VerticalExpand = true };
            _multiline.Input.OnTextChanged += _ => RefreshValidation();
            layout.AddChild(_multiline);
        }
        else
        {
            _line = new RMCCrtLineEdit { Text = value, HorizontalExpand = true };
            _line.OnTextChanged += _ => RefreshValidation();
            _line.OnTextEntered += _ => Submit();
            layout.AddChild(_line);
        }
        layout.AddChild(_counter);
        layout.AddChild(_validation);
        var actions = new BoxContainer { SeparationOverride = 8, MinHeight = 32 };
        actions.AddChild(Save);
        actions.AddChild(Cancel);
        layout.AddChild(actions);
        Save.OnPressed += _ => Submit();
        Cancel.OnPressed += _ => Close();
    }

    public void RefreshValidation()
    {
        var value = Value;
        var error = Validate?.Invoke(value);
        Save.Disabled = value.Length > _maxLength || error != null;
        _counter.Text = FormatCounter?.Invoke(value.Length, _maxLength) ?? $"{value.Length} / {_maxLength}";
        _counter.Tone = value.Length > _maxLength ? RMCCrtTone.Danger : RMCCrtTone.Muted;
        _validation.Text = error;
        _validation.Visible = error != null;
    }

    protected override void Opened()
    {
        base.Opened();
        RefreshValidation();
        if (_line != null)
            _line.GrabKeyboardFocus();
        else
            _multiline!.Input.GrabKeyboardFocus();
    }

    private void Submit()
    {
        RefreshValidation();
        if (Save.Disabled)
            return;

        Submitted?.Invoke(Value);
        Close();
    }
}
