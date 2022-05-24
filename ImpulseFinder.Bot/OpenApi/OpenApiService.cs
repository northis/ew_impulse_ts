using System.Collections.Concurrent;
using System.Reactive.Linq;
using Google.Protobuf;
using OpenAPI.Net;
using OpenAPI.Net.Helpers;
using Samples.Shared.Models;

namespace ImpulseFinder.Bot.OpenApi
{
    public sealed class OpenApiService : IOpenApiService
    {
        private readonly Func<OpenClient> m_LiveClientFactory;
        private readonly Func<OpenClient> m_DemoClientFactory;
        private readonly ConcurrentQueue<MessageQueueItem> m_MessagesQueue = new();
        private readonly System.Timers.Timer m_SendMessageTimer;

        private OpenClient m_LiveClient;
        private OpenClient m_DemoClient;

        private ApiCredentials m_ApiCredentials;

        public OpenApiService(
            Func<OpenClient> liveClientFactory, 
            Func<OpenClient> demoClientFactory, int maxMessagePerSecond = 45)
        {
            m_LiveClientFactory = liveClientFactory ?? throw new ArgumentNullException(nameof(liveClientFactory));
            m_DemoClientFactory = demoClientFactory ?? throw new ArgumentNullException(nameof(demoClientFactory));

            m_SendMessageTimer = new(1000.0 / maxMessagePerSecond);

            m_SendMessageTimer.Elapsed += SendMessageTimerElapsed;
            m_SendMessageTimer.AutoReset = false;
        }

        public bool IsConnected { get; private set; }

        public IObservable<IMessage> LiveObservable => m_LiveClient;

        public IObservable<IMessage> DemoObservable => m_DemoClient;

        public event Action Connected;

        public async Task Connect(ApiCredentials apiCredentials)
        {
            m_ApiCredentials = apiCredentials;

            OpenClient liveClient = null;
            OpenClient demoClient = null;

            try
            {
                liveClient = m_LiveClientFactory();

                await liveClient.Connect();

                demoClient = m_DemoClientFactory();

                await demoClient.Connect();
            }
            catch
            {
                if (liveClient is not null) liveClient.Dispose();
                if (demoClient is not null) demoClient.Dispose();

                throw;
            }

            m_SendMessageTimer.Start();

            await Task.WhenAll(AuthorizeApp(liveClient, m_ApiCredentials), AuthorizeApp(demoClient, m_ApiCredentials));

            IsConnected = true;

            m_LiveClient = liveClient;
            m_DemoClient = demoClient;

            m_LiveClient.Subscribe(_ => { }, OnError);
            m_DemoClient.Subscribe(_ => { }, OnError);

            Connected?.Invoke();
        }

        private void OnError(Exception exception)
        {
            if (IsConnected is false) return;

            IsConnected = false;

            Reconnect();
        }

        private async void Reconnect()
        {
            if (m_LiveClient.IsDisposed is false || m_DemoClient.IsDisposed is false) return;

            try
            {
                await Connect(m_ApiCredentials);
            }
            catch
            {
                await Task.Delay(5000);

                Reconnect();
            }
        }

        private Task<ProtoOAApplicationAuthRes> AuthorizeApp(
            OpenClient client, ApiCredentials apiCredentials)
        {
            var taskCompletionSource = new TaskCompletionSource<ProtoOAApplicationAuthRes>();

            IDisposable? disposable = null;

            IDisposable? disposableLocal = disposable;
            disposable = client.OfType<ProtoOAApplicationAuthRes>().Subscribe(response =>
            {
                taskCompletionSource.SetResult(response);

                disposableLocal?.Dispose();
            });

            var requestMessage = new ProtoOAApplicationAuthReq
            {
                ClientId = apiCredentials.ClientId,
                ClientSecret = apiCredentials.Secret,
            };

            EnqueueMessage(requestMessage, ProtoOAPayloadType.ProtoOaApplicationAuthReq, client);

            return taskCompletionSource.Task;
        }

