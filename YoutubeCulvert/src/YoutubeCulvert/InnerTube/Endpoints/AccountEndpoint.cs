using System.Diagnostics;

namespace YoutubeCulvert.InnerTube.Endpoints;

/// <summary>
/// Account endpoints: account menu, notifications and all mutating actions.
/// Every mutation is sent with <c>IsMutation = true</c> and therefore is never
/// retried by <see cref="Middleware.RetryMiddleware"/>.
/// </summary>
public sealed class AccountEndpoint
{
    private const string SubscribeParams = "EgIIAhgA";
    private const string UnsubscribeParams = "CgIIAhgA";

    private readonly IInnerTubeTransport _transport;

    public AccountEndpoint(IInnerTubeTransport transport) => _transport = transport;

    public Task<EndpointResult<JsonElement>> GetMenuAsync(CancellationToken ct = default) =>
        SendAsync(
            "account/account_menu",
            new JsonObject(),
            isMutation: false,
            ct);

    public Task<EndpointResult<JsonElement>> GetNotificationsAsync(
        string? continuation,
        CancellationToken ct = default)
    {
        var payload = new JsonObject();

        if (!string.IsNullOrWhiteSpace(continuation))
            payload["continuation"] = continuation;
        else
            payload["notificationsMenuRequestType"] = "NOTIFICATIONS_MENU_REQUEST_TYPE_INBOX";

        return SendAsync("notification/get_notification_menu", payload, isMutation: false, ct);
    }

    public Task<EndpointResult<JsonElement>> GetUnseenNotificationCountAsync(CancellationToken ct = default) =>
        SendAsync("notification/get_unseen_count", new JsonObject(), isMutation: false, ct);

    public Task<EndpointResult<JsonElement>> SetRatingAsync(
        string target,
        LikeState state,
        bool targetIsPlaylist,
        CancellationToken ct = default)
    {
        var endpoint = state switch
        {
            LikeState.Like => "like/like",
            LikeState.Dislike => "like/dislike",
            _ => "like/removelike"
        };

        var payload = new JsonObject
        {
            ["target"] = new JsonObject
            {
                [targetIsPlaylist ? "playlistId" : "videoId"] = target
            }
        };

        return SendAsync(endpoint, payload, isMutation: true, ct);
    }

    public Task<EndpointResult<JsonElement>> SetSubscriptionAsync(
        string channelId,
        bool subscribed,
        CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            ["channelIds"] = new JsonArray(channelId),
            ["params"] = subscribed ? SubscribeParams : UnsubscribeParams
        };

        return SendAsync(
            subscribed ? "subscription/subscribe" : "subscription/unsubscribe",
            payload,
            isMutation: true,
            ct);
    }

    public Task<EndpointResult<JsonElement>> SetNotificationPreferenceAsync(
        string channelId,
        SubscriptionNotifications notifications,
        CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            ["params"] = Protobuf.CommentParams.CreateNotificationPreferenceParams(channelId, notifications)
        };

        return SendAsync("notification/modify_channel_preference", payload, isMutation: true, ct);
    }

    public Task<EndpointResult<JsonElement>> CreateCommentAsync(
        string videoId,
        string text,
        string? parentCommentId,
        CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            ["commentText"] = text,
            ["createCommentParams"] = Protobuf.CommentParams.CreateCommentParams(videoId, parentCommentId)
        };

        return SendAsync("comment/create_comment", payload, isMutation: true, ct);
    }

    public Task<EndpointResult<JsonElement>> CreateCommentReplyAsync(
        string commentId,
        string text,
        CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            ["commentText"] = text,
            ["commentId"] = commentId
        };

        return SendAsync("comment/create_comment_reply", payload, isMutation: true, ct);
    }

    public Task<EndpointResult<JsonElement>> CreatePlaylistAsync(
        string title,
        IReadOnlyList<string> videoIds,
        PlaylistPrivacy privacy,
        string? description,
        CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            ["title"] = title,
            ["videoIds"] = new JsonArray(videoIds.Select(static id => (JsonNode?)JsonValue.Create(id)).ToArray()),
            ["privacyStatus"] = privacy switch
            {
                PlaylistPrivacy.Private => "PRIVATE",
                PlaylistPrivacy.Unlisted => "UNLISTED",
                _ => "PUBLIC"
            }
        };

        if (!string.IsNullOrWhiteSpace(description))
            payload["description"] = description;

        return SendAsync("playlist/create", payload, isMutation: true, ct);
    }

    public Task<EndpointResult<JsonElement>> DeletePlaylistAsync(
        string playlistId,
        CancellationToken ct = default)
    {
        var payload = new JsonObject { ["sourcePlaylistId"] = playlistId };
        return SendAsync("playlist/delete", payload, isMutation: true, ct);
    }

    public Task<EndpointResult<JsonElement>> EditPlaylistAsync(
        string playlistId,
        JsonArray actions,
        CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            ["playlistId"] = playlistId,
            ["actions"] = actions
        };

        return SendAsync("browse/edit_playlist", payload, isMutation: true, ct);
    }

    private async Task<EndpointResult<JsonElement>> SendAsync(
        string endpoint,
        JsonObject payload,
        bool isMutation,
        CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();

        var raw = await _transport
            .SendAsync(
                new InnerTubeRequest(endpoint, payload, AuthMode: AuthMode.Auto, IsMutation: isMutation),
                ct)
            .ConfigureAwait(false);

        return new EndpointResult<JsonElement>(
            raw,
            FromCache: false,
            Stopwatch.GetElapsedTime(started));
    }
}
