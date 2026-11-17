using System.Windows;

namespace CodingFlashcards;
public partial class App : Application
{
    private Mutex? quizMutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException+=(_,args)=> {MessageBox.Show("Operasi tidak dapat diselesaikan. "+SafeMessage(args.Exception),"Coding Flashcards");args.Handled=true;};
        try {
            if(e.Args.Contains("--scheduled") || e.Args.Contains("--quiz")) {
                if(e.Args.Contains("--scheduled")) {using var storage=new Storage();if(!storage.LoadSettings().ScheduleEnabled || !WindowsScheduler.DesktopAvailable()){Shutdown();return;}}
                quizMutex=new Mutex(true,@"Local\CodingFlashcards-Quiz-"+System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value,out var created);
                if(!created){if(!e.Args.Contains("--scheduled"))MessageBox.Show("Masih ada sesi kuis yang terbuka.","Coding Flashcards");Shutdown();return;}
                new QuizWindow().Show();
            } else new MainWindow().Show();
        } catch(Exception error) {MessageBox.Show(SafeMessage(error),"Coding Flashcards");Shutdown(1);}
    }
    public static string SafeMessage(Exception error)=>error is System.Net.Http.HttpRequestException ? "Koneksi internet gagal." : error is System.Security.Cryptography.CryptographicException ? "API key tersimpan tidak dapat dibuka. Masukkan ulang pada Settings." : error.Message;
    protected override void OnExit(ExitEventArgs e){quizMutex?.Dispose();base.OnExit(e);}
}
