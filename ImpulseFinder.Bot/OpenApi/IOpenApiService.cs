using Google.Protobuf;
using Samples.Shared.Models;

namespace ImpulseFinder.Bot.OpenApi
{
    public interface IOpenApiService : IDisposable
    {
        bool IsConnected { get; }

        IObservable<IMessage> LiveObservable { get; }

        IObservable<IMessage> DemoObservable { get; }

        event Action Connected;

        Task Connect(ApiCredentials apiCredentials);

        Task<ProtoOAAccountAuthRes> AuthorizeAccount(
            long accountId, bool isLive, string accessToken);

        Task<ProtoOALightSymbol[]> GetLightSymbols(long accountId, bool isLive);

        Task<ProtoOASymbol[]> GetSymbols(long accountId, bool isLive, long[] symbolIds);

        Task<SymbolModel[]> GetSymbolModels(
            long accountId, bool isLive, 
            ProtoOALightSymbol[] lightSymbols, ProtoOAAsset[] assets);

        Task<ProtoOALightSymbol[]> GetConversionSymbols(
            long accountId, bool isLive, long baseAssetId, long quoteAssetId);

        Task<ProtoOACtidTraderAccount[]> GetAccountsList(string accessToken);

        Task<ProtoOAReconcileRes> GetAccountOrders(long accountId, bool isLive);

        Task<ProtoOATrader> GetTrader(long accountId, bool isLive);

        Task<ProtoOASubscribeSpotsRes> SubscribeToSpots(
            long accountId, bool isLive, params long[] symbolIds);

        Task<ProtoOAUnsubscribeSpotsRes> UnsubscribeFromSpots(
            long accountId, bool isLive, params long[] symbolIds);

        Task<ProtoOAAsset[]> GetAssets(long accountId, bool isLive);

        Task<ProtoOATrendbar[]> GetTrendbars(long accountId, bool isLive,
            DateTimeOffset from, DateTimeOffset to, 
            ProtoOATrendbarPeriod period, long symbolId);

        Task<ProtoOASubscribeLiveTrendbarRes> SubscribeToLiveTrendbar(
            long accountId, bool isLive, long symbolId, ProtoOATrendbarPeriod period);

        Task<ProtoOAUnsubscribeLiveTrendbarRes> UnsubscribeFromLiveTrendbar(
            long accountId, bool isLive, long symbolId, ProtoOATrendbarPeriod period);

        Task<ProtoOAAccountLogoutRes> LogoutAccount(long accountId, bool isLive);
    }

}
