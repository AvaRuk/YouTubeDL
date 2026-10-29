using YoutubeCulvert.Internal;

namespace YoutubeCulvert.Parsing;

/// <summary>
/// Comment parsing. YouTube returns both the legacy <c>commentRenderer</c> and
/// the new <c>commentViewModel</c> + <c>commentEntityPayload</c> pair, so both
/// are supported.
/// </summary>
public sealed class CommentParser : IResponseParser<JsonElement, Comment>
{
    public ParseResult<Comment> Parse(JsonElement root)
    {
        var comments = new List<Comment>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entities = CollectCommentEntities(root);
        var replyContinuations = CollectReplyContinuations(root);
        var sawKnownShape = false;

        foreach (var element in root.Descendants())
        {
            Comment? comment = null;

            if (element.TryGetProperty("commentRenderer", out var oldRenderer) &&
                oldRenderer.ValueKind == JsonValueKind.Object)
            {
                sawKnownShape = true;
                comment = ParseOldComment(oldRenderer);
            }
            else if (element.TryGetProperty("commentViewModel", out var viewModel) &&
                     viewModel.ValueKind == JsonValueKind.Object)
            {
                sawKnownShape = true;
                comment = ParseViewModelComment(viewModel, entities);
            }

            if (comment is null || !seen.Add(comment.Id))
                continue;

            if (replyContinuations.TryGetValue(comment.Id, out var continuation))
                comment = comment with { RepliesContinuation = continuation };

            comments.Add(comment);
        }

        var recognized = sawKnownShape ||
                         root.TryGetProperty("contents", out _) ||
                         root.TryGetProperty("frameworkUpdates", out _) ||
                         root.TryGetProperty("continuationContents", out _) ||
                         root.TryGetProperty("messageRenderer", out _) ||
                         root.FindFirstObjectWithProperty("commentsEntryPointHeaderRenderer") is not null;

        if (!recognized)
        {
            return ParseResult<Comment>.Unrecognized(
                "response has no comment renderers or comment containers",
                "contents");
        }

        return ParseResult<Comment>.Recognized(
            comments,
            FindCommentsPageToken(root) ?? root.FindLastContinuationToken());
    }