        public Task<ProtoOACtidTraderAccount[]> GetAccountsList(string accessToken)
        {
            var taskCompletionSource = new TaskCompletionSource<ProtoOACtidTraderAccount[]>();

            IDisposable disposable = null;

            disposable = m_DemoClient.OfType<ProtoOAGetAccountListByAccessTokenRes>().Subscribe(response =>
            {
                taskCompletionSource.SetResult(response.CtidTraderAccount.ToArray());

                disposable?.Dispose();
            });

            var requestMessage = new ProtoOAGetAccountListByAccessTokenReq
            {
                AccessToken = accessToken
            };

            EnqueueMessage(requestMessage, ProtoOAPayloadType.ProtoOaGetAccountsByAccessTokenReq, m_DemoClient);

            return taskCompletionSource.Task;
        }

        private Task<T> RequestBase<T, TK>(
            OpenClient client, Func<T, bool> filter, TK request, ProtoOAPayloadType payloadType) 
            where T : IMessage where TK : IMessage
        {
            var taskCompletionSource = new TaskCompletionSource<T>();
            IDisposable? disposable = null;
            disposable = client.OfType<T>()
                .Where(filter)
                .Subscribe(a =>
            {
                taskCompletionSource.SetResult(a);
                disposable?.Dispose();
            });

            EnqueueMessage(request, payloadType, client);
            return taskCompletionSource.Task;
        }

        public Task<ProtoOAAccountAuthRes> AuthorizeAccount(
            long accountId, bool isLive, string accessToken)
        {
            VerifyConnection();
            OpenClient client = GetClient(isLive);
            var requestMessage = new ProtoOAAccountAuthReq
            {
                CtidTraderAccountId = accountId,
                AccessToken = accessToken,
            };
            Task<ProtoOAAccountAuthRes> res = 
                RequestBase<ProtoOAAccountAuthRes, ProtoOAAccountAuthReq>(
                client,
                a => a.CtidTraderAccountId == accountId,
                requestMessage,
                ProtoOAPayloadType.ProtoOaAccountAuthReq);

            return res;
        }

        public Task<ProtoOAAccountLogoutRes> LogoutAccount(long accountId, bool isLive)
        {
            VerifyConnection();

            var client = GetClient(isLive);

            var taskCompletionSource = new TaskCompletionSource<ProtoOAAccountLogoutRes>();

            IDisposable disposable = null;

            disposable = client.OfType<ProtoOAAccountLogoutRes>().Where(response => response.CtidTraderAccountId == accountId).Subscribe(response =>
            {
                taskCompletionSource.SetResult(response);

                disposable?.Dispose();
            });

            var requestMessage = new ProtoOAAccountLogoutReq
            {
                CtidTraderAccountId = accountId,
            };

            EnqueueMessage(requestMessage, ProtoOAPayloadType.ProtoOaAccountLogoutReq, client);

            return taskCompletionSource.Task;
        }

        public Task<ProtoOALightSymbol[]> GetLightSymbols(long accountId, bool isLive)
        {
            VerifyConnection();

            var client = GetClient(isLive);

            var taskCompletionSource = new TaskCompletionSource<ProtoOALightSymbol[]>();

            IDisposable disposable = null;

            disposable = client.OfType<ProtoOASymbolsListRes>().Where(response => response.CtidTraderAccountId == accountId).Subscribe(response =>
            {
                taskCompletionSource.SetResult(response.Symbol.Where(iSymbol => iSymbol.Enabled).ToArray());

                disposable?.Dispose();
            });

            var requestMessage = new ProtoOASymbolsListReq
            {
                CtidTraderAccountId = accountId,
                IncludeArchivedSymbols = false
            };

            EnqueueMessage(requestMessage, ProtoOAPayloadType.ProtoOaSymbolsListReq, client);

            return taskCompletionSource.Task;
        }

