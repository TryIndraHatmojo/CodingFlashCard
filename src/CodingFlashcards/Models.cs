using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodingFlashcards;

public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public sealed class CategoryChoice(string id, string label) : Observable
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    private bool selected = true;
    public bool Selected { get => selected; set { selected = value; Changed(); } }
}
public static class Languages
{
    public static readonly Dictionary<string,string> All = new()
    {
        ["python"]="Python", ["javascript"]="JavaScript", ["typescript"]="TypeScript",
        ["php"]="PHP", ["csharp"]="C#", ["cpp"]="C++", ["java"]="Java", ["sql"]="SQL (ANSI)"
    };
}
public static class Categories
{
    public static readonly Dictionary<string,string> All = new()
    {
        ["fundamentals"]="Dasar bahasa", ["output"]="Output kode", ["debugging"]="Debugging",
        ["algorithms"]="Algoritma", ["data_structures"]="Struktur data",
        ["complexity"]="Kompleksitas", ["oop"]="OOP", ["sql"]="SQL"
    };
    public static List<string> ForLanguage(string language) => All.Keys.Where(c => (language == "sql") == (c == "sql")).ToList();
}
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<LanguageSelectionMode>))]
public enum LanguageSelectionMode { BalancedQuestions, SingleLanguagePerSession }

public sealed class AppSettings
{
    public int Version { get; set; } = 2;
    public string BaseUrl { get; set; } = "https://api.kelontongai.id/v1";
    public string Model { get; set; } = "deepseek-v4.1-flash";
    public int QuestionCount { get; set; } = 3;
    public int IntervalMinutes { get; set; } = 30;
    public bool ScheduleEnabled { get; set; }
    public List<string> SelectedLanguages { get; set; } = ["python"];
    public LanguageSelectionMode LanguageMode { get; set; } = LanguageSelectionMode.BalancedQuestions;
    public string Difficulty { get; set; } = "Campuran";
    public bool TimerEnabled { get; set; }
    public int SecondsPerQuestion { get; set; } = 60;
    public List<string> SelectedCategories { get; set; } = Categories.ForLanguage("python");
    public void Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(uri.IsLoopback && uri.Scheme == "http")) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) throw new ArgumentException("Base URL harus HTTPS (HTTP hanya untuk localhost), tanpa query atau kredensial.");
        if (string.IsNullOrWhiteSpace(Model)) throw new ArgumentException("Pilih atau masukkan ID model.");
        if (QuestionCount is < 1 or > 50) throw new ArgumentException("Jumlah soal harus 1–50.");
        if (IntervalMinutes is < 1 or > 1440) throw new ArgumentException("Interval harus 1–1.440 menit.");
        if (SecondsPerQuestion is < 5 or > 600) throw new ArgumentException("Timer harus 5–600 detik.");
        if (SelectedLanguages is null || SelectedLanguages.Count == 0 || SelectedLanguages.Any(l => l is null || !Languages.All.ContainsKey(l)) || SelectedLanguages.Distinct().Count() != SelectedLanguages.Count) throw new ArgumentException("Centang minimal satu bahasa yang tersedia, tanpa duplikat.");
        if (!Enum.IsDefined(LanguageMode)) throw new ArgumentException("Mode bahasa tidak valid.");
        if (Difficulty is not ("Mudah" or "Sedang" or "Sulit" or "Campuran")) throw new ArgumentException("Kesulitan tidak valid.");
        if (SelectedCategories is null || SelectedCategories.Count == 0 || SelectedCategories.Distinct().Count() != SelectedCategories.Count || SelectedCategories.Any(c => !SelectedLanguages.Any(l => Categories.ForLanguage(l).Contains(c)))) throw new ArgumentException("Pilih minimal satu tipe soal yang tersedia.");
        foreach (var language in SelectedLanguages)
            if (!SelectedCategories.Any(c => Categories.ForLanguage(language).Contains(c))) throw new ArgumentException("Pilih minimal satu tipe soal untuk " + Languages.All[language] + ".");
    }
}
public sealed class Question
{
    public string Language { get; set; } = "";
    public string Difficulty { get; set; } = "";
    public string Category { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string Code { get; set; } = "";
    public List<string> Options { get; set; } = [];
    public int AnswerIndex { get; set; } = -1;
    public string Explanation { get; set; } = "";
    public string Model { get; set; } = "";
    public string Hash => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json.Write(new[]{Language,Category,Prompt?.Trim(),Code}))));
    public void Validate()
    {
        if (Language is null || !Languages.All.ContainsKey(Language) || !Categories.ForLanguage(Language).Contains(Category) ||
            Difficulty is not ("Mudah" or "Sedang" or "Sulit") || string.IsNullOrWhiteSpace(Prompt) || Prompt.Length > 6000 ||
            Code is null || Code.Length > 20000 || string.IsNullOrWhiteSpace(Explanation) || Explanation.Length > 20000)
            throw new FormatException("Pertanyaan, bahasa, kesulitan atau pembahasan tidak valid.");
        if (Options is null || Options.Count != 4 || Options.Any(x=>string.IsNullOrWhiteSpace(x) || x.Length > 6000) ||
            Options.Select(x=>x.Trim()).Distinct(StringComparer.Ordinal).Count() != 4 || AnswerIndex is < 0 or > 3)
            throw new FormatException("Soal harus memiliki empat pilihan berbeda dan satu kunci yang sah.");
        if (Category is "output" or "debugging" && string.IsNullOrWhiteSpace(Code)) throw new FormatException("Soal ini memerlukan potongan kode.");
    }
}
public sealed record Attempt(Question Question, int? SelectedIndex, bool TimedOut);
public sealed class PracticeSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Started { get; set; } = DateTimeOffset.Now;
    public string Model { get; set; } = "";
    public string Status { get; set; } = "Belum selesai";
    public int Total { get; set; }
    public bool FromCache { get; set; }
    public List<Attempt> Attempts { get; set; } = [];
    public int Correct => Attempts.Count(a => !a.TimedOut && a.SelectedIndex == a.Question.AnswerIndex);
    public string Summary => $"{Started:dd MMM yyyy HH:mm}   •   {Correct}/{Total} benar   •   {Status}" + (FromCache ? "   •   Ulangan cache" : "");
}
public sealed record ModelEntry(string Id, string Provider)
{
    public override string ToString() => $"{Provider}  ·  {Id}";
}
public static class Json
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Options) ?? throw new FormatException("Data JSON kosong.");
}
