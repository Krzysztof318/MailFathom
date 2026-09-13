// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using NSubstitute;
using StackExchange.Redis;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>A RESP endpoint held in memory, delivering what one replica publishes to every replica subscribed to the channel.</summary>
/// <remarks>
/// Each replica is handed a substitute for the library's own subscriber interface, so what a test exercises is the use
/// of that interface rather than a copy of it. Delivery is synchronous, on the publisher's thread, which a real endpoint
/// never is; what a test asserts is whether a message arrived, never which thread it arrived on.
/// </remarks>
internal sealed class InMemoryBackplane
{
    private readonly Lock mutex = new();
    private readonly List<(RedisChannel Channel, Action<RedisChannel, RedisValue> Handler)> subscriptions = [];
    private readonly TaskCompletionSource firstSubscription = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets a task that completes once any replica has subscribed, which is the moment an announcement can be heard.</summary>
    internal Task FirstSubscription => this.firstSubscription.Task;

    /// <summary>Opens one replica's subscriber over this endpoint.</summary>
    /// <returns>A subscriber whose publications reach every subscriber this endpoint opened.</returns>
    internal ISubscriber Connect()
    {
        var subscriber = Substitute.For<ISubscriber>();

        subscriber
            .SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>())
            .Returns(call =>
            {
                lock (this.mutex)
                {
                    this.subscriptions.Add((call.ArgAt<RedisChannel>(0), call.ArgAt<Action<RedisChannel, RedisValue>>(1)));
                }

                this.firstSubscription.TrySetResult();

                return Task.CompletedTask;
            });

        subscriber
            .PublishAsync(Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(call => Task.FromResult(this.Deliver(call.ArgAt<RedisChannel>(0), call.ArgAt<RedisValue>(1))));

        return subscriber;
    }

    private long Deliver(RedisChannel channel, RedisValue message)
    {
        Action<RedisChannel, RedisValue>[] handlers;

        lock (this.mutex)
        {
            handlers = [.. this.subscriptions.Where(subscription => subscription.Channel == channel).Select(subscription => subscription.Handler)];
        }

        foreach (var handler in handlers)
        {
            handler(channel, message);
        }

        return handlers.Length;
    }
}
