using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._RMC14.UserInterface.Crt;

/// <summary>
/// A multiline editor in a CRT inset. The standard editor handles selection, wrapping, and scrolling.
/// </summary>
public sealed class RMCCrtTextEdit : PanelContainer
{
    public readonly TextEdit Input = new()
    {
        HorizontalExpand = true,
        VerticalExpand = true,
        Margin = new Thickness(8),
    };

    public string Text
    {
        get => Rope.Collapse(Input.TextRope);
        set => Input.TextRope = new Rope.Leaf(value);
    }

    public RMCCrtTextEdit()
    {
        Input.AddStyleClass(RMCCrtStyleClasses.Input);
        var panel = new RMCCrtPanel { Variant = RMCCrtPanelVariant.Inset };
        panel.AddChild(Input);
        AddChild(panel);
    }
}
