using System.Threading.Channels;
using BitbucketCodeReview.Models.Bitbucket;

namespace BitbucketCodeReview.Infrastructure;

/// <summary>
/// Bounded in-memory queue for incoming webhook payloads.
/// Uses a Channel for proper producer/consumer back-pressure and graceful shutdown drain.
/// </summary>
public sealed class ReviewQueue
{
    private readonly Channel<WebhookPayload> _channel;

    public ReviewQueue(int capacity = 50)
    {
        _channel = Channel.CreateBounded<WebhookPayload>(new BoundedChannelOptions(capacity)
        {
            // DropWrite matches TryWrite semantics: returns false when full, item is dropped.
            // The controller converts false → HTTP 503 to signal back-pressure to Bitbucket.
            FullMode     = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    /// <summary>Returns false if the queue is full — caller should respond 503.</summary>
    public bool TryEnqueue(WebhookPayload payload) => _channel.Writer.TryWrite(payload);

    public IAsyncEnumerable<WebhookPayload> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);

    public int Count => _channel.Reader.Count;
}
