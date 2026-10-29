using YoutubeCulvert.Authentication;
using YoutubeCulvert.Handle;
using YoutubeCulvert.InnerTube.Endpoints;
using YoutubeCulvert.Url;

namespace YoutubeCulvert.Services;

/// <summary>Account actions: like, subscribe, comment and playlist edits.</summary>
public sealed class InteractionsService : IInteractionsService
{
    private readonly AccountEndpoint _accounts;
    private readonly ChannelHandleResolver _resolver;
    private readonly IAuthenticationService _auth;
    private readonly IYoutubeUrlParser _urls;

    internal InteractionsService(
        AccountEndpoint accounts,
        ChannelHandleResolver resolver,
        IAuthenticationService auth,
        IYoutubeUrlParser urls)
    {
        _accounts = accounts;
        _resolver = resolver;
        _auth = auth;
        _urls = urls;
    }

    public async Task<InteractionResult> LikeAsync(
        string videoId,
        LikeState state,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

        var id = _urls.ParseVideoIdOrThrow(videoId);
        var result = await _accounts.SetRatingAsync(id, state, targetIsPlaylist: false, ct)
            .ConfigureAwait(false);

        return ToInteraction(result.Raw);
    }

    public async Task<InteractionResult> SubscribeAsync(
        string channelId,
        bool subscribed,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

        var channel = _urls.ParseChannelOrThrow(channelId);
        var id = await _resolver.ResolveAsync(channel, ct).ConfigureAwait(false);

        var result = await _accounts.SetSubscriptionAsync(id, subscribed, ct).ConfigureAwait(false);
        return ToInteraction(result.Raw);
    }

    public async Task<InteractionResult> SetNotificationPreferenceAsync(
        string channelId,
        SubscriptionNotifications notifications,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

        var channel = _urls.ParseChannelOrThrow(channelId);
        var id = await _resolver.ResolveAsync(channel, ct).ConfigureAwait(false);

        var result = await _accounts
            .SetNotificationPreferenceAsync(id, notifications, ct)
            .ConfigureAwait(false);

        return ToInteraction(result.Raw);
    }

    public async Task<InteractionResult> PostCommentAsync(
        string videoId,
        string text,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

        var id = _urls.ParseVideoIdOrThrow(videoId);
        var result = await _accounts.CreateCommentAsync(id, text, null, ct).ConfigureAwait(false);

        return ToInteraction(result.Raw);
    }

    public async Task<InteractionResult> ReplyAsync(
        string commentId,
        string text,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

        var result = await _accounts.CreateCommentReplyAsync(commentId, text, ct).ConfigureAwait(false);
        return ToInteraction(result.Raw);
    }

    public async Task<InteractionResult> CreatePlaylistAsync(
        string title,
        PlaylistPrivacy privacy = PlaylistPrivacy.Private,
        string? description = null,
        IEnumerable<string>? videoIds = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

        var ids = (videoIds ?? [])
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(_urls.ParseVideoIdOrThrow)
            .ToArray();

        var result = await _accounts
            .CreatePlaylistAsync(title, ids, privacy, description, ct)
            .ConfigureAwait(false);

        return ToInteraction(result.Raw);
    }

    public async Task<InteractionResult> AddToPlaylistAsync(
        string playlistId,
        string videoId,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

        var playlist = _urls.ParsePlaylistIdOrThrow(playlistId);
        var video = _urls.ParseVideoIdOrThrow(videoId);

        var actions = new JsonArray
        {
            new JsonObject
            {
                ["action"] = "ACTION_ADD_VIDEO",
                ["addedVideoId"] = video
            }
        };

        var result = await _accounts.EditPlaylistAsync(playlist, actions, ct).ConfigureAwait(false);
        return ToInteraction(result.Raw);
    }

    public async Task<InteractionResult> RemoveFromPlaylistAsync(
        string playlistId,
        string? setVideoId = null,
        string? videoId = null,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

        var playlist = _urls.ParsePlaylistIdOrThrow(playlistId);

        if (string.IsNullOrWhiteSpace(setVideoId) && string.IsNullOrWhiteSpace(videoId))
        {
            throw new ArgumentException(
                "Provide either setVideoId or videoId to remove a playlist entry.",
                nameof(videoId));
        }

        var action = !string.IsNullOrWhiteSpace(setVideoId)
            ? new JsonObject
            {
                ["action"] = "ACTION_REMOVE_VIDEO",
                ["setVideoId"] = setVideoId
            }
            : new JsonObject
            {
                ["action"] = "ACTION_REMOVE_VIDEO_BY_VIDEO_ID",
                ["removedVideoId"] = _urls.ParseVideoIdOrThrow(videoId!)
            };

        var result = await _accounts
            .EditPlaylistAsync(playlist, new JsonArray(action), ct)
            .ConfigureAwait(false);

        return ToInteraction(result.Raw);
    }

    public async Task<InteractionResult> DeletePlaylistAsync(
        string playlistId,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

        var playlist = _urls.ParsePlaylistIdOrThrow(playlistId);
        var result = await _accounts.DeletePlaylistAsync(playlist, ct).ConfigureAwait(false);

        return ToInteraction(result.Raw);
    }

    public async Task<InteractionResult> RenamePlaylistAsync(
        string playlistId,
        string title,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

        var playlist = _urls.ParsePlaylistIdOrThrow(playlistId);
        var actions = new JsonArray
        {
            new JsonObject
            {
                ["action"] = "ACTION_SET_PLAYLIST_NAME",
                ["playlistName"] = title
            }
        };

        var result = await _accounts.EditPlaylistAsync(playlist, actions, ct).ConfigureAwait(false);
        return ToInteraction(result.Raw);
    }

    public async Task<InteractionResult> SetPlaylistDescriptionAsync(
        string playlistId,
        string description,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

        var playlist = _urls.ParsePlaylistIdOrThrow(playlistId);
        var actions = new JsonArray
        {
            new JsonObject
            {
                ["action"] = "ACTION_SET_PLAYLIST_DESCRIPTION",
                ["playlistDescription"] = description ?? string.Empty
            }
        };

        var result = await _accounts.EditPlaylistAsync(playlist, actions, ct).ConfigureAwait(false);
        return ToInteraction(result.Raw);
    }

    public Task<InteractionResult> AddToWatchLaterAsync(
        string videoId,
        CancellationToken ct = default) =>
        AddToPlaylistAsync("WL", videoId, ct);

    public async Task<InteractionResult> RemoveFromWatchLaterAsync(
        string videoId,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

        var video = _urls.ParseVideoIdOrThrow(videoId);
        var actions = new JsonArray
        {
            new JsonObject
            {
                ["action"] = "ACTION_REMOVE_VIDEO_BY_VIDEO_ID",
                ["removedVideoId"] = video
            }
        };

        var result = await _accounts.EditPlaylistAsync("WL", actions, ct).ConfigureAwait(false);
        return ToInteraction(result.Raw);
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        await _auth.InitializeAsync(ct).ConfigureAwait(false);

        if (!_auth.IsAuthenticated)
        {
            throw new YoutubeAuthenticationRequiredException(
                "This operation requires a signed-in YouTube account. " +
                "Call Auth.SignInWithCookieHeaderAsync or Auth.StartDeviceCodeFlowAsync first.");
        }
    }

    private static InteractionResult ToInteraction(JsonElement raw) => new()
    {
        Success = !HasSoftError(raw),
        StatusCode = 200,
        Raw = raw
    };

    private static bool HasSoftError(JsonElement raw) =>
        raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("error", out _);
}
