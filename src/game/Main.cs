using System;
using System.Runtime.InteropServices;

internal static class AotEntry
{
    [DllImport("libc", SetLastError = true)]
    private static extern int execve(string path, string[] argv, string[] envp);

    /// <summary>The GC's gen0 budget can only be set through DOTNET_GCgen0size (no runtimeconfig name), and the default
    /// on this ARM is far too big for a 256 MB console (title RSS 137 MB vs 109 MB with 2 MB). Set the GC knobs and
    /// re-exec once, so no launch script is needed; values already in the environment win.</summary>
    private static void EnsureGcSettings(string[] args)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.Arm || Environment.GetEnvironmentVariable("SDV_GC_SET") == "1") return;
        // .NET's SetEnvironmentVariable doesn't touch the native environ on Unix: build envp for execve explicitly
        var env = new System.Collections.Generic.Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables()) env[(string)e.Key] = (string)e.Value;
        env["SDV_GC_SET"] = "1";
        env.TryAdd("DOTNET_GCgen0size", "0x200000");
        env.TryAdd("DOTNET_GCConserveMemory", "7");
        var envp = new string[env.Count + 1];
        int i = 0;
        foreach (var kv in env) envp[i++] = kv.Key + "=" + kv.Value;
        string self = Environment.ProcessPath ?? "/proc/self/exe";
        var argv = new string[args.Length + 2];
        argv[0] = self; Array.Copy(args, 0, argv, 1, args.Length);
        execve(self, argv, envp);   // only returns on failure: carry on with the defaults
    }

    [STAThread]
    private static void Main(string[] args)
    {
        EnsureGcSettings(args);
        // map tilesheets: lazy only while a save is loading (every location loads its map then)
        xTile.Display.XnaDisplayDevice.DeferTileSheetLoads = () => StardewValley.SaveGame.IsProcessing;
        int n = SdvAot.ContentReaders.Register();
#if !SDV_RELEASE
        // test builds only: the probe (SDV_SAVE/SDV_AUTOGO/SDV_WARP/...) and first-chance exception log
        Console.Error.WriteLine($"[aot] {n} content readers registered, gen0size={Environment.GetEnvironmentVariable("DOTNET_GCgen0size")}");
        if (Environment.GetEnvironmentVariable("SDV_SAVE") != null) StartupHook.Initialize();
        if (Environment.GetEnvironmentVariable("SDV_FIRSTCHANCE") != null)
        {
            int logged = 0;
            AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
            {
                if (e.Exception is System.Reflection.TargetInvocationException || e.Exception is System.Reflection.AmbiguousMatchException || logged > 40) return;
                logged++;
                System.IO.File.AppendAllText("/out/firstchance.log", $"--- {e.Exception.GetType().FullName}: {e.Exception.Message}\n{new System.Diagnostics.StackTrace(1, false)}\n");
            };
        }
#endif
        StardewValley.Program.Main(args);
    }
}
