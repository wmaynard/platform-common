using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Interop;

public class SlackMessageClient
{
    public static readonly string POST_MESSAGE = PlatformEnvironment.Optional("SLACK_ENDPOINT_POST_MESSAGE");
    public static readonly string POST_UPLOAD = PlatformEnvironment.Optional("SLACK_ENDPOINT_UPLOAD");
    public static readonly string GET_USER_LIST = PlatformEnvironment.Optional("SLACK_ENDPOINT_USER_LIST");
    public const int SLACK_BLOCK_LIMIT = 50;

    internal HashSet<string> Channels { get; set; }
    // public string Channel { get; private set; }
    public string Token { get; private set; }

    private Task UserLoading { get; set; }

    public SlackMessageClient(string channel, string token)
    {
        Channels = new HashSet<string> { channel };
        Token = token.StartsWith("Bearer")
            ? token
            : $"Bearer {token}";
        Users = new List<SlackUser>();

        UserLoading = LoadUsers();
    }

    public List<SlackUser> Users { get; init; }

    public SlackUser[] UserSearch(params Owner[] owners)
    {
        UserLoading.Wait();
        return owners
            .Select(owner => UserSearch(OwnerInformation.Lookup(owner).AllFields).FirstOrDefault())
            .ToArray();
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="terms"></param>
    /// <returns></returns>
    public SlackUser[] UserSearch(params string[] terms)
    {
        UserLoading.Wait();
        return Users?
            .OrderByDescending(user => user.Score(terms))
            .Where(user => user.Score(terms) > 0)
            .ToArray()
            ?? Array.Empty<SlackUser>();
    }

    private async Task LoadUsers()
    {
        Log.Info(Owner.Will, "Loading workplace Slack user data.");

        await ApiService.Instance
            .Request(GET_USER_LIST)
            .AddAuthorization(Token)
            .OnSuccess((_, response) =>
            {
                foreach (RumbleJson memberData in response.AsRumbleJson.Require<RumbleJson[]>(key: "members"))
                    Users.Add(memberData);
                Log.Local(Owner.Default, "Slack member data loaded.");
            }).GetAsync();
    }

    private async Task<RumbleJson> SendToSlack(SlackMessage message)
    {
        RumbleJson response = null;

        try
        {
            RumbleJson payload = message.Compress().ToJson();
            payload["unfurl_links"] = false;
            payload["unfurl_media"] = false;

            if (ApiService.Instance != null)
                await ApiService.Instance
                    .Request(POST_MESSAGE)
                    .AddAuthorization(Token)
                    .SetPayload(payload)
                    .OnFailure((_, apiResponse) => response = apiResponse.AsRumbleJson ?? new RumbleJson())
                    .OnSuccess((_, apiResponse) =>
                    {
                        response = apiResponse.AsRumbleJson ?? new RumbleJson();
                         if (!response.Require<bool>("ok"))
                            throw new FailedRequestException(POST_MESSAGE, payload);
                    })
                    .PostAsync();
        }
        catch (Exception e)
        {
            string reason = response?.Optional<string>("error");

            if (reason == "no_text") // TD-12854
                Log.Local(Owner.Will, "No text was sent to Slack; make sure there are no empty requests.");
            else
                Log.Error(Owner.Will, "There was an error sending a message to Slack.", data: new
                {
                    SlackApiResponse = response
                }, exception: e);
        }

        Graphite.Track(Graphite.KEY_SLACK_MESSAGE_COUNT, 1, type: Graphite.Metrics.Type.FLAT);
        return response;
    }

    public async Task<RumbleJson> Send(SlackMessage message, string channel = null)
    {
        message.Compress(); // TODO: If message is split into more than one message, handle the subsequent messages

        RumbleJson response = null;

        if (!string.IsNullOrWhiteSpace(channel))
        {
            message.Channel = channel;
            response = await SendToSlack(message);
        }
        else
            foreach (string _channel in Channels)
            {
                message.Channel = _channel;
                response = await SendToSlack(message);
            }

        return response;
    }

    public async Task<RumbleJson> DirectMessage(SlackMessage message, Owner owner)
    {
        SlackUser info = UserSearch(owner).FirstOrDefault();
        message.Channel = info?.ID;
        return info == null
            ? null
            : await SendToSlack(message);
    }

    /// <summary>
    /// Sends an attachment to the channels specified.  If a channel is specified explicitly, the attachment will only
    /// go to that specific channel.
    /// </summary>
    /// <param name="path"></param>
    /// <param name="channel"></param>
    /// <returns></returns>
    /// <exception cref="FailedRequestException"></exception>
    public async Task<RumbleJson> TryUpload(string path, string channel = null)
    {
        RumbleJson response = null;
        
        string[] toSend = string.IsNullOrWhiteSpace(channel)
            ? Channels.ToArray()
            : new[] { channel };
        
        foreach (string _channel in toSend)
        {
            try
            {
                MultipartFormDataContent multiForm = new MultipartFormDataContent();
                multiForm.Add(new StringContent(Token.Replace("Bearer ", "")), name: "token"); // our token isn't a header here
                multiForm.Add(new StringContent(_channel), name: "channels");
                multiForm.Add(new StreamContent(File.OpenRead(path)), name: "file", Path.GetFileName(path));

                HttpResponseMessage httpResponse = await ApiService.Instance.MultipartFormPost(POST_UPLOAD, multiForm);
                response = await httpResponse.Content.ReadAsStringAsync();

                if (!response.Require<bool>("ok"))
                    throw new FailedRequestException(POST_UPLOAD, responseData: response);
            }
            catch (Exception e)
            {
                Log.Error(Owner.Default, "Unable to upload file to Slack.", data: new
                {
                    Path = path
                }, exception: e);
            }
        }

        return response;
    }

    public async void Send(List<SlackBlock> content) => await Send(new SlackMessage(content));
    public async void Send(List<SlackBlock> content, List<SlackAttachment> attachments) => await Send(new SlackMessage(content, attachments.ToArray()));
}

