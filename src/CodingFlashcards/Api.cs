using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IO;

namespace CodingFlashcards;

public sealed class KelontongClient : IDisposable
{
    private readonly HttpClient http;
    public KelontongClient(TimeSpan? timeout=null)
    {
        http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}) {Timeout=timeout??TimeSpan.FromSeconds(90)};
    }
    private async Task<string> Send(AppSettings settings,string key,string path,object? body,CancellationToken cancel)
    {
        settings.Validate(); if(string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("API key belum tersedia. Buka Settings.");
        using var request=new HttpRequestMessage(body is null?HttpMethod.Get:HttpMethod.Post,settings.BaseUrl.TrimEnd('/')+"/"+path);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key.Trim());
        if(body is not null) request.Content=new StringContent(Json.Write(body),Encoding.UTF8,"application/json");
        try {
            using var response=await http.SendAsync(request,cancel);
            if(!response.IsSuccessStatusCode) throw new ApiException((int)response.StatusCode,response.StatusCode switch {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden=>"Autentikasi ditolak. Periksa API key dan akses akun.",
                HttpStatusCode.PaymentRequired=>"Saldo atau kuota API tidak mencukupi.",
                HttpStatusCode.NotFound=>"Endpoint atau model tidak tersedia. Periksa Base URL dan ID model.",
                HttpStatusCode.TooManyRequests=>"Batas penggunaan API tercapai. Coba lagi nanti.",
                _=>$"Layanan API mengembalikan HTTP {(int)response.StatusCode}. Coba lagi atau periksa pengaturan."
            });
            var content=await response.Content.ReadAsStringAsync(cancel);
            if(content.Length>2_000_000) throw new FormatException("Respons API terlalu besar.");
            return content;
        } catch(TaskCanceledException) when(!cancel.IsCancellationRequested) { throw new TimeoutException("API tidak merespons dalam batas waktu. Coba lagi."); }
        catch(HttpRequestException) { throw new IOException("Tidak dapat tersambung ke KelontongAI. Periksa internet dan Base URL."); }
    }
    private async Task<string> Chat(AppSettings settings,string key,string prompt,CancellationToken cancel)
    {
        var response=await Send(settings,key,"chat/completions",new {model=settings.Model,stream=false,messages=new[]{new{role="system",content="Anda menyusun soal pilihan ganda persiapan interview coding berbahasa Indonesia. Berikan JSON saja tanpa markdown. Tepat satu jawaban benar. Pembahasan harus menjelaskan langkah dan alasan jawaban. Jangan membuat soal psikotes. Kode hanya ditampilkan, tidak dijalankan aplikasi."},new{role="user",content=prompt}}},cancel);
        using var doc=JsonDocument.Parse(response);
        try { return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()??throw new FormatException("Konten respons kosong."); }
        catch(Exception e) when(e is KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException) {throw new FormatException("Format chat completions tidak sesuai.");}
    }
    public async Task Test(AppSettings settings,string key,CancellationToken cancel) => await Chat(settings,key,"Balas dengan JSON {\"ok\":true} saja.",cancel);
    public async Task<List<string>> Models(AppSettings settings,string key,CancellationToken cancel)
    {
        using var doc=JsonDocument.Parse(await Send(settings,key,"models",null,cancel));
        var list=doc.RootElement.GetProperty("data").EnumerateArray().Select(x=>x.GetProperty("id").GetString()!).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct().Order().ToList();
        if(list.Count==0) throw new FormatException("Daftar model dari API kosong."); return list;
    }
    public async Task<List<Question>> Generate(AppSettings settings,string key,Storage storage,IProgress<string>? progress,CancellationToken cancel,SessionPlan? plan=null)
    {
        settings.Validate();plan ??= SessionPlan.Create(settings,storage);
        if(!plan.Matches(settings) || plan.Committed) throw new InvalidOperationException("Rencana sesi tidak sesuai.");
        var result=new List<Question>();
        for(var offset=0;offset<settings.QuestionCount;offset+=5)
        {
            var requests=plan.Requests.Skip(offset).Take(5).ToArray();
            var specification = Json.Write(new { difficulty=settings.Difficulty, requests });
            var prompt="Buat soal coding sesuai spesifikasi JSON berikut: " + specification + "\nNonce: " + Guid.NewGuid() +
                ". Respons {\"questions\":[...]}, tepat satu item per pasangan language-category dalam requests sesuai urutan. Setiap item wajib memiliki language (ID persis), difficulty (Mudah/Sedang/Sulit; jika Campuran variasikan), category, prompt, code (string tanpa pagar markdown; kosong bila tidak diperlukan), options (4 string berbeda), answerIndex (0..3), explanation. " +
                "Gunakan hanya bahasa dan kategori yang diminta. output dan debugging wajib menyertakan code. Gunakan kode singkat dan deterministik, hindari undefined behavior, sebutkan versi bahasa/runtime bila memengaruhi jawaban. Semua input dan konteks harus lengkap. SQL memakai standar ANSI; soal hasil query wajib memuat skema tabel dan seluruh data contoh di prompt/code. Escape newline dan karakter kode dengan benar sebagai string JSON. Pembahasan berbahasa Indonesia, jelaskan hasil/bug/kompleksitas sesuai topik.";
            Exception? error=null; List<Question>? batch=null;
            for(var attempt=0;attempt<2;attempt++)
            {
                progress?.Report($"Membuat soal {offset+1}–{offset+requests.Length} dari {settings.QuestionCount}"+(attempt==1?" · memperbaiki format":""));
                try {
                    var content=await Chat(settings,key,prompt+(attempt==1?" Respons sebelumnya tidak valid. Periksa struktur, jumlah, urutan kategori dan hindari soal duplikat.":""),cancel);
                    content=content.Trim(); if(content.StartsWith("```")) {var newline=content.IndexOf('\n');content=content[(newline+1)..];content=content[..content.LastIndexOf("```")].Trim();}
                    using var doc=JsonDocument.Parse(content);
                    batch=doc.RootElement.GetProperty("questions").EnumerateArray().Select(x=>Json.Read<Question>(x.GetRawText())).ToList();
                    if(batch.Count!=requests.Length) throw new FormatException("Jumlah soal tidak sesuai.");
                    for(var i=0;i<batch.Count;i++) {
                        if(batch[i].Category!=requests[i].Category) throw new FormatException("Urutan kategori tidak sesuai.");
                        batch[i].Model=settings.Model;batch[i].Validate();
                        if(batch[i].Language != requests[i].Language || settings.Difficulty != "Campuran" && batch[i].Difficulty != settings.Difficulty) throw new FormatException("Bahasa atau tingkat kesulitan tidak sesuai.");
                    }
                    if(result.Concat(batch).DistinctBy(q=>q.Hash).Count()!=result.Count+batch.Count) throw new FormatException("Terdapat soal duplikat.");
                    error=null;break;
                } catch(Exception e) when(e is FormatException or JsonException or KeyNotFoundException or ArgumentOutOfRangeException or InvalidOperationException) {error=e;}
            }
            if(error is not null || batch is null) throw new FormatException("Soal dari model tidak lolos validasi setelah dua percobaan. Coba lagi atau pilih model lain.");
            result.AddRange(batch);
        }
        cancel.ThrowIfCancellationRequested();storage.CommitPlan(settings,plan,result,cancel);return result;
    }
    public void Dispose()=>http.Dispose();
}
public sealed class ApiException(int statusCode,string message):Exception(message) { public int StatusCode {get;}=statusCode; }
public static class ModelCatalog
{
    public static List<ModelEntry> Load()
    {
        var uri=new Uri("pack://application:,,,/CodingFlashcards;component/ModelCatalog.json");
        using var reader=new StreamReader(System.Windows.Application.GetResourceStream(uri)!.Stream);
        var models=Json.Read<List<ModelEntry>>(reader.ReadToEnd());
        var cache=Path.Combine(Storage.Root,"models.json");
        if(File.Exists(cache)) {try {models.AddRange(Json.Read<List<ModelEntry>>(File.ReadAllText(cache)));} catch(JsonException) {}}
        return models.DistinctBy(m=>m.Id).OrderBy(m=>m.Provider).ThenBy(m=>m.Id).ToList();
    }
    public static string Provider(string id)=>id.Split('-')[0] switch {"claude"=>"Claude","gpt"=>"GPT","deepseek"=>"Deepseek","glm"=>"GLM","kimi"=>"Kimi","qwen3.8"=>"Qwen","gemini"=>"Gemini","muse"=>"Meta","minimax"=>"MiniMax",var p=>p};
}
