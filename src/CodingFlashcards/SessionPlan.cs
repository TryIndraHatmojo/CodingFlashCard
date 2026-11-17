namespace CodingFlashcards;

public sealed record QuestionRequest(string Language, string Category);

public sealed class LanguageRounds
{
    public string Selection { get; set; } = "";
    public Dictionary<string, List<string>> Remaining { get; set; } = [];
}

public sealed class SessionPlan
{
    private readonly List<QuestionRequest> requests = [];
    public IReadOnlyList<QuestionRequest> Requests => requests.AsReadOnly();
    internal string SettingsJson { get; }
    internal string? PreviousRounds { get; }
    internal LanguageRounds NextRounds { get; }
    internal bool Committed { get; set; }

    private SessionPlan(AppSettings settings, Storage storage)
    {
        SettingsJson = Json.Write(settings);
        PreviousRounds = storage.ReadRounds();
        NextRounds = PreviousRounds is null ? new() : Json.Read<LanguageRounds>(PreviousRounds);
        var selection = string.Join(",", settings.SelectedLanguages.Order());
        if (NextRounds.Selection != selection) NextRounds = new() { Selection = selection };
    }

    public bool Matches(AppSettings settings) => SettingsJson == Json.Write(settings);

    public static SessionPlan Create(AppSettings settings, Storage storage, Random? random = null)
    {
        settings.Validate();
        random ??= Random.Shared;
        var plan = new SessionPlan(settings, storage);
        var mode = settings.LanguageMode.ToString();
        if (!plan.NextRounds.Remaining.TryGetValue(mode, out var remaining))
            plan.NextRounds.Remaining[mode] = remaining = [];
        string Draw()
        {
            if (remaining.Count == 0)
            {
                remaining.AddRange(settings.SelectedLanguages.Order());
                for (var i = remaining.Count - 1; i > 0; i--)
                {
                    var j = random.Next(i + 1);
                    (remaining[i], remaining[j]) = (remaining[j], remaining[i]);
                }
            }
            var language = remaining[0];
            remaining.RemoveAt(0);
            return language;
        }
        var sessionLanguage = settings.LanguageMode == LanguageSelectionMode.SingleLanguagePerSession ? Draw() : null;
        var offsets = new Dictionary<string, int>();
        for (var i = 0; i < settings.QuestionCount; i++)
        {
            var language = sessionLanguage ?? Draw();
            var categories = settings.SelectedCategories.Where(c => Categories.ForLanguage(language).Contains(c)).ToList();
            var offset = offsets.GetValueOrDefault(language);
            var category = categories[(storage.RotationIndex(language) + offset) % categories.Count];
            offsets[language] = offset + 1;
            plan.requests.Add(new(language, category));
        }
        return plan;
    }
}
