namespace Content.Client._Stories.CustomPhrases;

[DataDefinition]
public sealed partial class CustomPhrase
{
    [DataField]
    public string Text = string.Empty;

    [DataField]
    public List<string> Tags = new();
}