        public Task<ProtoOASymbol[]> GetSymbols(long accountId, bool isLive, long[] symbolIds)
        {
            VerifyConnection();

            var client = GetClient(isLive);

            var taskCompletionSource = new TaskCompletionSource<ProtoOASymbol[]>();

            IDisposable disposable = null;

            disposable = client.OfType<ProtoOASymbolByIdRes>().Where(response => response.CtidTraderAccountId == accountId).Subscribe(response =>
            {
                taskCompletionSource.SetResult(response.Symbol.Where(iSymbol => iSymbol.TradingMode == ProtoOATradingMode.Enabled).ToArray());

                disposable?.Dispose();
            });

            var requestMessage = new ProtoOASymbolByIdReq
            {
                CtidTraderAccountId = accountId,
            };

            requestMessage.SymbolId.AddRange(symbolIds);

            EnqueueMessage(requestMessage, ProtoOAPayloadType.ProtoOaSymbolByIdReq, client);

            return taskCompletionSource.Task;
        }

        public async Task<SymbolModel[]> GetSymbolModels(long accountId, bool isLive, ProtoOALightSymbol[] lightSymbols, ProtoOAAsset[] assets)
        {
            var symbolIds = lightSymbols.Select(iSymbol => iSymbol.SymbolId).ToArray();

            var symbols = await GetSymbols(accountId, isLive, symbolIds);

            return lightSymbols.Where(lightSymbol => symbols.Any(symbol => lightSymbol.SymbolId == symbol.SymbolId)).Select(lightSymbol => new SymbolModel
            {
                LightSymbol = lightSymbol,
                Data = symbols.First(symbol => symbol.SymbolId == lightSymbol.SymbolId),
                BaseAsset = assets.First(iAsset => iAsset.AssetId == lightSymbol.BaseAssetId),
                QuoteAsset = assets.First(iAsset => iAsset.AssetId == lightSymbol.QuoteAssetId)
            }).ToArray();
        }

        public Task<ProtoOALightSymbol[]> GetConversionSymbols(long accountId, bool isLive, long baseAssetId, long quoteAssetId)
        {
            VerifyConnection();

            var client = GetClient(isLive);

            var taskCompletionSource = new TaskCompletionSource<ProtoOALightSymbol[]>();

            IDisposable disposable = null;

            disposable = client.OfType<ProtoOASymbolsForConversionRes>().Where(response => response.CtidTraderAccountId == accountId)
                .Subscribe(response =>
                {
                    taskCompletionSource.SetResult(response.Symbol.ToArray());

                    disposable?.Dispose();
                });

            var requestMessage = new ProtoOASymbolsForConversionReq
            {
                CtidTraderAccountId = accountId,
                FirstAssetId = baseAssetId,
                LastAssetId = quoteAssetId
            };

            EnqueueMessage(requestMessage, ProtoOAPayloadType.ProtoOaSymbolsForConversionReq, client);

            return taskCompletionSource.Task;
        }

        public Task<ProtoOAReconcileRes> GetAccountOrders(long accountId, bool isLive)
        {
            VerifyConnection();

            var client = GetClient(isLive);

            var taskCompletionSource = new TaskCompletionSource<ProtoOAReconcileRes>();

            IDisposable disposable = null;

            disposable = client.OfType<ProtoOAReconcileRes>().Where(response => response.CtidTraderAccountId == accountId).Subscribe(response =>
            {
                taskCompletionSource.SetResult(response);

                disposable?.Dispose();
            });

            var requestMessage = new ProtoOAReconcileReq
            {
                CtidTraderAccountId = accountId
            };

            EnqueueMessage(requestMessage, ProtoOAPayloadType.ProtoOaReconcileReq, client);

            return taskCompletionSource.Task;
        }
        
        public Task<ProtoOATrader> GetTrader(long accountId, bool isLive)
        {
            VerifyConnection();

            var client = GetClient(isLive);

            var taskCompletionSource = new TaskCompletionSource<ProtoOATrader>();

            IDisposable disposable = null;

            disposable = client.OfType<ProtoOATraderRes>().Where(response => response.CtidTraderAccountId == accountId).Subscribe(response =>
            {
                taskCompletionSource.SetResult(response.Trader);

                disposable?.Dispose();
            });

            var requestMessage = new ProtoOATraderReq
            {
                CtidTraderAccountId = accountId
            };

            EnqueueMessage(requestMessage, ProtoOAPayloadType.ProtoOaTraderReq, client);

            return taskCompletionSource.Task;
        }

