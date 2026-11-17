using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace CodingFlashcards;
public partial class QuizWindow : Window
{
    private readonly Storage storage=new();
    private AppSettings settings;
    private readonly CancellationTokenSource cancel=new();
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(200)};
    private List<Question> questions=[];
    private PracticeSession? session;
    private SessionPlan? plan;
    private int index;
    private int? selected;
    private bool answered,loading,closed,locked,suspended;
    private double remaining;
    private long lastTick;
    public QuizWindow()
    {
        InitializeComponent();settings=storage.LoadSettings();
        timer.Tick+=Tick;
        SystemEvents.SessionSwitch+=SessionChanged;SystemEvents.PowerModeChanged+=PowerChanged;
        Loaded+=async(_,_)=>await LoadQuestions();
    }
    private async Task LoadQuestions()
    {
        if(loading)return;loading=true;RetryButton.Visibility=CacheButton.Visibility=Visibility.Collapsed;
        AnswerButton.IsEnabled=false;PromptText.Text="Menyiapkan latihan…";StatusText.Text="Menghubungi KelontongAI. Anda dapat menutup jendela ini kapan saja.";
        try {
            settings=storage.LoadSettings();settings.Validate();
            if(plan is null || plan.Committed || !plan.Matches(settings)) plan=SessionPlan.Create(settings,storage);
            using var client=new KelontongClient();
            questions=await client.Generate(settings,Secrets.Load(),storage,new Progress<string>(message=>{if(!closed)ProgressText.Text=message;}),cancel.Token,plan);
            if(!closed)Begin(false);
        } catch(OperationCanceledException) when(cancel.IsCancellationRequested) {}
        catch(Exception error) {if(!closed){PromptText.Text="Latihan belum dapat dimuat";StatusText.Text=App.SafeMessage(error);RetryButton.Visibility=Visibility.Visible;CacheButton.Visibility=plan is not null && storage.Cached(settings,plan).Count>=settings.QuestionCount?Visibility.Visible:Visibility.Collapsed;}}
        finally{loading=false;}
    }
    private void Begin(bool cached)
    {
        session=new PracticeSession {Model=settings.Model,Total=questions.Count,FromCache=cached};index=0;storage.SaveSession(session);ShowQuestion();
    }
    private void ShowQuestion()
    {
        var q=questions[index];answered=false;selected=null;FeedbackBorder.Visibility=NextButton.Visibility=Visibility.Collapsed;AnswerButton.Visibility=Visibility.Visible;AnswerButton.IsEnabled=false;CodeText.Text="";CodeText.Visibility=Visibility.Collapsed;OptionsPanel.Children.Clear();
        ProgressText.Text=$"SOAL {index+1} / {questions.Count}   ·   {Languages.All[q.Language]}   ·   {Categories.All[q.Category]}   ·   {q.Difficulty}";PromptText.Text=q.Prompt;
        StatusText.Text=session!.FromCache?"Ulangan soal tersimpan · tidak menggunakan API":"Model: "+q.Model;
        if(!string.IsNullOrWhiteSpace(q.Code)){CodeText.Text=q.Code;CodeText.Visibility=Visibility.Visible;}
        for(var i=0;i<4;i++)
        {
            var option=i;var row=new StackPanel();row.Children.Add(new TextBlock {Text=((char)('A'+i))+ ".  "+q.Options[i],TextWrapping=TextWrapping.Wrap,FontSize=16,FontFamily=new FontFamily("Consolas"),Margin=new Thickness(0,4,0,4)});
            var radio=new RadioButton {Content=row,GroupName="answers",Padding=new Thickness(10),Margin=new Thickness(0,4,0,4),HorizontalContentAlignment=HorizontalAlignment.Stretch};
            radio.Checked+=(_,_)=>{selected=option;AnswerButton.IsEnabled=!answered;};OptionsPanel.Children.Add(radio);
        }
        remaining=settings.SecondsPerQuestion;lastTick=Environment.TickCount64;
        if(settings.TimerEnabled){TimerText.Text=$"{remaining:0} detik";timer.Start();}else TimerText.Text="Tanpa batas waktu";
    }
    private void Tick(object? sender,EventArgs e)
    {
        var now=Environment.TickCount64;var elapsed=(now-lastTick)/1000.0;lastTick=now;
        if(locked||suspended||!WindowsScheduler.DesktopAvailable())return;
        // A delayed dispatcher callback after sleep must not consume the sleeping time.
        if(elapsed<3)remaining-=elapsed;
        TimerText.Text=$"{Math.Ceiling(Math.Max(0,remaining)):0} detik";
        if(remaining<=0)Submit(true);
    }
    private void SessionChanged(object sender,SessionSwitchEventArgs e)=>Dispatcher.InvokeAsync(()=>{locked=e.Reason==SessionSwitchReason.SessionLock?true:e.Reason==SessionSwitchReason.SessionUnlock?false:locked;lastTick=Environment.TickCount64;});
    private void PowerChanged(object sender,PowerModeChangedEventArgs e)=>Dispatcher.InvokeAsync(()=>{if(e.Mode==PowerModes.Suspend)suspended=true;if(e.Mode==PowerModes.Resume)suspended=false;lastTick=Environment.TickCount64;});
    private void Answer(object sender,RoutedEventArgs e){if(selected.HasValue)Submit(false);}
    private void Submit(bool timeout)
    {
        if(answered||session is null)return;answered=true;timer.Stop();var q=questions[index];
        session.Attempts.Add(new Attempt(q,timeout?null:selected,timeout));if(session.Attempts.Count==session.Total)session.Status="Selesai";storage.SaveSession(session);
        foreach(UIElement option in OptionsPanel.Children)option.IsEnabled=false;
        var correct=!timeout&&selected==q.AnswerIndex;
        FeedbackText.Text=(timeout?"Waktu habis.":correct?"Benar!":"Belum tepat.")+$" Kunci: {(char)('A'+q.AnswerIndex)}\n\n"+q.Explanation;
        FeedbackText.Foreground=correct?Brushes.DarkGreen:Brushes.DarkSlateBlue;FeedbackBorder.Visibility=Visibility.Visible;AnswerButton.Visibility=Visibility.Collapsed;NextButton.Visibility=Visibility.Visible;
        NextButton.Content=index==questions.Count-1?"Lihat hasil":"Berikutnya →";
    }
    private void Next(object sender,RoutedEventArgs e)
    {
        if(!answered)return;if(++index<questions.Count){ShowQuestion();return;}
        session!.Status="Selesai";storage.SaveSession(session);OptionsPanel.Children.Clear();CodeText.Text="";CodeText.Visibility=Visibility.Collapsed;FeedbackBorder.Visibility=NextButton.Visibility=Visibility.Collapsed;
        ProgressText.Text="SESI SELESAI";PromptText.Text=$"{session.Correct} dari {session.Total} jawaban benar";StatusText.Text="Hasil dan pembahasan tersimpan di menu Riwayat. Sampai latihan berikutnya!";
    }
    private async void Retry(object sender,RoutedEventArgs e)=>await LoadQuestions();
    private void UseCache(object sender,RoutedEventArgs e)
    {
        if(plan is null)return;
        questions=storage.Cached(settings,plan);if(questions.Count<settings.QuestionCount){StatusText.Text="Cache yang sesuai belum cukup.";return;}storage.CommitPlan(settings,plan,questions);RetryButton.Visibility=CacheButton.Visibility=Visibility.Collapsed;Begin(true);
    }
    private void OpenSettings(object sender,RoutedEventArgs e)=>Process.Start(new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true});
    private void CloseQuiz(object sender,RoutedEventArgs e)=>Close();
    private void ClosingQuiz(object? sender,CancelEventArgs e)
    {
        closed=true;cancel.Cancel();timer.Stop();SystemEvents.SessionSwitch-=SessionChanged;SystemEvents.PowerModeChanged-=PowerChanged;
        if(session is not null)storage.SaveSession(session);storage.Dispose();
    }
}
