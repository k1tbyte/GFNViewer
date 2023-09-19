using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GFNViewer;
internal enum QueueState
{
    Passed,
    Processing,
    Stopped,
    Failed
}

internal sealed partial class GFNWrapper : IDisposable
{
    private readonly string LogPath;
    private readonly System.Timers.Timer Timer;
    private readonly Action<string?, QueueState> QueueCallback; 
    private string? LastQueue = "";
    private long LastChangeIndex;

    public void Dispose()
    {
        Timer.Dispose();
    }

    public GFNWrapper(Action<string?, QueueState> callback)
    {
        LogPath       = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\NVIDIA Corporation\\GeForceNOW\\debug.log";
        QueueCallback = callback;

        if (!File.Exists(LogPath))
            throw new InvalidOperationException("GeForce NOW not found");

        using var file = File.Open(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        LastChangeIndex = file.Length;

        Timer = new(TimeSpan.FromSeconds(10));
        Timer.Elapsed += DebugLogCheckEvent;
        Timer.Start();
    }

    private void DebugLogCheckEvent(object? sender, System.Timers.ElapsedEventArgs e)
    {
        using var file = File.Open(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        if (file.Length == LastChangeIndex)
            return;

        if(file.Length < LastChangeIndex)
        {
            LastChangeIndex = file.Length;
            return;
        }

        file.Position   = LastChangeIndex;
        LastChangeIndex = file.Length;

        using var reader = new StreamReader(file);
        var text = reader.ReadToEnd();

        var state = QueueState.Processing;
        string? result = null;

        if (text.Contains("onStopResult"))
        {
            state = QueueState.Stopped;
        }
        else if (text.Contains("IPC_STREAMING_SESSION_SETUP_EVENT"))
        {
            state = QueueState.Passed;
        }
        else if(text.Contains("IPC_STREAMING_FAILURE_EVENT"))
        {
            state = QueueState.Failed;
        }
        else if((result = QueueRegex().Matches(text).LastOrDefault()?.Groups[4]?.Value) == null || LastQueue == result)
        {
            return;
        }

        LastQueue = result;
        QueueCallback?.Invoke(result, state);
    }

    [GeneratedRegex("\\[(.+)/[ ]*(.+):INFO:.+\\].+\\(state: (.*), queue: (.*), eta: (.*)\\)")]
    private static partial Regex QueueRegex();
}
