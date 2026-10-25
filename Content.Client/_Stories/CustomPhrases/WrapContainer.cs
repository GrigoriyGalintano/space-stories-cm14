using System.Numerics;
using Robust.Client.UserInterface;

namespace Content.Client._Stories.CustomPhrases;

public sealed class WrapContainer : Control
{
    public float Separation { get; set; } = 4;

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var x = 0f;
        var y = 0f;
        var rowHeight = 0f;
        var width = 0f;

        foreach (var child in Children)
        {
            if (!child.Visible)
                continue;

            child.Measure(availableSize);
            var size = child.DesiredSize;

            if (x > 0 && x + size.X > availableSize.X)
            {
                y += rowHeight + Separation;
                x = 0;
                rowHeight = 0;
            }

            width = Math.Max(width, x + size.X);
            x += size.X + Separation;
            rowHeight = Math.Max(rowHeight, size.Y);
        }

        return new Vector2(width, y + rowHeight);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var x = 0f;
        var y = 0f;
        var rowHeight = 0f;

        foreach (var child in Children)
        {
            if (!child.Visible)
                continue;

            var size = child.DesiredSize;

            if (x > 0 && x + size.X > finalSize.X)
            {
                y += rowHeight + Separation;
                x = 0;
                rowHeight = 0;
            }

            child.Arrange(UIBox2.FromDimensions(new Vector2(x, y), size));
            x += size.X + Separation;
            rowHeight = Math.Max(rowHeight, size.Y);
        }

        return finalSize;
    }
}
