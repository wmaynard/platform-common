using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Interop;

public class SlackDiagnostics
{
    private const int COOLDOWN_MS = 3_600_000; // 1 hour
    private const string FILE_DIRECTORY = "uploads";
    private static SlackMessageClient Client;

    public string ID { get; init; }
    public string Title { get; init; }
    public string Message { get; init; }

    private string UploadDirectory { get; set; }
    private List<string> Attachments { get; set; }
    private List<string> Messages { get; set; }
    private List<string> AdditionalChannels { get; set; }
    private Dictionary<Owner, SlackUser> UsersToTag { get; init; }
    private static Dictionary<string, Cache> CachedLogs { get; set; }
    private string[] AllChannels => Client.Channels.ToArray();

    private bool Sent { get; set; }

    private SlackDiagnostics(string title, string message = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            Utilities.Log.Error(Owner.Default, "SlackDiagnostics requires a non-null, non-empty parameter 'title'.");
            throw new Exception("SlackDiagnostics requires a non-null, non-empty parameter 'title'.");
        }

        if (string.IsNullOrWhiteSpace(PlatformEnvironment.SlackLogBotToken))
            Utilities.Log.Warn(Owner.Default, "Slack bot token not found; Slack diagnostics will be unusable.");

        if (string.IsNullOrWhiteSpace(PlatformEnvironment.SlackLogChannel))
            Utilities.Log.Warn(Owner.Default, "No default Slack log channel found; Slack utilities may not instantiate correctly.");

        Client ??= new SlackMessageClient(
            channel: PlatformEnvironment.SlackLogChannel,
            token: PlatformEnvironment.SlackLogBotToken
        );