        public Task<ProtoOASubscribeSpotsRes> SubscribeToSpots(long accountId, bool isLive, params long[] symbolIds)
        {
            var client = GetClient(isLive);

            var taskCompletionSource = new TaskCompletionSource<ProtoOASubscribeSpotsRes>();

            IDisposable disposable = null;

            disposable = client.OfType<ProtoOASubscribeSpotsRes>().Where(response => response.CtidTraderAccountId == accountId).Subscribe(response =>
            {
                taskCompletionSource.SetResult(response);

                disposable?.Dispose();
            });

            var requestMessage = new ProtoOASubscribeSpotsReq
            {
                CtidTraderAccountId = accountId,
            };

            requestMessage.SymbolId.AddRange(symbolIds);

            EnqueueMessage(requestMessage, ProtoOAPayloadType.ProtoOaSubscribeSpotsReq, client);

            return taskCompletionSource.Task;
        }

        public Task<ProtoOAUnsubscribeSpotsRes> UnsubscribeFromSpots(long accountId, bool isLive, params long[] symbolIds)
        {
            VerifyConnection();

            var client = GetClient(isLive);

            var taskCompletionSource = new TaskCompletionSource<ProtoOAUnsubscribeSpotsRes>();

            IDisposable disposable = null;

            disposable = client.OfType<ProtoOAUnsubscribeSpotsRes>().Where(response => response.CtidTraderAccountId == accountId).Subscribe(response =>
            {
                taskCompletionSource.SetResult(response);

                disposable?.Dispose();
            });

            var requestMessage = new ProtoOAUnsubscribeSpotsReq
            {
                CtidTraderAccountId = accountId,
            };

            requestMessage.SymbolId.AddRange(symbolIds);

            EnqueueMessage(requestMessage, ProtoOAPayloadType.ProtoOaUnsubscribeSpotsReq, client);

            return taskCompletionSource.Task;
        }

        public Task<ProtoOASubscribeLiveTrendbarRes> SubscribeToLiveTrendbar(long accountId, bool isLive, long symbolId, ProtoOATrendbarPeriod period)
        {
            VerifyConnection();

            var client = GetClient(isLive);

            var taskCompletionSource = new TaskCompletionSource<ProtoOASubscribeLiveTrendbarRes>();

            IDisposable disposable = null;

            disposable = client.OfType<ProtoOASubscribeLiveTrendbarRes>().Where(response => response.CtidTraderAccountId == accountId).Subscribe(response =>
            {
                taskCompletionSource.SetResult(response);

                disposable?.Dispose();
            });

            var requestMessage = new ProtoOASubscribeLiveTrendbarReq
            {
                CtidTraderAccountId = accountId,
                Period = period,
                SymbolId = symbolId
            };

            EnqueueMessage(requestMessage, ProtoOAPayloadType.ProtoOaSubscribeLiveTrendbarReq, client);

            return taskCompletionSource.Task;
        }

        public Task<ProtoOAUnsubscribeLiveTrendbarRes> UnsubscribeFromLiveTrendbar(long accountId, bool isLive, long symbolId, ProtoOATrendbarPeriod period)
        {
            VerifyConnection();

            var client = GetClient(isLive);

            var taskCompletionSource = new TaskCompletionSource<ProtoOAUnsubscribeLiveTrendbarRes>();

            IDisposable disposable = null;

            disposable = client.OfType<ProtoOAUnsubscribeLiveTrendbarRes>().Where(response => response.CtidTraderAccountId == accountId).Subscribe(response =>
            {
                taskCompletionSource.SetResult(response);

                disposable?.Dispose();
            });

            var requestMessage = new ProtoOAUnsubscribeLiveTrendbarReq
            {
                CtidTraderAccountId = accountId,
                Period = period,
                SymbolId = symbolId
            };

            EnqueueMessage(requestMessage, ProtoOAPayloadType.ProtoOaUnsubscribeLiveTrendbarReq, client);

            return taskCompletionSource.Task;
        }

