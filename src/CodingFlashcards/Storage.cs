using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodingFlashcards;

public sealed class Storage : IDisposable
{
    public static string Root { get; } = Environment.GetEnvironmentVariable("CODINGFLASHCARDS_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodingFlashcards");
    public static string SettingsPath => Path.Combine(Root, "settings.json");
    private IntPtr db;
    public Storage()
    {
        Directory.CreateDirectory(Root);
        if (sqlite3_open(Path.Combine(Root,"practice.db"), out db) != 0) throw new IOException("Tidak dapat membuka database latihan.");
        sqlite3_busy_timeout(db, 5000);
        Execute("PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS questions(hash TEXT PRIMARY KEY,category TEXT NOT NULL,json TEXT NOT NULL); CREATE TABLE IF NOT EXISTS sessions(id TEXT PRIMARY KEY,json TEXT NOT NULL); CREATE TABLE IF NOT EXISTS state(key TEXT PRIMARY KEY,value TEXT NOT NULL); PRAGMA user_version=1;");
    }
    public AppSettings LoadSettings()
    {
        if (!File.Exists(SettingsPath)) return new();
        var json = File.ReadAllText(SettingsPath);
        var settings = Json.Read<AppSettings>(json);
        using var doc = JsonDocument.Parse(json);
        var properties = doc.RootElement.EnumerateObject().ToList();
        if (!properties.Any(p => p.Name.Equals("selectedLanguages", StringComparison.OrdinalIgnoreCase)))
        {
            var legacy = properties.FirstOrDefault(p => p.Name.Equals("language", StringComparison.OrdinalIgnoreCase));
            if (legacy.Value.ValueKind == JsonValueKind.String) settings.SelectedLanguages = [legacy.Value.GetString()!];
        }
        settings.Version = 2;
        settings.Validate();
        return settings;
    }
    public void SaveSettings(AppSettings settings)
    {
        settings.Validate();
        var previous = LoadSettings();
        Execute("BEGIN IMMEDIATE;");
        try {
            if (!previous.SelectedLanguages.Order().SequenceEqual(settings.SelectedLanguages.Order())) Execute("DELETE FROM state WHERE key='language-rounds';");
            AtomicWrite(SettingsPath, Json.Write(settings));
            Execute("COMMIT;");
        } catch { Execute("ROLLBACK;"); throw; }
    }
    internal string? ReadRounds() => Query("SELECT value FROM state WHERE key='language-rounds';").FirstOrDefault();
    public void CommitPlan(AppSettings settings, SessionPlan plan, List<Question> questions, CancellationToken cancel = default)
    {
        if (!plan.Matches(settings) || plan.Committed || questions.Count != plan.Requests.Count) throw new InvalidOperationException("Rencana sesi tidak sesuai atau sudah digunakan.");
        for(var i=0;i<questions.Count;i++) {
            questions[i].Validate();
            if(questions[i].Language != plan.Requests[i].Language || questions[i].Category != plan.Requests[i].Category || settings.Difficulty != "Campuran" && questions[i].Difficulty != settings.Difficulty) throw new FormatException("Soal tidak sesuai rencana sesi.");
        }
        if(questions.DistinctBy(q=>q.Hash).Count()!=questions.Count) throw new FormatException("Soal duplikat.");
        Execute("BEGIN IMMEDIATE;");
        try {
            cancel.ThrowIfCancellationRequested();
            if(ReadRounds()!=plan.PreviousRounds) throw new InvalidOperationException("Giliran bahasa sudah berubah. Muat ulang sesi.");
            SaveQuestions(questions);
            foreach(var group in plan.Requests.GroupBy(q=>q.Language)) Advance(group.Key,group.Count());
            Execute($"INSERT OR REPLACE INTO state VALUES('language-rounds',{Sql(Json.Write(plan.NextRounds))});");
            cancel.ThrowIfCancellationRequested();
            Execute("COMMIT;");
            plan.Committed=true;
        } catch { Execute("ROLLBACK;"); throw; }
    }
    public static void AtomicWrite(string path, string data)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, data, new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }
    public void SaveQuestions(IEnumerable<Question> questions)
    {
        foreach (var q in questions) Execute($"INSERT OR IGNORE INTO questions VALUES({Sql(q.Hash)},{Sql(q.Category)},{Sql(Json.Write(q))});");
    }
    public List<Question> Cached(AppSettings settings, SessionPlan plan)
    {
        if (!plan.Matches(settings) || plan.Committed) return [];
        var pool = Query("SELECT json FROM questions ORDER BY rowid DESC LIMIT 2000;").Select(Json.Read<Question>).ToList();
        var result = new List<Question>();
        var used = new HashSet<string>();
        foreach(var request in plan.Requests) {
            var question=pool.FirstOrDefault(q=>q.Language==request.Language && q.Category==request.Category && (settings.Difficulty=="Campuran" || q.Difficulty==settings.Difficulty) && !used.Contains(q.Hash));
            if(question is null) return [];
            result.Add(question); used.Add(question.Hash);
        }
        return result;
    }
    public void SaveSession(PracticeSession session) => Execute($"INSERT OR REPLACE INTO sessions VALUES({Sql(session.Id)},{Sql(Json.Write(session))});");
    public List<PracticeSession> History() => Query("SELECT json FROM sessions ORDER BY rowid DESC LIMIT 100;").Select(Json.Read<PracticeSession>).OrderByDescending(x=>x.Started).ToList();
    public int RotationIndex(string language) => int.TryParse(Query($"SELECT value FROM state WHERE key={Sql("rotation:"+language)};").FirstOrDefault(), out var n) ? n : 0;
    public void Advance(string language,int count) => Execute($"INSERT OR REPLACE INTO state VALUES({Sql("rotation:"+language)},{Sql((RotationIndex(language) + count).ToString())});");
    private static string Sql(string value) => "'" + value.Replace("'", "''") + "'";
    private void Execute(string sql)
    {
        var code = sqlite3_exec(db, sql, IntPtr.Zero, IntPtr.Zero, out var error);
        if (error != IntPtr.Zero) sqlite3_free(error);
        if (code != 0) throw new IOException("Operasi database gagal (" + code + ").");
    }
    private List<string> Query(string sql)
    {
        if (sqlite3_prepare_v2(db, sql, -1, out var statement, IntPtr.Zero) != 0) throw new IOException("Pembacaan database gagal.");
        try {
            var rows = new List<string>(); int result;
            while ((result = sqlite3_step(statement)) == 100) rows.Add(Marshal.PtrToStringUTF8(sqlite3_column_text(statement,0)) ?? "");
            if (result != 101) throw new IOException("Pembacaan database belum selesai.");
            return rows;
        } finally { sqlite3_finalize(statement); }
    }
    public void Dispose() { if (db != IntPtr.Zero) { sqlite3_close(db); db=IntPtr.Zero; } }
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr handle);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr handle);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr handle,int ms);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_exec(IntPtr handle,[MarshalAs(UnmanagedType.LPUTF8Str)] string sql,IntPtr callback,IntPtr arg,out IntPtr error);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] private static extern void sqlite3_free(IntPtr pointer);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr handle,[MarshalAs(UnmanagedType.LPUTF8Str)] string sql,int length,out IntPtr statement,IntPtr tail);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr statement,int column);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr statement);
}
public static class Secrets
{
    private static string PathName => Path.Combine(Storage.Root, "key.protected");
    public static string Load()
    {
        if (File.Exists(PathName)) return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(PathName), null, DataProtectionScope.CurrentUser));
        foreach (var scope in new[] {EnvironmentVariableTarget.Process,EnvironmentVariableTarget.User,EnvironmentVariableTarget.Machine})
        {
            var key = Environment.GetEnvironmentVariable("kelontongai",scope);
            if (!string.IsNullOrWhiteSpace(key)) { Save(key); return key.Trim(); }
        }
        return "";
    }
    public static void Save(string key)
    {
        Directory.CreateDirectory(Storage.Root);
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser);
        var temporary=PathName+".tmp"; File.WriteAllBytes(temporary,bytes); File.Move(temporary,PathName,true);
    }
}
