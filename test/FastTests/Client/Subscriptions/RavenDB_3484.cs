﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Documents.Subscriptions;
using Raven.Client.Exceptions.Documents.Subscriptions;
using Raven.Tests.Core.Utils.Entities;
using Sparrow;
using Tests.Infrastructure;
using Xunit;
using Xunit.Extensions;

namespace FastTests.Client.Subscriptions
{
    public class RavenDB_3484 : RavenTestBase
    {
        private readonly TimeSpan _reasonableWaitTime = Debugger.IsAttached ? TimeSpan.FromSeconds(60 * 10) : TimeSpan.FromSeconds(50);

        [Fact]
        public void OpenIfFree_ShouldBeDefaultStrategy()
        {
            Assert.Equal(SubscriptionOpeningStrategy.OpenIfFree, new SubscriptionConnectionOptions("test").Strategy);
        }

        [Fact]
        public async Task ShouldRejectWhen_OpenIfFree_StrategyIsUsed()
        {
            using (var store = GetDocumentStore())
            {
                var id = store.Subscriptions.Create<User>();
                var subscription = store.Subscriptions.Open(new SubscriptionConnectionOptions(id));

                using (var session = store.OpenSession())
                {
                    session.Store(new User());
                    session.SaveChanges();
                }

                var mre = new AsyncManualResetEvent();
                subscription.Run(x => mre.Set());

                Assert.True(await mre.WaitAsync(TimeSpan.FromSeconds(60)));

                await Assert.ThrowsAsync<SubscriptionInUseException>(() => store.Subscriptions.Open(new SubscriptionConnectionOptions(id)
                {
                    Strategy = SubscriptionOpeningStrategy.OpenIfFree
                }).Run(x => { }));
            }
        }

        [Fact]
        public async Task ShouldReplaceActiveClientWhen_TakeOver_StrategyIsUsed()
        {
            using (var store = GetDocumentStore())
            {
                Server.ServerStore.Observer.Suspended = true;
                var id = store.Subscriptions.Create<User>();

                const int numberOfClients = 2;

                var subscriptions = new (Subscription<User> Subscription, Task Task, BlockingCollection<User> Items)[numberOfClients];

                using (var s = store.OpenSession())
                {
                    var usersShouldnotexist = "users/ShouldNotExist";
                    s.Store(new User(),usersShouldnotexist);
                    s.SaveChanges();
                    s.Delete(usersShouldnotexist);
                    s.SaveChanges();
                }

                try
                {
                    for (int i = 0; i < numberOfClients; i++)
                    {
                        var subscriptionOpeningStrategy = i > 0 ? SubscriptionOpeningStrategy.TakeOver : SubscriptionOpeningStrategy.OpenIfFree;
                        var subscription = store.Subscriptions.Open<User>(new SubscriptionConnectionOptions(id)
                        {
                            Strategy = subscriptionOpeningStrategy
                        });

                        var items = new BlockingCollection<User>();
                        
                        var batchAcknowledgedMre = new AsyncManualResetEvent();

                        subscription.AfterAcknowledgment += x =>
                        {
                            batchAcknowledgedMre.Set();
                            return Task.CompletedTask;
                        };

                        var subscriptionRunningTask = subscription.Run(x =>
                        {
                            foreach (var item in x.Items)
                            {
                                items.Add(item.Result);
                            }
                        });

                        if (i > 0)
                        {
                            Assert.True(await subscriptions[i - 1].Task.WaitAsync(TimeSpan.FromSeconds(60)));
                            await Assert.ThrowsAsync<SubscriptionInUseException>(() => subscriptions[i - 1].Task);
                        }

                        using (var s = store.OpenSession())
                        {
                            s.Store(new User());
                            s.Store(new User());

                            s.SaveChanges();
                        }

                        subscriptions[i] = (subscription, subscriptionRunningTask, items);

                        Assert.True(subscriptions[i].Items.TryTake(out _, _reasonableWaitTime));
                        Assert.True(subscriptions[i].Items.TryTake(out _, _reasonableWaitTime));

                        Assert.True(await batchAcknowledgedMre.WaitAsync(TimeSpan.FromSeconds(10))); // let it acknowledge the processed batch before we open another subscription

                        if (i > 0)
                        {
                            Assert.False(subscriptions[i - 1].Items.TryTake(out _, TimeSpan.FromSeconds(1)));
                        }
                    }
                }
                finally
                {
                    foreach (var valueTuple in subscriptions)
                    {
                        valueTuple.Subscription?.Dispose();
                    }
                }
            }
        }

