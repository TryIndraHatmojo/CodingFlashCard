using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;

namespace CodingFlashcards;

public static class WindowsScheduler
{
    public static string TaskName => "CodingFlashcards-" + WindowsIdentity.GetCurrent().User!.Value;
    private static dynamic Connect() {dynamic service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;service.Connect();return service;}
    public static string BuildXml(AppSettings settings,string exe,string sid)
    {
        settings.Validate();
        return $"""
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo><Description>Latihan Coding Flashcards untuk pengguna saat ini.</Description></RegistrationInfo>
          <Triggers><TimeTrigger><Repetition><Interval>PT{settings.IntervalMinutes}M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition><StartBoundary>{DateTime.Now.AddMinutes(settings.IntervalMinutes):yyyy-MM-ddTHH:mm:ss}</StartBoundary><Enabled>true</Enabled></TimeTrigger></Triggers>
          <Principals><Principal id="Author"><UserId>{SecurityElement.Escape(sid)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
          <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><StartWhenAvailable>false</StartWhenAvailable><RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable><Enabled>true</Enabled><WakeToRun>false</WakeToRun><ExecutionTimeLimit>PT0S</ExecutionTimeLimit></Settings>
          <Actions Context="Author"><Exec><Command>{SecurityElement.Escape(exe)}</Command><Arguments>--scheduled</Arguments><WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(exe))}</WorkingDirectory></Exec></Actions>
        </Task>
        """;
    }
    public static void Apply(AppSettings settings)
    {
        dynamic service=Connect();
        try {
            dynamic folder=service.GetFolder("\\");
            if(!settings.ScheduleEnabled) {try { folder.GetTask(TaskName).Enabled=false; } catch(COMException e) when((uint)e.HResult==0x80070002) {} return;}
            var exe=Environment.ProcessPath??throw new IOException("Lokasi aplikasi tidak ditemukan.");
            if(Path.GetFileNameWithoutExtension(exe).Equals("dotnet",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Aktifkan jadwal dari CodingFlashcards.exe hasil publish, bukan dotnet run.");
            folder.RegisterTask(TaskName,BuildXml(settings,exe,WindowsIdentity.GetCurrent().User!.Value),6,null,null,3,null);
        } finally {Marshal.FinalReleaseComObject(service);}
    }
    public static void Save(AppSettings settings,Storage storage)
    {
        settings.Validate(); var previous=storage.LoadSettings();
        // Do not reset the repetition anchor when unrelated settings change.
        var changed=previous.ScheduleEnabled!=settings.ScheduleEnabled || previous.IntervalMinutes!=settings.IntervalMinutes || settings.ScheduleEnabled && !Exists();
        if(changed) Apply(settings);
        try {storage.SaveSettings(settings);} catch {if(changed) Apply(previous);throw;}
    }
    private static bool Exists() {try {dynamic s=Connect();try {s.GetFolder("\\").GetTask(TaskName);return true;}finally{Marshal.FinalReleaseComObject(s);}}catch(COMException){return false;}}
    public static string Status()
    {
        try {dynamic service=Connect();try {dynamic task=service.GetFolder("\\").GetTask(TaskName);return task.Enabled ? $"Jadwal aktif · berikutnya {((DateTime)task.NextRunTime):dd MMM HH:mm}" : "Jadwal nonaktif";}finally{Marshal.FinalReleaseComObject(service);}}
        catch(COMException e) when((uint)e.HResult==0x80070002) {return "Jadwal belum diaktifkan";}
        catch {return "Status Task Scheduler tidak dapat dibaca";}
    }
    public static bool DesktopAvailable()
    {
        var handle=OpenInputDesktop(0,false,0x100);
        if(handle==IntPtr.Zero)return false;CloseDesktop(handle);return true;
    }
    [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
}
