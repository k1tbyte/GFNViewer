using Newtonsoft.Json;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace GFNViewer;

public sealed class Config
{
    public string TelegramToken { get; set; }     = null!;
    public string AdminUsername { get; set; } = null!;
    public HashSet<string> AvailableViewers { get; set; } = new();
}

internal static class Program
{
    internal static Config Config { get; private set; } = null!;
    internal static TelegramBotClient Client { get; private set; } = null!;

    private static GFNWrapper? Wrapper;
    private static readonly Dictionary<string, (long ChatId, int QueueMessageId)> Viewers = new();

    private static void Init()
    {
        bool rewrite = false;

        if (System.IO.File.Exists("appconfig.json"))
        {
            Config = JsonConvert.DeserializeObject<Config>(System.IO.File.ReadAllText("appconfig.json"))!;
        }

        Config ??= new();

        if (string.IsNullOrEmpty(Config.TelegramToken))
        {
            Console.Write("Enter telegram bot token: ");
            Config.TelegramToken = Console.ReadLine();
            rewrite = true;
        }

        if(string.IsNullOrEmpty(Config.AdminUsername))
        {
            Console.Write("Enter admin username (without @): ");
            Config.AdminUsername = Console.ReadLine();
            rewrite = true;
        }

        if(rewrite)
        {
            System.IO.File.WriteAllText("appconfig.json", JsonConvert.SerializeObject(Config));
        }

    }

    private static void Main(string[] args)
    {

        var process = System.Diagnostics.Process.GetProcessesByName("GeForceNOW").FirstOrDefault(o => o.MainWindowHandle != IntPtr.Zero);

        Init();
        Client = new TelegramBotClient(Config.TelegramToken!);
        Client.StartReceiving(TelegramUpdateHandler, TelegramErrorHandler);
        Console.ReadLine();
    }

    private static async Task TelegramUpdateHandler(ITelegramBotClient sender, Update e, CancellationToken cancellationToken)
    {
        if (e.Message.From.Username != Config.AdminUsername && !Config.AvailableViewers.Contains(e.Message.From.Username.ToLower()))
            return;

        if(e.Message.Text == "/track" && e.Message.From.Username != Config.AdminUsername)
        {
            if(Wrapper == null)
            {
                await Client.SendTextMessageAsync(e.Message.Chat.Id, "❌ Tracking has not been enabled by an administrator");
                return;
            }

            if(!Viewers.TryAdd(e.Message.From.Username,(e.Message.Chat.Id,e.Message.MessageId + 2 )))
            {
                await Client.SendTextMessageAsync(e.Message.Chat.Id, "❗️ Queue notification is already enabled");
                return;
            }

            await Client.SendTextMessageAsync(e.Message.Chat.Id, "✅ Queue notification enabled");
            var queueMessage = await Client.SendTextMessageAsync(e.Message.Chat.Id, "🌀 Waiting for queue information");
            await Client.PinChatMessageAsync(e.Message.Chat.Id, queueMessage.MessageId);
            return;
        }

        if (e.Message.From.Username != Config.AdminUsername)
            return;

        if (e.Message.Text == "/start")
        {
            if (Wrapper != null)
            {
                await Client.SendTextMessageAsync(e.Message.Chat.Id, "❗️ Queue tracking is already underway");
                return;
            }

            try
            {
                Wrapper = new(QueueCallback);
                await Client.SendTextMessageAsync(e.Message.Chat.Id, "✅ Queue tracking started");
                await Client.SendTextMessageAsync(e.Message.Chat.Id, "🌀 Waiting for queue information");
                Viewers.Add(e.Message.From.Username, (e.Message.Chat.Id, e.Message.MessageId + 2));
            }
            catch
            {
                await Client.SendTextMessageAsync(e.Message.Chat.Id, "❌ Unable to set tracking");
            }
            return;
        }

        if(e.Message.Text == "/stop")
        {
            if(Wrapper == null)
            {
                await Client.SendTextMessageAsync(e.Message.Chat.Id, "❗️ Queue tracking has not yet been started");
                return;
            }

            Wrapper.Dispose();
            Wrapper = null;
            await Client.SendTextMessageAsync(e.Message.Chat.Id, "✅ Queue tracking stopped");
            await Client.DeleteMessageAsync(e.Message.Chat.Id, Viewers[Config.AdminUsername].QueueMessageId);

            foreach (var item in Viewers)
            {
                if(item.Key != Config.AdminUsername)
                {
                    await Client.SendTextMessageAsync(item.Value.ChatId, "❗️ The administrator has disabled queue tracking");
                }
            }

            Viewers.Clear();
        }
    }

    private static async void QueueCallback(string? value, QueueState state)
    {
        var msgText = 
            state == QueueState.Passed  ? "🔸 The queue has passed, entering the game" :
            state == QueueState.Stopped ? "🔻 Queue waiting stopped" : 
            state == QueueState.Failed  ? "❌ Internal streaming error" : $"🔹 Queue position: {value}";


        foreach (var item in Viewers)
        {
            if (state == QueueState.Processing)
            {
                await Client.EditMessageTextAsync(item.Value.ChatId, item.Value.QueueMessageId, msgText);
                continue;
            }

            await Client.SendTextMessageAsync(item.Value.ChatId, msgText);
        }

        if (state != QueueState.Processing)
        {
            Viewers.Clear();
            Wrapper.Dispose();
            Wrapper = null;
        }

        return;

    }

    private async static Task TelegramErrorHandler(ITelegramBotClient sender, Exception e, CancellationToken cancellationToken)
    {
        Console.WriteLine($"\n{DateTime.Now} | ERROR | " + e);
        await Client.SendTextMessageAsync(Viewers[Config.AdminUsername].ChatId,"❌ An error has occurred");
    }
}
