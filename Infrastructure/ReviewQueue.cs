using System.Threading.Channels;
using BitbucketCodeReview.Models.Bitbucket;

namespace BitbucketCodeReview.Infrastructure;

/// <summary>
/// Bounded in-memory queue for incoming webhook payloads.
/// Replaces fire-and-forget Task.Run with a proper producer/consumer channel,
/// giving back-pressure control and graceful drain on shutdown.
/// </summary>
public sealed class ReviewQueue
{
    private readonly Channel<WebhookPayload> _channel;

    public ReviewQueue(int capacity = 50)
    {
        _channel = Channel.CreateBounded<WebhookPayload>(new BoundedChannelOptions(capacity)
        {
            FullMode          = BoundedChannelFullMode.Wait,   // block producer when full
            SingleReader      = true,
            SingleWriter      = false,
            AllowSynchronousContinuations = false
        });
    }

    /// <summary>Returns false if the queue is full and the caller should respond 429.</summary>
    public bool TryEnqueue(WebhookPayload payload)
        => _channel.Writer.TryWrite(payload);

    public IAsyncEnumerable<WebhookPayload> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);

    public int Count => _channel.Reader.Count;
}
