namespace YoutubeCulvert.Protobuf;

/// <summary>
/// Builds InnerTube tokens for comment sections, comment creation and
/// subscription notification levels.
/// </summary>
/// <remarks>
/// Comments section token (GetCommentsSectionParams):
/// <code>
/// field 2 (message) — context { field 2 video_id }
/// field 3 (varint)  — constant 6
/// field 6 (message) — params {
///     field 4 (message) — opts { 4 video_id, 6 sort_by, 15 type = 2, 16 comment_id }
///     field 8 (string)  — target = "comments-section"
/// }
/// </code>
/// </remarks>
internal static class CommentParams
{
    private const string CommentsTarget = "comments-section";

    /// <summary>Continuation token that requests the first page of a comment section.</summary>
    public static string CreateCommentsSectionToken(
        string videoId,
        CommentSortOrder sortOrder,
        string? commentId = null)
    {
        var opts = new ProtoWriter()
            .WriteString(4, videoId)
            .WriteInt32(6, (int)sortOrder)
            .WriteInt32(15, 2)
            .WriteString(16, commentId);

        var parameters = new ProtoWriter()
            .WriteMessage(4, opts)
            .WriteString(8, CommentsTarget);

        var token = new ProtoWriter()
            .WriteMessage(2, new ProtoWriter().WriteString(2, videoId))
            .WriteInt32Always(3, 6)
            .WriteMessage(6, parameters);

        return Escape(token.ToBase64());
    }

    /// <summary>Value for <c>createCommentParams</c> in <c>comment/create_comment</c>.</summary>
    public static string CreateCommentParams(string videoId, string? parentCommentId = null)
    {
        var token = new ProtoWriter()
            .WriteString(2, videoId)
            .WriteMessage(5, new ProtoWriter().WriteInt32Always(1, 0))
            .WriteInt32Always(10, 7)
            .WriteString(11, parentCommentId);

        return Escape(token.ToBase64());
    }

    /// <summary>Value for <c>params</c> in <c>notification/modify_channel_preference</c>.</summary>
    public static string CreateNotificationPreferenceParams(
        string channelId,
        SubscriptionNotifications notifications)
    {
        // YouTube indexes the levels as 1 = personalized, 2 = all, 3 = none.
        var index = notifications switch
        {
            SubscriptionNotifications.Personalized => 1,
            SubscriptionNotifications.All => 2,
            _ => 3
        };

        var token = new ProtoWriter()
            .WriteString(1, channelId)
            .WriteMessage(2, new ProtoWriter().WriteInt32Always(1, index))
            .WriteInt32Always(3, 0)
            .WriteInt32Always(4, 4);

        return Escape(token.ToBase64());
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
