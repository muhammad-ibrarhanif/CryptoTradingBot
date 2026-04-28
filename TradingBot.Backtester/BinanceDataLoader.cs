using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Binance.Net.Clients;
using Binance.Net.Enums;
using global::TradingBot.Core.Models;
using TradingBot.Core.Models;

namespace TradingBot.Backtester
{


    public static class BinanceDataLoader
    {
        public static async Task<List<Candle>> FetchKlinesAsync(
            string symbol,
            KlineInterval interval,
            DateTime startTime,
            DateTime endTime)
        {
            using var restClient = new BinanceRestClient();
            var klineResult = await restClient.SpotApi.ExchangeData.GetKlinesAsync(
                symbol,
                interval,
                startTime,
                endTime);

            if (!klineResult.Success)
                throw new Exception($"Failed to fetch data: {klineResult.Error}");

            return klineResult.Data.Select(k => new Candle
            {
                OpenTime = k.OpenTime,
                Open = k.OpenPrice,
                High = k.HighPrice,
                Low = k.LowPrice,
                Close = k.ClosePrice,
                Volume = k.Volume,
                CloseTime = k.CloseTime
            }).ToList();
        }
    }
}