        public Task<ProtoOATrendbar[]> GetTrendbars(long accountId, bool isLive, DateTimeOffset from, DateTimeOffset to, ProtoOATrendbarPeriod period, long symbolId)
        {
            VerifyConnection();

            var periodMaximum = period.GetMaximumTime();

            if (from == default) from = to.Add(-periodMaximum);

            if (to - from > periodMaximum) throw new ArgumentOutOfRangeException(nameof(to), "The time range is not valid");

            var client = GetClient(isLive);

            var taskCompletionSource = new TaskCompletionSource<ProtoOATrendbar[]>();

            IDisposable disposable = null;

            disposable = client.OfType<ProtoOAGetTrendbarsRes>().Where(response => response.CtidTraderAccountId == accountId).Subscribe(response =>
            {
                taskCompletionSource.SetResult(response.Trendbar.ToArray());

                disposable?.Dispose();
            });

            var requestMessage = new ProtoOAGetTrendbarsReq
            {
                FromTimestamp = from.ToUnixTimeMilliseconds(),
                ToTimestamp = to.ToUnixTimeMilliseconds(),
                CtidTraderAccountId = accountId,
                Period = period,
                SymbolId = symbolId
            };

            EnqueueMessage(requestMessage, ProtoOAPayloadType.ProtoOaGetTrendbarsReq, client);

            return taskCompletionSource.Task;
        }

        public Task<ProtoOAAsset[]> GetAssets(long accountId, bool isLive)
        {
            VerifyConnection();

            var client = GetClient(isLive);

            var taskCompletionSource = new TaskCompletionSource<ProtoOAAsset[]>();

            IDisposable disposable = null;

            disposable = client.OfType<ProtoOAAssetListRes>().Where(response => response.CtidTraderAccountId == accountId).Subscribe(response =>
            {
                taskCompletionSource.SetResult(response.Asset.ToArray());

                disposable?.Dispose();
            });

            var requestMessage = new ProtoOAAssetListReq
            {
                CtidTraderAccountId = accountId,
            };

            EnqueueMessage(requestMessage, ProtoOAPayloadType.ProtoOaAssetListReq, client);

            return taskCompletionSource.Task;
        }

        public void Dispose()
        {
            m_LiveClient?.Dispose();
            m_DemoClient?.Dispose();
        }

        private OpenClient GetClient(bool isLive) => isLive ? m_LiveClient : m_DemoClient;

        private void VerifyConnection()
        {
            if (IsConnected is false) throw new InvalidOperationException("The service is not connected yet, please connect the service before using it");
        }

        private void EnqueueMessage<TMessage>(
            TMessage message, ProtoOAPayloadType payloadType, OpenClient client)
            where TMessage : IMessage
        {
            bool isHistorical = payloadType is 
                ProtoOAPayloadType.ProtoOaDealListReq or 
                ProtoOAPayloadType.ProtoOaGetTrendbarsReq or 
                ProtoOAPayloadType.ProtoOaGetTickdataReq or 
                ProtoOAPayloadType.ProtoOaCashFlowHistoryListReq;
            var messageQueueItem = new MessageQueueItem(
                message, client, payloadType, isHistorical);

            m_MessagesQueue.Enqueue(messageQueueItem);
        }

        private async void SendMessageTimerElapsed
            (object? sender, System.Timers.ElapsedEventArgs e)
        {
            m_SendMessageTimer.Stop();

            if (m_MessagesQueue.TryDequeue(out var messageQueueItem) == false)
            {
                m_SendMessageTimer.Start();

                return;
            }

            await messageQueueItem.Client.SendMessage(
                messageQueueItem.Message, messageQueueItem.PayloadType);

            if (messageQueueItem.IsHistorical) await Task.Delay(250);

            m_SendMessageTimer.Start();
        }

        private record MessageQueueItem(IMessage Message,
            OpenClient Client, ProtoOAPayloadType PayloadType, bool IsHistorical);
    }
}