        ID = Guid.NewGuid().ToString();
        Title = title;
        Message = message;
        UsersToTag = new Dictionary<Owner, SlackUser>();
        CachedLogs ??= new Dictionary<string, Cache>() { { Title, new Cache() }};
        Messages = new List<string>();
        Attachments = new List<string>();
        AdditionalChannels = new List<string>();
    }

    private bool CanSend => !CachedLogs.ContainsKey(Title) 
        || (CachedLogs[Title].LastTimestampSent == 0 
        || CachedLogs[Title].LastTimestampSent < Timestamp.UnixTimeUTCMS - COOLDOWN_MS);

    #pragma warning disable CS4014
    /// <summary>
    /// Sends the log off to Slack.  Awaitable.  Calling this ends the chain and prevents further sending.
    /// </summary>
    /// <returns>An awaitable Task.</returns>
    public async Task Send() // TODO: Warn if the message is never sent; or find a way to elegantly send automatically.
    {
        CheckSentStatus();
        if (!CachedLogs.ContainsKey(Title))
            CachedLogs[Title] = new Cache() 
            { 
                Count = 0, 
                LastTimestampSent = 0 
            };
        Cache info = CachedLogs[Title];
        if (!CanSend)
        {
            info.Count++;
            CachedLogs[Title] = info;
            Utilities.Log.Verbose(Owner.Will, $"It's too recent for the SlackDiagnostics to send another log for the given message. ({Title})");
            return;
        }

        List<SlackBlock> content = new List<SlackBlock>
        {
            SlackBlock.Header(Title),
            SlackBlock.Divider()
        };
        DateTime now = DateTime.UtcNow;
        content.Add($"*Service:* {PlatformEnvironment.ServiceName}");
        content.Add($"*Environment:* {PlatformEnvironment.Deployment}");
        if (now.Hour >= 16 && now.Hour <= 23 && UsersToTag.Any())
            content.Add("*Owners:* " + string.Join(", ", UsersToTag.Values.Select(user => user.Tag)));
        else if (UsersToTag.Any())
        {
            content.Add("*Owners:* " 
                + string.Join(", ", UsersToTag.Values.Select(user => $"`{user.DisplayName ?? user.FirstName ?? user.Name}`"))
                + " (_No one has been tagged because it's late._)"
            );
            Utilities.Log.Info(Owner.Default, "It's too late or too early to tag Slack users.");
        }

        foreach (string channel in AdditionalChannels)
            Client.Channels.Add(channel);
        content.Add(SlackBlock.Divider());

        content.Add(Message);
        foreach (string message in Messages)
            content.Add($"_{message}_");

        if (info.Count > 0)
            content.Add($"This error has triggered *{info.Count} times* since it was last sent.");

        if (Attachments.Any())
            content.Add("*Attachments:*");
        try
        {
            await Client.Send(new SlackMessage(content));
            foreach (string path in Attachments)
                await Client.TryUpload(path);
        }
        catch (Exception e)
        {
            Utilities.Log.Error(Owner.Default, "An error occurred sending a SlackDiagnostics log.", exception: e);
        }


        // Clean everything up
        try
        {
            if (!string.IsNullOrWhiteSpace(UploadDirectory))
                Directory.Delete(UploadDirectory, true);
        }
        catch (Exception e)
        {
            Utilities.Log.Error(Owner.Default, "Unable to delete uploaded files.", exception: e);
        }

        info.Count = 0;
        info.LastTimestampSent = Timestamp.UnixTimeUTCMS;
        CachedLogs[Title] = info;
        Sent = true;
    }
    #pragma warning restore CS4014

    /// <summary>
    /// Creates an attachment in a temporary directory.  Attachments are sent as a separate upload after the message
    /// is sent, then the directory is deleted.  Attachment content must be plaintext only.
    /// </summary>
    /// <param name="name">The filename for the attachment.</param>
    /// <param name="content">The text content of the attachment.</param>
    /// <returns>The SlackDiagnostics object for chaining.</returns>
    public SlackDiagnostics Attach(string name, string content)
    {
        CheckSentStatus();
        if (!CanSend || string.IsNullOrWhiteSpace(content))
            return this;  // A Slack log was requested, but can't send yet.  No file will be created.
        Attachments ??= new List<string>();
        UploadDirectory = Path.Combine(Environment.CurrentDirectory, FILE_DIRECTORY, ID);
        string path = Path.Combine(UploadDirectory, name);
        Directory.CreateDirectory(UploadDirectory);
        File.WriteAllText(path, content);
        Attachments.Add(path);

        return this;
    }

    /// <summary>
    /// Adds a channel to the list of places to send this message.
    /// </summary>
    /// <param name="channelId">The ID of a channel to send to.  You can find this by right-clicking on a channel in Slack
    /// and selecting "Open channel details".</param>
    /// <returns>The SlackDiagnostics object for chaining.</returns>
    public SlackDiagnostics AddChannel(string channelId)
    {
        CheckSentStatus();
        AdditionalChannels ??= new List<string>();
        AdditionalChannels.Add(channelId);
        return this;
    }

    /// <summary>
    /// Adds text to the Slack message.  All messages appear before the attachments.
    /// </summary>
    /// <param name="content">The text to send to Slack.</param>
    /// <returns>The SlackDiagnostics object for chaining.</returns>
    public SlackDiagnostics AddMessage(string content)
    {
        CheckSentStatus();
        Messages ??= new List<string>();
        Messages.Add(content);
        return this;
    }

    /// <summary>
    /// Alternative to Send().  Sends direct messages directly to users.  Assumes you want to ignore the default log channel.
    /// To use the default log channel in addition to a DM, use AddChannel(PlatformEnvironment.SlackLogChannel) before this call.
    /// Calling this ends the chain and prevents further sending.
    /// </summary>
    /// <param name="owners">People to send DMs to.</param>
    /// <returns>An awaitable Task.</returns>
    public async Task DirectMessage(params Owner[] owners)
    {
        CheckSentStatus();
        AdditionalChannels ??= new List<string>();

        // If we're sending a DM, ignore the default log channel.
        if (!string.IsNullOrWhiteSpace(PlatformEnvironment.SlackLogChannel) && Client.Channels.Contains(PlatformEnvironment.SlackLogChannel))
            Client.Channels.Remove(PlatformEnvironment.SlackLogChannel);

        foreach (Owner owner in owners.Distinct())
        {
            SlackUser user = Client.UserSearch(owner).FirstOrDefault();
            if (user != null)
                AdditionalChannels.Add(user.ID);
        }

        await Send();
    }

    /// <summary>
    /// Tag user(s) based on Log Owner with the associated message.  Owners can only be tagged from 8am - 6pm PST; otherwise
    /// names just come through with backtick code formatting.
    /// </summary>
    /// <param name="owners">The owner(s) to tag in Slack.</param>
    /// <returns>The SlackDiagnostics object for chaining.</returns>
    public SlackDiagnostics Tag(params Owner[] owners)
    {
        CheckSentStatus();
        foreach (Owner owner in owners.Where(owner => !UsersToTag.ContainsKey(owner)))
            UsersToTag[owner] = Client.UserSearch(owner).FirstOrDefault();
        return this;
    }

    private bool CheckSentStatus() => Sent ? throw new SlackMessageException(AllChannels, "Message has already been sent.") : false;

    /// <summary>
    /// Creates a log message to send to Slack.  All methods can be chained to create the Log, which must end in
    /// .Send() to actually go out.
    /// </summary>
    /// <param name="title">The title of the message.  This is displayed in a header font and should be relatively short.</param>
    /// <param name="message">Any extra detail you want to include.  For longer messages, you can also use AddMessage().</param>
    /// <returns>The SlackDiagnostics object for chaining.</returns>
    public static SlackDiagnostics Log(string title, string message) => new SlackDiagnostics(title, message);

    private struct Cache
    {
        internal long LastTimestampSent;
        internal int Count;
    }
}