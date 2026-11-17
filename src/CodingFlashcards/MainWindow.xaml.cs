using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CodingFlashcards;
public sealed class SettingsViewModel
{
    public System.Collections.ObjectModel.ObservableCollection<CategoryChoice> Categories {get;}=[];
    public List<CategoryChoice> Languages {get;}=CodingFlashcards.Languages.All.Select(x=>new CategoryChoice(x.Key,x.Value){Selected=false}).ToList();
}
public partial class MainWindow : Window
{
    private readonly Storage storage=new();
    private readonly SettingsViewModel vm=new();
    private readonly CancellationTokenSource cancellation=new();
    private List<ModelEntry> models=[];
    private bool busy;
    public MainWindow()
    {
        InitializeComponent();DataContext=vm;DifficultyBox.ItemsSource=new[]{"Mudah","Sedang","Sulit","Campuran"};
        ModeBox.ItemsSource=new Dictionary<LanguageSelectionMode,string>{{LanguageSelectionMode.BalancedQuestions,"Acak merata"},{LanguageSelectionMode.SingleLanguagePerSession,"Acak satu bahasa per sesi"}};
        foreach(var language in vm.Languages)language.PropertyChanged+=(_,_)=>RefreshCategories();
        models=ModelCatalog.Load();SetModels();LoadSettings();RefreshHome();
        Closed+=(_,_)=>{cancellation.Cancel();storage.Dispose();};
    }
    private void SetModels()
    {
        var view=new ListCollectionView(models);view.GroupDescriptions.Add(new PropertyGroupDescription("Provider"));
        view.Filter=item=>item is ModelEntry entry && (entry.Id+entry.Provider).Contains(SearchBox.Text,StringComparison.OrdinalIgnoreCase);
        ModelBox.ItemsSource=view;
    }
    private void LoadSettings()
    {
        var s=storage.LoadSettings();BaseUrlBox.Text=s.BaseUrl;ModelBox.Text=s.Model;CountBox.Text=s.QuestionCount.ToString();IntervalBox.Text=s.IntervalMinutes.ToString();ScheduleBox.IsChecked=s.ScheduleEnabled;DifficultyBox.SelectedItem=s.Difficulty;TimerBox.IsChecked=s.TimerEnabled;SecondsBox.Text=s.SecondsPerQuestion.ToString();
        ModeBox.SelectedValue=s.LanguageMode;
        foreach(var language in vm.Languages)language.Selected=s.SelectedLanguages.Contains(language.Id);
        foreach(var item in vm.Categories)item.Selected=s.SelectedCategories.Contains(item.Id);
        try {KeyBox.Password=Secrets.Load();KeyStatus.Text=string.IsNullOrEmpty(KeyBox.Password)?"API key belum tersedia.":"API key tersedia · disimpan dengan Windows DPAPI.";}catch{KeyStatus.Text="Masukkan ulang API key; data terenkripsi sebelumnya tidak dapat dibuka.";}
    }
    private AppSettings ReadSettings()
    {
        if(!int.TryParse(CountBox.Text,out var count)||!int.TryParse(IntervalBox.Text,out var interval)||!int.TryParse(SecondsBox.Text,out var seconds))throw new ArgumentException("Jumlah soal, interval, dan timer harus bilangan bulat.");
        var s=new AppSettings {SelectedLanguages=vm.Languages.Where(l=>l.Selected).Select(l=>l.Id).ToList(),LanguageMode=ModeBox.SelectedValue is LanguageSelectionMode mode?mode:LanguageSelectionMode.BalancedQuestions,BaseUrl=BaseUrlBox.Text.Trim().TrimEnd('/'),Model=ModelBox.Text.Trim(),QuestionCount=count,IntervalMinutes=interval,ScheduleEnabled=ScheduleBox.IsChecked==true,Difficulty=DifficultyBox.SelectedItem as string??"Campuran",TimerEnabled=TimerBox.IsChecked==true,SecondsPerQuestion=seconds,SelectedCategories=vm.Categories.Where(c=>c.Selected).Select(c=>c.Id).ToList()};s.Validate();return s;
    }
    private void SaveSettings(object sender,RoutedEventArgs e)
    {
        try {if(busy)throw new InvalidOperationException("Tunggu operasi API selesai.");var s=ReadSettings();if(string.IsNullOrWhiteSpace(KeyBox.Password))throw new ArgumentException("Isi API key terlebih dahulu.");Secrets.Save(KeyBox.Password);WindowsScheduler.Save(s,storage);Message.Text="Pengaturan tersimpan. "+WindowsScheduler.Status();RefreshHome();}
        catch(Exception error){Message.Text="Belum tersimpan: "+App.SafeMessage(error);}
    }
    private void RefreshHome()
    {
        if(!IsInitialized)return;var s=storage.LoadSettings();ScheduleStatus.Text=WindowsScheduler.Status();Overview.Text=$"{string.Join(", ",s.SelectedLanguages.Select(l=>Languages.All[l]))} ({(s.LanguageMode==LanguageSelectionMode.BalancedQuestions?"Acak merata":"Acak satu bahasa per sesi")}) · {s.QuestionCount} soal per sesi · setiap {s.IntervalMinutes} menit\nModel: {s.Model} · {s.SelectedCategories.Count} tipe soal · timer {(s.TimerEnabled?"aktif":"nonaktif")}";
    }
    private void TabChanged(object sender,SelectionChangedEventArgs e){if(!IsInitialized||e.Source!=Tabs)return;if(Tabs.SelectedIndex==1)HistoryList.ItemsSource=storage.History();if(Tabs.SelectedIndex==0)RefreshHome();}
    private void OpenSettings(object sender,RoutedEventArgs e)=>Tabs.SelectedIndex=2;
    private void StartQuiz(object sender,RoutedEventArgs e)=>Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--quiz"){UseShellExecute=true});
    private void OpenCatalog(object sender,RoutedEventArgs e)=>Process.Start(new ProcessStartInfo("https://kelontongai.id/#models"){UseShellExecute=true});
    private void ImportKey(object sender,RoutedEventArgs e)
    {
        foreach(var scope in new[]{EnvironmentVariableTarget.Process,EnvironmentVariableTarget.User,EnvironmentVariableTarget.Machine}) {var key=Environment.GetEnvironmentVariable("kelontongai",scope);if(!string.IsNullOrWhiteSpace(key)){KeyBox.Password=key.Trim();Message.Text="Key diimpor. Klik Simpan pengaturan untuk menyimpannya.";return;}}
        Message.Text="Environment kelontongai tidak ditemukan pada proses, User, atau Machine.";
    }
    private async void TestConnection(object sender,RoutedEventArgs e)
    {
        if(busy)return;busy=true;Message.Text="Menguji model terpilih…";
        try{using var client=new KelontongClient();await client.Test(ReadSettings(),KeyBox.Password,cancellation.Token);Message.Text="Koneksi berhasil; model merespons.";}
        catch(Exception error){Message.Text=App.SafeMessage(error);}finally{busy=false;}
    }
    private async void RefreshModels(object sender,RoutedEventArgs e)
    {
        if(busy)return;busy=true;var selected=ModelBox.Text;Message.Text="Mengambil daftar model…";
        try{using var client=new KelontongClient();var ids=await client.Models(ReadSettings(),KeyBox.Password,cancellation.Token);var fresh=ids.Select(id=>new ModelEntry(id,ModelCatalog.Provider(id))).ToList();Storage.AtomicWrite(Path.Combine(Storage.Root,"models.json"),Json.Write(fresh));models=models.Concat(fresh).DistinctBy(m=>m.Id).OrderBy(m=>m.Provider).ThenBy(m=>m.Id).ToList();SetModels();ModelBox.Text=selected;Message.Text=$"{fresh.Count} model diterima dari API. Daftar katalog awal tetap tersedia.";}
        catch(Exception error){Message.Text=App.SafeMessage(error)+" Daftar model tersimpan tetap tersedia.";}finally{busy=false;}
    }
    private void FilterModels(object sender,TextChangedEventArgs e){if(ModelBox?.ItemsSource is ListCollectionView view)view.Refresh();}
    private void RefreshCategories()
    {
        var available=vm.Languages.Where(l=>l.Selected).SelectMany(l=>CodingFlashcards.Categories.ForLanguage(l.Id)).ToHashSet();
        var previous=vm.Categories.ToDictionary(c=>c.Id,c=>c.Selected);
        vm.Categories.Clear();
        foreach(var entry in CodingFlashcards.Categories.All.Where(c=>available.Contains(c.Key)))
            vm.Categories.Add(new CategoryChoice(entry.Key,entry.Value){Selected=previous.GetValueOrDefault(entry.Key,true)});
    }
    private void ShowHistory(object sender,SelectionChangedEventArgs e)
    {if(HistoryList.SelectedItem is PracticeSession s)HistoryDetails.Text=string.Join("\n\n",s.Attempts.Select((a,i)=>$"{i+1}. {Languages.All[a.Question.Language]} · {Categories.All[a.Question.Category]} · {a.Question.Difficulty}\n{a.Question.Prompt}\n{a.Question.Code}\n\nJawaban Anda: {(a.TimedOut ? "Waktu habis" : a.SelectedIndex is int n ? ((char)('A'+n))+". "+a.Question.Options[n] : "Belum dijawab")}\nKunci: {(char)('A'+a.Question.AnswerIndex)}. {a.Question.Options[a.Question.AnswerIndex]}\n{a.Question.Explanation}"));}
}
