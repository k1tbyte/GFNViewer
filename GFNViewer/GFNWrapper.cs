using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GFNViewer;
internal sealed partial class GFNWrapper : IDisposable
{
    private readonly string LogPath;
    private readonly System.Timers.Timer Timer;
    private const int LastLines = 20;
    private readonly Action<string?> QueueCallback; 
    private string? LastQueue = "";

    public void Dispose()
    {
        Timer.Dispose();
    }

    public GFNWrapper(Action<string?> callback)
    {
        LogPath       = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\NVIDIA Corporation\\GeForceNOW\\debug.log";
        QueueCallback = callback;

        if (!File.Exists(LogPath))
            throw new InvalidOperationException("GeForce NOW not found");

        Timer = new(TimeSpan.FromSeconds(10));
        Timer.Elapsed += DebugLogCheckEvent;
        Timer.Start();
    }

    private void DebugLogCheckEvent(object? sender, System.Timers.ElapsedEventArgs e)
    {
        using var file = File.Open(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        file.Position = file.Length -2;

        int lines = 0;
        var builder = new StringBuilder();
        while(file.Position > 0 && lines != LastLines)
        {
            var chunk =  file.ReadByte();
            builder.Insert(0,(char)chunk);
            if(chunk == '\n')
            {
                lines++;

                if (lines == LastLines)
                    break;
            }
            file.Position -= 2;
        }
        var match = QueueRegex().Matches(builder.ToString()).LastOrDefault();

        var val = match?.Groups[4]?.Value;

        if (val == LastQueue)
            return;

        QueueCallback?.Invoke(LastQueue = val);
    }

    [GeneratedRegex("\\[(.+)/[ ]*(.+):INFO:.+\\].+\\(state: (.*), queue: (.*), eta: (.*)\\)")]
    private static partial Regex QueueRegex();
}
