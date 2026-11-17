using CodingFlashcards;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

internal static class Program
{
    private static int checks;
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
    private static void Reject(Action action,string message){try{action();}catch(Exception e)when(e is ArgumentException or FormatException){checks++;return;}throw new Exception("Accepted "+message);}
    private static Question Sample(string language="python",string category="output",string difficulty="Sedang",string code="x = 1\nprint(x)")=>new(){Language=language,Category=category,Difficulty=difficulty,Code=code,Prompt="Apa output kode ini? ("+difficulty+")",Options=["1","2","3","4"],AnswerIndex=0,Explanation="Nilai x adalah 1."};
    [STAThread] public static int Main(string[] args)
    {
        Environment.SetEnvironmentVariable("CODINGFLASHCARDS_DATA_DIR",Path.Combine(Environment.CurrentDirectory,"artifacts","checks-"+Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable("kelontongai","test-only");
        try {
            var settings=new AppSettings();settings.Validate();Check(settings.SelectedLanguages.SequenceEqual(["python"]) && settings.QuestionCount==3 && !settings.ScheduleEnabled,"defaults");
            foreach(var lang in Languages.All.Keys){var s=new AppSettings{SelectedLanguages=[lang],SelectedCategories=Categories.ForLanguage(lang)};s.Validate();foreach(var cat in s.SelectedCategories)Sample(lang,cat).Validate();Check(true,"language categories");}
            Reject(()=>new AppSettings{SelectedCategories=[]}.Validate(),"empty categories");
            Reject(()=>new AppSettings{SelectedLanguages=["sql"]}.Validate(),"incompatible categories");
            Reject(()=>new AppSettings{QuestionCount=0}.Validate(),"count");
            Reject(()=>new AppSettings{Difficulty="invalid"}.Validate(),"difficulty");
            Reject(()=>new AppSettings{BaseUrl="http://example.com"}.Validate(),"insecure URL");
            Reject(()=>Sample(code:"").Validate(),"missing code");
            var q=Sample();q.Options=["a","a","b","c"];Reject(q.Validate,"duplicate options");
            q=Sample();q.AnswerIndex=-1;Reject(q.Validate,"missing answer");
            q=Sample();q.Options=["x","X","y","Y"];q.Validate();Check(true,"case sensitive code options");
            Check(Sample(code:"X=1").Hash!=Sample(code:"x=1").Hash,"hash preserves case");
            Check(Sample(code:" x=1").Hash!=Sample(code:"x=1").Hash,"hash preserves indentation");
            Check(Sample().Hash!=Sample("java").Hash,"hash includes language");
            using var storage=new Storage();storage.SaveSettings(settings);Check(storage.LoadSettings().SelectedLanguages.SequenceEqual(["python"]),"settings roundtrip");
            storage.SaveQuestions([Sample(),Sample(),Sample("java"),Sample(difficulty:"Sulit"),Sample(category:"oop")]);
            settings.SelectedCategories=["output"];settings.QuestionCount=1;settings.Difficulty="Sedang";Check(storage.Cached(settings,SessionPlan.Create(settings,storage)).Count==1,"cache exact filters and dedup");
            settings.Difficulty="Campuran";Check(storage.Cached(settings,SessionPlan.Create(settings,storage)).Count==1,"mixed difficulty cache");
            settings.SelectedLanguages=["java"];Check(storage.Cached(settings,SessionPlan.Create(settings,storage)).Count==1,"language cache");
            storage.Advance("python",3);storage.Advance("java",1);using(var second=new Storage())Check(second.RotationIndex("python")==3&&second.RotationIndex("java")==1,"independent rotations persisted");
            var session=new PracticeSession{Total=2,Attempts=[new(Sample(),0,false),new(Sample(),null,true)]};storage.SaveSession(session);Check(storage.History()[0].Correct==1,"history scoring timeout");
            var xml=XDocument.Parse(WindowsScheduler.BuildXml(new(),@"C:\My Apps\CodingFlashcards.exe","S-1-5-21-test"));XNamespace ns="http://schemas.microsoft.com/windows/2004/02/mit/task";
            Check(xml.Descendants(ns+"Interval").Single().Value=="PT30M","schedule interval");Check(xml.Descendants(ns+"Command").Single().Value.EndsWith("CodingFlashcards.exe"),"schedule executable");
            Check(WindowsScheduler.TaskName.StartsWith("CodingFlashcards-"),"separate task");Check(Storage.Root.Contains("checks-"),"isolated data");
            PlannerChecks(storage);
            storage.SaveSettings(new());
            RenderScreens(storage);
            if(args.Length>0)Task.Run(()=>ApiChecks(args[0],storage)).GetAwaiter().GetResult();
            Console.WriteLine($"PASS: {checks} checks");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    private static List<Question> ForPlan(SessionPlan plan)
    {
        var nonce=Guid.NewGuid().ToString("N");
        return plan.Requests.Select((r,i)=> {var q=Sample(r.Language,r.Category);q.Prompt+=$" {nonce}-{i}";return q;}).ToList();
    }
    private static void PlannerChecks(Storage storage)
    {
        File.WriteAllText(Storage.SettingsPath,"{\"version\":1,\"language\":\"java\",\"selectedCategories\":[\"output\"]}");
        var old=storage.LoadSettings();Check(old.Version==2&&old.SelectedLanguages.SequenceEqual(["java"]),"legacy migration");
        File.WriteAllText(Storage.SettingsPath,"{\"language\":\"java\",\"selectedLanguages\":[],\"selectedCategories\":[\"output\"]}");
        Reject(()=>storage.LoadSettings(),"explicit empty list not migrated");
        File.Delete(Storage.SettingsPath);
        Reject(()=>new AppSettings{SelectedLanguages=[]}.Validate(),"no languages");
        Reject(()=>new AppSettings{SelectedLanguages=["python","python"]}.Validate(),"duplicate languages");
        Reject(()=>new AppSettings{SelectedLanguages=["python","sql"],SelectedCategories=["output"]}.Validate(),"SQL requires category");
        Reject(()=>new AppSettings{LanguageMode=(LanguageSelectionMode)999}.Validate(),"unknown mode");
        var s=new AppSettings{SelectedLanguages=["python","java","sql"],SelectedCategories=["output","sql"],QuestionCount=1};
        storage.SaveSettings(s);
        var sequence=new List<string>();
        for(var i=0;i<9;i++) {
            using var reopened=new Storage();
            var plan=SessionPlan.Create(s,reopened,new Random(42+i));
            sequence.Add(plan.Requests.Single().Language);reopened.CommitPlan(s,plan,ForPlan(plan));
        }
        foreach(var round in sequence.Chunk(3))Check(round.Distinct().Count()==3,"balanced rounds across restarts");
        var a=SessionPlan.Create(s,storage,new Random(7));storage.CommitPlan(s,a,ForPlan(a));
        s.LanguageMode=LanguageSelectionMode.SingleLanguagePerSession;s.QuestionCount=7;storage.SaveSettings(s);
        var one=SessionPlan.Create(s,storage,new Random(11));Check(one.Requests.Select(r=>r.Language).Distinct().Count()==1,"one language all seven questions");storage.CommitPlan(s,one,ForPlan(one));
        s.LanguageMode=LanguageSelectionMode.BalancedQuestions;s.QuestionCount=1;storage.SaveSettings(s);
        var b=SessionPlan.Create(s,storage,new Random(7));Check(a.Requests[0].Language!=b.Requests[0].Language,"balanced round resumes after mode switch");
        s.LanguageMode=LanguageSelectionMode.SingleLanguagePerSession;storage.SaveSettings(s);
        var two=SessionPlan.Create(s,storage,new Random(11));Check(one.Requests[0].Language!=two.Requests[0].Language,"session round resumes after switch");
        var rounds=new List<string>{one.Requests[0].Language};
        storage.CommitPlan(s,two,ForPlan(two));rounds.Add(two.Requests[0].Language);
        var three=SessionPlan.Create(s,storage,new Random(11));rounds.Add(three.Requests[0].Language);storage.CommitPlan(s,three,ForPlan(three));
        Check(rounds.Distinct().Count()==3,"session mode complete round");
        s.SelectedLanguages=["python","java"];s.SelectedCategories=["debugging","fundamentals"];s.QuestionCount=8;s.LanguageMode=LanguageSelectionMode.BalancedQuestions;storage.SaveSettings(s);
        var mixed=SessionPlan.Create(s,storage,new Random(2));
        foreach(var round in mixed.Requests.Chunk(2))Check(round.Select(r=>r.Language).Distinct().Count()==2,"multiple rounds in large session");
        var before=storage.RotationIndex("python");
        var planned=ForPlan(mixed);storage.SaveQuestions(planned.Take(7));
        Check(storage.Cached(s,mixed).Count==0,"incomplete plan cache unavailable");
        storage.SaveQuestions(planned);
        var cached=storage.Cached(s,mixed);Check(cached.Count==8&&cached.Select(q=>q.Hash).SequenceEqual(storage.Cached(s,mixed).Select(q=>q.Hash)),"cache stable and complete");
        using(var cancelled=new CancellationTokenSource()){
            cancelled.Cancel();try{storage.CommitPlan(s,mixed,cached,cancelled.Token);throw new Exception("expected cancel");}catch(OperationCanceledException){checks++;}
        }
        Check(storage.RotationIndex("python")==before,"cancel leaves categories unchanged");
        Check(SessionPlan.Create(s,storage,new Random(2)).Requests.SequenceEqual(mixed.Requests),"cancel leaves language round unchanged");
        storage.CommitPlan(s,mixed,cached);Check(storage.RotationIndex("python")==before+4,"cache advances category counts");
        s.QuestionCount=1;var partial=SessionPlan.Create(s,storage,new Random(2));storage.CommitPlan(s,partial,ForPlan(partial));
        var after=SessionPlan.Create(s,storage,new Random(2));
        var first=after.Requests.ToList();s.SelectedLanguages.Reverse();storage.SaveSettings(s);
        Check(SessionPlan.Create(s,storage,new Random(2)).Requests.SequenceEqual(first),"checkbox order does not reset round");
        s.SelectedLanguages=["php"];s.SelectedCategories=["output"];s.QuestionCount=7;storage.SaveSettings(s);
        foreach(var mode in Enum.GetValues<LanguageSelectionMode>()){
            s.LanguageMode=mode;var single=SessionPlan.Create(s,storage,new Random(3));Check(single.Requests.All(r=>r.Language=="php"),"one selected language both modes");
        }
        storage.SaveSettings(s);Check(storage.LoadSettings().LanguageMode==s.LanguageMode,"mode persisted");
        Check(File.ReadAllText(Storage.SettingsPath).Contains("SingleLanguagePerSession"),"mode stored as JSON string");
    }
    private static void Render(FrameworkElement content,string name)
    {
        content.Measure(new Size(960,780));content.Arrange(new Rect(0,0,960,780));content.UpdateLayout();
        foreach(var dpi in new[]{96,120,144}){
            var bitmap=new RenderTargetBitmap(960*dpi/96,780*dpi/96,dpi,dpi,PixelFormats.Pbgra32);bitmap.Render(content);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory("artifacts/screens");
            using var file=File.Create($"artifacts/screens/{name}-{dpi}.png");encoder.Save(file);checks++;
        }
    }
    private static void RenderScreens(Storage storage)
    {
        var app=new App();app.InitializeComponent();var window=new MainWindow();var tabs=(TabControl)window.FindName("Tabs");
        ((Grid)window.Content).Background=window.Background;
        foreach(var tab in new[]{0,2,1}){tabs.SelectedIndex=tab;if(tab==1)((ListBox)window.FindName("HistoryList")).SelectedIndex=0;Render((FrameworkElement)window.Content,new[]{"home","history","settings"}[tab]);}
        tabs.SelectedIndex=2;var scroll=(ScrollViewer)window.FindName("SettingsScroll");scroll.ScrollToEnd();window.UpdateLayout();Render((FrameworkElement)window.Content,"settings-languages");
        var vm=(SettingsViewModel)window.DataContext;
        foreach(var l in vm.Languages)l.Selected=l.Id=="sql";
        Check(vm.Categories.Single().Id=="sql","SQL category UI");
        vm.Languages.Single(l=>l.Id=="java").Selected=true;
        Check(vm.Categories.Count==8,"combined categories UI");
        vm.Categories.Single(c=>c.Id=="oop").Selected=false;
        vm.Languages.Single(l=>l.Id=="python").Selected=true;
        Check(!vm.Categories.Single(c=>c.Id=="oop").Selected,"category selection preserved");
        ((ComboBox)window.FindName("ModeBox")).SelectedValue=LanguageSelectionMode.SingleLanguagePerSession;
        Render((FrameworkElement)window.Content,"settings-mixed");
        var quiz=new QuizWindow();
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        typeof(QuizWindow).GetField("questions",flags)!.SetValue(quiz,new List<Question>{Sample(code:"def calculate_total(values):\n    total = 0\n    for value in values:\n        total += value\n    return total\n\nprint(calculate_total([1, 2, 3]))\n# "+new string('x',160))});
        typeof(QuizWindow).GetField("session",flags)!.SetValue(quiz,new PracticeSession{Total=1});
        typeof(QuizWindow).GetMethod("ShowQuestion",flags)!.Invoke(quiz,null);
        ((Grid)quiz.Content).Background=quiz.Background;Render((FrameworkElement)quiz.Content,"quiz");
        Check(((TextBox)quiz.FindName("CodeText")).Text.Contains("    total"),"code indentation UI");
        ((RadioButton)((StackPanel)quiz.FindName("OptionsPanel")).Children[0]).IsChecked=true;
        typeof(QuizWindow).GetMethod("Submit",flags)!.Invoke(quiz,[false]);
        Check(((TextBlock)quiz.FindName("FeedbackText")).Text.Contains("Benar!"),"answer feedback");
        typeof(QuizWindow).GetMethod("Next",flags)!.Invoke(quiz,[null!,new RoutedEventArgs()]);
        Check(((TextBlock)quiz.FindName("ProgressText")).Text=="SESI SELESAI","quiz completion");
        quiz.Close();window.Close();
    }
    private static async Task ApiChecks(string origin,Storage storage)
    {
        var settings=new AppSettings{BaseUrl=origin+"/kelontong/v1",SelectedCategories=["output","debugging","complexity"]};
        using var client=new KelontongClient(TimeSpan.FromSeconds(10));
        Check((await client.Models(settings,"test-only",default)).Contains(settings.Model),"HTTP models");await client.Test(settings,"test-only",default);checks++;
        foreach(var lang in Languages.All.Keys){settings.SelectedLanguages=[lang];settings.SelectedCategories=Categories.ForLanguage(lang);settings.QuestionCount=Math.Min(7,settings.SelectedCategories.Count);var qs=await client.Generate(settings,"test-only",storage,null,default);Check(qs.Count==settings.QuestionCount&&qs.All(q=>q.Language==lang),"HTTP language generation");}
        settings.SelectedLanguages=["python","java","sql"];settings.SelectedCategories=["output","sql"];settings.QuestionCount=13;
        foreach(var mode in Enum.GetValues<LanguageSelectionMode>()){
            settings.LanguageMode=mode;storage.SaveSettings(settings);var plan=SessionPlan.Create(settings,storage,new Random(123));
            var qs=await client.Generate(settings,"test-only",storage,null,default,plan);
            Check(qs.Select(q=>new QuestionRequest(q.Language,q.Category)).SequenceEqual(plan.Requests),"HTTP exact planned pairs across batches");
            Check(mode==LanguageSelectionMode.SingleLanguagePerSession?qs.Select(q=>q.Language).Distinct().Count()==1:qs.Select(q=>q.Language).Distinct().Count()==3,"HTTP both modes");
        }
        settings.LanguageMode=LanguageSelectionMode.BalancedQuestions;
        settings.SelectedLanguages=["python"];settings.SelectedCategories=["output"];settings.QuestionCount=2;settings.Difficulty="Sedang";
        foreach(var scenario in new[]{"malformed","wrong-language","wrong-category","wrong-difficulty","missing-code","bad-options","missing-answer","null-options","wrong-count","duplicate"}){
            settings.Model=scenario;var before=storage.RotationIndex("python");try{await client.Generate(settings,"test-only",storage,null,default);throw new Exception("expected rejection "+scenario);}catch(FormatException){checks++;}Check(storage.RotationIndex("python")==before,"failed generation preserves rotation");
        }
        settings.Model="slow";var cancellationPlan=SessionPlan.Create(settings,storage,new Random(77));var previous=storage.RotationIndex("python");
        using(var cts=new CancellationTokenSource(100)){
            try{await client.Generate(settings,"test-only",storage,null,cts.Token,cancellationPlan);throw new Exception("expected cancellation");}catch(OperationCanceledException){checks++;}
        }
        Check(storage.RotationIndex("python")==previous,"HTTP cancellation does not advance rotation");
        Check((await client.Generate(settings,"test-only",storage,null,default,cancellationPlan)).Count==2,"same plan reusable after HTTP cancellation");
        settings.Model="fenced";Check((await client.Generate(settings,"test-only",storage,null,default)).Count==2,"fenced JSON");
        foreach(var pair in new Dictionary<string,int>{{"unauthorized",401},{"missing",404},{"rate",429},{"server",500}}){settings.Model=pair.Key;try{await client.Test(settings,"test-only",default);throw new Exception("expected HTTP error");}catch(ApiException e){Check(e.StatusCode==pair.Value,"HTTP error classification");}}
        settings.Model="slow";using var shortClient=new KelontongClient(TimeSpan.FromMilliseconds(250));try{await shortClient.Test(settings,"test-only",default);throw new Exception("expected timeout");}catch(TimeoutException){checks++;}
    }
}