        [Fact]
        public void ShouldOpenSubscriptionWith_WaitForFree_StrategyWhenItIsNotInUseByAnotherClient()
        {
            using (var store = GetDocumentStore())
            {
                var id = store.Subscriptions.Create<User>();
                var subscription = store.Subscriptions.Open<User>(new SubscriptionConnectionOptions(id)
                {
                    Strategy = SubscriptionOpeningStrategy.WaitForFree
                });

                var items = new BlockingCollection<User>();
                
                using (var s = store.OpenSession())
                {
                    s.Store(new User());
                    s.Store(new User());

                    s.SaveChanges();
                }

                subscription.Run(batch => batch.Items.ForEach(x => items.Add(x.Result)));

                Assert.True(items.TryTake(out _, _reasonableWaitTime));
                Assert.True(items.TryTake(out _, _reasonableWaitTime));
            }
        }

        [Fact]
        public async Task ShouldProcessSubscriptionAfterItGetsReleasedWhen_WaitForFree_StrategyIsSet()
        {
            using (var store = GetDocumentStore())
            {
                var id = store.Subscriptions.Create<User>();

                var userId = 0;

                foreach (var activeClientStrategy in new[] { SubscriptionOpeningStrategy.OpenIfFree, SubscriptionOpeningStrategy.TakeOver})
                {
                    var activeSubscriptionMre = new AsyncManualResetEvent();
                    var activeSubscription = store.Subscriptions.Open<User>(new SubscriptionConnectionOptions(id)
                    {
                        Strategy = activeClientStrategy
                    });

                    var pendingSubscription = store.Subscriptions.Open<User>(new SubscriptionConnectionOptions(id)
                    {
                        Strategy = SubscriptionOpeningStrategy.WaitForFree
                    });

                    var pendingBatchAcknowledgedMre = new AsyncManualResetEvent();
                    pendingSubscription.AfterAcknowledgment += x =>
                    {
                        pendingBatchAcknowledgedMre.Set();
                        return Task.CompletedTask;
                    };

                    var items = new BlockingCollection<User>();
                    
                    using (var s = store.OpenSession())
                    {
                        s.Store(new User(), "users/" + userId++);
                        s.Store(new User(), "users/" + userId++);

                        s.SaveChanges();
                    }

                    activeSubscription.AfterAcknowledgment += async x => activeSubscriptionMre.Set();

                    _ = activeSubscription.Run(x => { });
                    Assert.True(await activeSubscriptionMre.WaitAsync(_reasonableWaitTime));
                    _ = pendingSubscription.Run(batch => batch.Items.ForEach(i => items.Add(i.Result)));
                    activeSubscriptionMre.Reset();

                    using (var s = store.OpenSession())
                    {
                        s.Store(new User(), "users/" + userId++);
                        s.Store(new User(), "users/" + userId++);

                        s.SaveChanges();
                    }

                    Assert.True(await activeSubscriptionMre.WaitAsync(_reasonableWaitTime));

                    activeSubscription.Dispose(); // disconnect the active client, the pending one should be notified the the subscription is free and retry to open it

                    using (var s = store.OpenSession())
                    {
                        s.Store(new User(), "users/" + userId++);
                        s.Store(new User(), "users/" + userId++);

                        s.SaveChanges();
                    }

                    User user;

                    Assert.True(items.TryTake(out user, _reasonableWaitTime));
                    Assert.Equal("users/" + (userId - 4), user.Id);
                    Assert.True(items.TryTake(out user, _reasonableWaitTime));
                    Assert.Equal("users/" + (userId - 3), user.Id);

                    Assert.True(await pendingBatchAcknowledgedMre.WaitAsync(_reasonableWaitTime)); // let it acknowledge the processed batch before we open another subscription

                    pendingSubscription.Dispose();
                }
            }
        }

     
    }
}
