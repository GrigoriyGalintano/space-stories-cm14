using System.Linq;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Chat;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared._Stories.Input;
using Content.Shared._Stories.SCCVars;
using Content.Shared.Chat;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Input.Binding;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;

namespace Content.Client._Stories.CustomPhrases;

public sealed class CustomPhrasesUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IResourceManager _res = default!;
    [Dependency] private readonly ISerializationManager _serialization = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly ResPath ConfigPath = new("/custom_phrases.yml");

    private readonly List<CustomPhrase> _phrases = new();
    private bool _loaded;
    private TimeSpan _nextSend;
    private CustomPhrasesWindow? _window;
    private ISawmill _sawmill = default!;

    public event Action? PhrasesChanged;

    private MenuButton? PhrasesButton => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.CustomPhrasesButton;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("custom_phrases");
    }

    public int MaxPhrases => _cfg.GetCVar(SCCVars.CustomPhrasesMaxPhrases);
    public int MaxTags => _cfg.GetCVar(SCCVars.CustomPhrasesMaxTags);
    public int MaxTagLength => _cfg.GetCVar(SCCVars.CustomPhrasesMaxTagLength);
    private TimeSpan SendCooldown => TimeSpan.FromSeconds(_cfg.GetCVar(SCCVars.CustomPhrasesSendCooldown));

    public IReadOnlyList<CustomPhrase> Phrases
    {
        get
        {
            EnsureLoaded();
            return _phrases;
        }
    }

    public float CooldownProgress => _timing.RealTime >= _nextSend || SendCooldown <= TimeSpan.Zero
        ? 0f
        : Math.Min(1f, (float) ((_nextSend - _timing.RealTime) / SendCooldown));

    public List<string> AllTags()
    {
        return Phrases
            .SelectMany(p => p.Tags)
            .Distinct()
            .OrderBy(t => t)
            .ToList();
    }

    public int CountTagsWith(CustomPhrase? replaced, IEnumerable<string> tags)
    {
        return Phrases
            .Where(p => p != replaced)
            .SelectMany(p => p.Tags)
            .Concat(tags)
            .Distinct()
            .Count();
    }

    public void OnStateEntered(GameplayState state)
    {
        CommandBinds.Builder
            .Bind(StoriesKeyFunctions.OpenCustomPhrases, InputCmdHandler.FromDelegate(_ => ToggleWindow()))
            .Register<CustomPhrasesUIController>();
    }

    public void OnStateExited(GameplayState state)
    {
        CommandBinds.Unregister<CustomPhrasesUIController>();
        _window?.Close();
    }

    public void LoadButton()
    {
        if (PhrasesButton == null)
            return;

        PhrasesButton.OnPressed += OnButtonPressed;
    }

    public void UnloadButton()
    {
        if (PhrasesButton == null)
            return;

        PhrasesButton.OnPressed -= OnButtonPressed;
    }

    private void OnButtonPressed(BaseButton.ButtonEventArgs args)
    {
        ToggleWindow();
    }

    public void ToggleWindow()
    {
        if (_window != null)
        {
            _window.Close();
            return;
        }

        _window = new CustomPhrasesWindow(this);
        _window.OnClose += OnWindowClosed;
        _window.OpenCentered();

        if (PhrasesButton != null)
            PhrasesButton.Pressed = true;
    }

    private void OnWindowClosed()
    {
        _window = null;

        if (PhrasesButton != null)
            PhrasesButton.Pressed = false;
    }

    public void Send(CustomPhrase phrase)
    {
        if (_timing.RealTime < _nextSend)
            return;

        _nextSend = _timing.RealTime + SendCooldown;
        UIManager.GetUIController<ChatUIController>().SendText(phrase.Text, ChatSelectChannel.Local);
    }

    public bool TryAdd(string text, List<string> tags)
    {
        EnsureLoaded();

        if (_phrases.Count >= MaxPhrases)
            return false;

        _phrases.Add(new CustomPhrase { Text = text, Tags = tags });
        OnChanged();
        return true;
    }

    public void Update(CustomPhrase phrase, string text, List<string> tags)
    {
        phrase.Text = text;
        phrase.Tags = tags;
        OnChanged();
    }

    public void Remove(CustomPhrase phrase)
    {
        if (_phrases.Remove(phrase))
            OnChanged();
    }

    public List<string> ParseTags(string input)
    {
        var tags = input.Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return NormalizeTags(tags);
    }

    public static string FormatTags(IEnumerable<string> tags)
    {
        return string.Join(" ", tags.Select(t => $"#{t}"));
    }

    private List<string> NormalizeTags(IEnumerable<string> tags)
    {
        return tags
            .Select(t => t.Trim().TrimStart('#').ToLowerInvariant())
            .Where(t => t.Length > 0 && t.Length <= MaxTagLength)
            .Distinct()
            .ToList();
    }

    private void OnChanged()
    {
        Save();
        PhrasesChanged?.Invoke();
    }

    private void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;

        if (!_res.UserData.Exists(ConfigPath))
            return;

        try
        {
            using var reader = _res.UserData.OpenText(ConfigPath);
            var documents = DataNodeParser.ParseYamlStream(reader);

            if (documents.FirstOrDefault()?.Root is not MappingDataNode root ||
                !root.TryGet("phrases", out var phrasesNode))
            {
                return;
            }

            var phrases = _serialization.Read<List<CustomPhrase>>(phrasesNode, notNullableOverride: true);
            foreach (var phrase in phrases)
            {
                if (string.IsNullOrWhiteSpace(phrase.Text) || _phrases.Count >= MaxPhrases)
                    continue;

                phrase.Tags = NormalizeTags(phrase.Tags);
                _phrases.Add(phrase);
            }
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to load custom phrases: {e}");
        }
    }

    private void Save()
    {
        try
        {
            var mapping = new MappingDataNode();
            mapping.Add("version", new ValueDataNode("1"));
            mapping.Add("phrases", _serialization.WriteValue(_phrases, notNullableOverride: true));

            using var writer = _res.UserData.OpenWriteText(ConfigPath);
            var stream = new YamlStream { new YamlDocument(mapping.ToYaml()) };
            stream.Save(writer, false);
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to save custom phrases: {e}");
        }
    }
}