    /// <summary>
    /// Returns the "load more comments" token. Only top-level continuation items
    /// are considered: the last token in a comments response often belongs to
    /// the reply thread of the last comment.
    /// </summary>
    public static string? FindCommentsPageToken(JsonElement root)
    {
        string? token = null;

        foreach (var element in root.Descendants())
        {
            if (!element.TryGetProperty("continuationItems", out var items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("continuationItemRenderer", out var renderer) ||
                    renderer.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                token = renderer.FindContinuationToken() ?? token;
            }
        }

        return token;
    }

    /// <summary>Finds the comments continuation of a watch-next response.</summary>
    public static string? FindCommentsContinuation(JsonElement root)
    {
        foreach (var element in root.Descendants())
        {
            if (element.TryGetProperty("sectionIdentifier", out var section) &&
                section.ValueKind == JsonValueKind.String &&
                section.GetString()?.Contains("comment", StringComparison.OrdinalIgnoreCase) == true)
            {
                var token = element.FindContinuationToken();
                if (token is not null)
                    return token;
            }
        }

        foreach (var element in root.Descendants())
        {
            if (element.TryGetProperty("targetId", out var targetId) &&
                targetId.ValueKind == JsonValueKind.String &&
                targetId.GetString()?.Contains("comments", StringComparison.OrdinalIgnoreCase) == true)
            {
                var token = element.FindContinuationToken();
                if (token is not null)
                    return token;
            }
        }

        foreach (var element in root.Descendants())
        {
            if (!element.TryGetProperty("commentsEntryPointHeaderRenderer", out var entryPoint) ||
                entryPoint.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var token = entryPoint.FindContinuationToken();
            if (token is not null)
                return token;
        }

        return null;
    }

    /// <summary>Finds the <c>commentThreadRenderer</c> of a comment.</summary>
    public static JsonElement? FindCommentThread(JsonElement root, string commentId)
    {
        foreach (var element in root.Descendants())
        {
            if (!element.TryGetProperty("commentThreadRenderer", out var thread) ||
                thread.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (string.Equals(ReadThreadCommentId(thread), commentId, StringComparison.Ordinal))
                return thread;
        }

        return null;
    }

    /// <summary>Returns the replies subtree of a <c>commentThreadRenderer</c>.</summary>
    public static JsonElement? GetRepliesContainer(JsonElement thread)
    {
        var replies = thread.Path("replies");

        if (replies is { ValueKind: JsonValueKind.Object })
            return replies;

        replies = thread.Path("comment", "replies");
        return replies is { ValueKind: JsonValueKind.Object } ? replies : null;
    }

    private static Dictionary<string, string> CollectReplyContinuations(JsonElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var element in root.Descendants())
        {
            if (!element.TryGetProperty("commentThreadRenderer", out var thread) ||
                thread.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = ReadThreadCommentId(thread);
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var container = GetRepliesContainer(thread);
            var token = container?.FindContinuationToken();

            if (token is not null)
                result[id] = token;
        }

        return result;
    }

    private static Dictionary<string, JsonElement> CollectCommentEntities(JsonElement root)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var element in root.Descendants())
        {
            if (!element.TryGetProperty("commentEntityPayload", out var payload) ||
                payload.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = payload.Path("properties", "commentId")?.GetString();
            if (!string.IsNullOrWhiteSpace(id))
                result[id] = payload;
        }

        return result;
    }

    private static Comment? ParseOldComment(JsonElement renderer)
    {
        var id = renderer.GetString("commentId");
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var authorChannelId = renderer.FindFirstObjectWithProperty("authorEndpoint") is { } authorEndpoint
            ? authorEndpoint.FindChannelId()
            : null;

        var authorThumbnail = renderer.TryGetProperty("authorThumbnail", out var thumbnail)
            ? thumbnail.ParseThumbnails().OrderByDescending(static t => t.Area).FirstOrDefault()
            : null;

        return new Comment
        {
            Id = id,
            Author = renderer.GetTextProperty("authorText"),
            AuthorChannelId = authorChannelId,
            AuthorThumbnail = authorThumbnail,
            Text = renderer.GetTextProperty("contentText"),
            LikeCount = ParseVoteCount(renderer),
            ReplyCount = renderer.GetInt32("replyCount"),
            PublishedTime = renderer.GetTextProperty("publishedTimeText"),
            IsPinned = renderer.TryGetProperty("pinnedCommentBadge", out _),
            IsHearted = renderer.GetBool("authorIsChannelOwner") == true,
            Raw = renderer.Clone()
        };
    }

    private static Comment? ParseViewModelComment(
        JsonElement viewModel,
        IReadOnlyDictionary<string, JsonElement> entities)
    {
        var id = viewModel.GetString("commentId")
                 ?? viewModel.Path("commentViewModel", "commentId")?.GetString();

        if (string.IsNullOrWhiteSpace(id))
            return null;

        if (!entities.TryGetValue(id, out var entity))
            return new Comment { Id = id, Raw = viewModel.Clone() };

        var properties = entity.FindFirstObjectWithProperty("properties");
        var author = entity.FindFirstObjectWithProperty("author");
        var toolbar = entity.FindFirstObjectWithProperty("toolbar");

        return new Comment
        {
            Id = id,
            Author = author?.GetString("displayName"),
            AuthorChannelId = author?.GetString("channelId"),
            AuthorThumbnail = author?.GetString("avatarThumbnailUrl") is { } avatar
                ? new Thumbnail { Url = avatar, Width = 48, Height = 48 }
                : null,
            Text = properties?.Path("content", "content")?.GetString()
                   ?? properties?.GetTextProperty("content"),
            LikeCount = JsonHelpers.ParseCount(
                toolbar?.GetString("likeCountNotliked") ?? toolbar?.GetString("likeCountLiked")),
            ReplyCount = JsonHelpers.ParseItemCount(toolbar?.GetString("replyCount")),
            PublishedTime = properties?.GetString("publishedTime"),
            IsPinned = properties?.TryGetProperty("pinnedCommentBadge", out _) == true,
            IsHearted = author?.GetBool("isCreator") == true,
            Raw = entity.Clone()
        };
    }

    private static long? ParseVoteCount(JsonElement renderer)
    {
        if (!renderer.TryGetProperty("voteCount", out var voteCount))
            return null;

        return JsonHelpers.ParseCount(voteCount.GetText());
    }

    private static string? ReadThreadCommentId(JsonElement thread)
    {
        if (thread.Path("commentViewModel") is { ValueKind: JsonValueKind.Object } viewModel)
        {
            var id = viewModel.GetString("commentId")
                     ?? viewModel.Path("commentViewModel", "commentId")?.GetString();

            if (!string.IsNullOrWhiteSpace(id))
                return id;
        }

        return thread.Path("comment", "commentRenderer", "commentId")?.GetString()
               ?? thread.Path("comment", "commentViewModel", "commentId")?.GetString();
    }
}
