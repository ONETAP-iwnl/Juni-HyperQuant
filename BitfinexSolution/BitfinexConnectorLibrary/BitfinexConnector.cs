using BitfinexConnectorLibrary.Interfaces;
using BitfinexConnectorLibrary.Models;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BitfinexConnectorLibrary
{
    public class BitfinexConnector : ITestConnector
    {
        public event Action<Trade> NewBuyTrade;
        public event Action<Trade> NewSellTrade;
        public event Action<Candle> CandleSeriesProcessing;
        public async Task<IEnumerable<Candle>> GetCandleSeriesAsync(string pair, int periodInSec, DateTimeOffset? from, DateTimeOffset? to = null, long? count = 0)
        {
            var period = periodInSec switch
            {
                60 => "1m",
                300 => "5m",
                900 => "15m",
                _ => throw new ArgumentException("иного периода нет")
            };
            var pairs = $"t{pair.ToUpper()}"; //вначале "t", потому что битфинех не принимает просто BTCUSTD
            var url = $"https://api-pub.bitfinex.com/v2/candles/trade:{period}:{pairs}/hist";

            var options = new RestClientOptions(url);
            var client = new RestClient(options);
            var request = new RestRequest("");
            request.AddHeader("accept", "application/json");
            if(from != null)
            {
                request.AddQueryParameter("start", from.Value.ToUnixTimeMilliseconds());
            }
            if (to != null)
            {
                request.AddQueryParameter("end", to.Value.ToUnixTimeMilliseconds());
            }
            if(count >0)
            {
                request.AddQueryParameter("limit", count.ToString());
            }
            var response = await client.GetAsync(request);
            if(!response.IsSuccessful)
            {
                throw new HttpRequestException($"ошибка:{response.StatusCode} - {response.Content}");
            }
            var responsedata = JsonConvert.DeserializeObject<List<List<object>>>(response.Content); //еще битфинтех возвращает массив массивов, окак
            var candle = responsedata.Select(d => new Candle
            {
                Pair = pair,
                OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(d[0])),
                OpenPrice = Convert.ToDecimal(d[1]),
                ClosePrice = Convert.ToDecimal(d[2]),
                HighPrice = Convert.ToDecimal(d[3]),
                LowPrice = Convert.ToDecimal(d[4]),
                TotalVolume = Convert.ToDecimal(d[5]),
                TotalPrice = 0, //там битфинех не дает тотал прайс
            });
            return candle;
        }

        public Task<IEnumerable<Trade>> GetNewTradesAsync(string pair, int maxCount)
        {
            
        }

        public void SubscribeCandles(string pair, int periodInSec, DateTimeOffset? from = null, DateTimeOffset? to = null, long? count = 0)
        {
            throw new NotImplementedException();
        }

        public void SubscribeTrades(string pair, int maxCount = 100)
        {
            throw new NotImplementedException();
        }

        public void UnsubscribeCandles(string pair)
        {
            throw new NotImplementedException();
        }

        public void UnsubscribeTrades(string pair)
        {
            throw new NotImplementedException();
        }
    }
}
