using System.Linq;
using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._RMC14.UserInterface.Crt;

/// <summary>
/// A wrapping selector whose detached popup keeps the owner's theme, including live appearance changes.
/// </summary>
public sealed class RMCCrtOptionButton : Container, IRMCCrtThemedControl
{
    private readonly RMCCrtActionButton _button = new()
    {
        ContentAlignment = RMCCrtContentAlignment.Left,
        ContentMargin = new Thickness(8, 5),
        TextFontSize = 10,
        HorizontalExpand = true,
        MinHeight = 30,
    };
    private readonly Popup _popup = new();
    private readonly RMCCrtPanel _panel = new() { BackgroundOpacity = 1 };
    private readonly BoxContainer _options = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly ScrollContainer _scroll = new() { HScrollEnabled = false, MaxHeight = 280, ReturnMeasure = true };
    private readonly List<(int Id, string Label, RMCCrtActionButton Button)> _items = new();
    private RMCCrtThemeContext _context;

    public event Action<int>? OnItemSelected;
    public int SelectedId { get; private set; }
    public int ItemCount => _items.Count;

    public bool Disabled
    {
        get => _button.Disabled;
        set
        {
            _button.Disabled = value;
            if (value)
                _popup.Close();
        }
    }

    public RMCCrtOptionButton()
    {
        _button.Background.ScanlineOpacity = 0.08f;
        AddChild(_button);
        _scroll.AddChild(_options);
        _panel.AddChild(_scroll);
        _popup.AddChild(_panel);
        _button.OnPressed += _ => OpenPopup();
        _popup.OnPopupHide += () => _popup.Parent?.RemoveChild(_popup);
    }

    public void AddItem(string label, int? id = null)
    {
        var itemId = id ?? _items.Count;
        if (_items.Any(item => item.Id == itemId))
            throw new ArgumentException("An item with the same ID already exists.", nameof(id));

        var option = new RMCCrtActionButton
        {
            Text = label,
            TextFontSize = 10,
            Variant = RMCCrtButtonVariant.Flat,
            ContentAlignment = RMCCrtContentAlignment.Left,
            HorizontalExpand = true,
            MinHeight = 30,
        };
        option.Background.ScanlineOpacity = 0.08f;
        option.OnPressed += _ =>
        {
            SelectId(itemId);
            _popup.Close();
            OnItemSelected?.Invoke(itemId);
        };
        _items.Add((itemId, label, option));
        _options.AddChild(option);
        if (_items.Count == 1)
            SelectId(itemId);
    }

    public void SelectId(int id)
    {
        var index = _items.FindIndex(item => item.Id == id);
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(id));

        SelectedId = id;
        _button.Text = $"{_items[index].Label}  ▾";
        foreach (var item in _items)
            item.Button.Selected = item.Id == id;
    }

    void IRMCCrtThemedControl.ApplyCrtTheme(RMCCrtThemeContext context)
    {
        _context = context;
        RMCCrtThemeHelpers.ApplyToDescendants(_popup, context);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        ((IRMCCrtThemedControl) this).ApplyCrtTheme(RMCCrtThemeHelpers.FindContext(this));
    }

    protected override void ExitedTree()
    {
        _popup.Close();
        base.ExitedTree();
    }

    private void OpenPopup()
    {
        if (_items.Count == 0 || Disabled)
            return;

        UserInterfaceManager.ModalRoot.AddChild(_popup);
        RMCCrtThemeHelpers.ApplyToDescendants(_popup, _context);
        _scroll.SetWidth = Width;
        _scroll.Measure(new Vector2(Width, _scroll.MaxHeight));
        _popup.Open(UIBox2.FromDimensions(GlobalPosition + new Vector2(0, Height + 1),
            new Vector2(Width, Math.Min(_scroll.DesiredSize.Y, _scroll.MaxHeight))));
    }
}
