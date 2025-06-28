using BitfinexConnectorLibrary.Interfaces;
using BitfinexConnectorLibrary.Models;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Net.WebSockets;
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
        private ClientWebSocket _ws;
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

            var responseData = JsonConvert.DeserializeObject<List<List<object>>>(response.Content); //еще битфинтех возвращает массив массивов, окак
            var candle = responseData.Select(d => new Candle
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

        public async Task<IEnumerable<Trade>> GetNewTradesAsync(string pair, int maxCount)
        {
            var pairs = $"t{pair.ToUpper()}";
            var url = $"https://api-pub.bitfinex.com/v2/trades/{pairs}/hist";
            var options = new RestClientOptions(url);
            var client = new RestClient(options);
            var request = new RestRequest("");
            request.AddHeader("accept", "application/json");
            if(maxCount > 0)
            {
                request.AddQueryParameter("limit", maxCount.ToString());
            }
            request.AddQueryParameter("sort", "-1");
            var response = await client.GetAsync(request);
            if (!response.IsSuccessful)
            {
                throw new HttpRequestException($"ошибка:{response.StatusCode} - {response.Content}");
            }

            var tradesData = JsonConvert.DeserializeObject<List<List<object>>> (response.Content);
            var trade = tradesData.Select(d =>
            {
                var amount = Convert.ToDecimal(d[2]);
                return new Trade
                {
                    Pair = pair,
                    Id = d[0].ToString(),
                    Time = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(d[1])),
                    Amount = Math.Abs(amount),
                    Side = amount > 0 ? "buy" : "sell", //если амаунт больше 0, то купилось, если меньше 0, то продалось - направление
                    Price = Convert.ToDecimal(d[3])
                };
            });
            return trade;
        }

        public async Task<IEnumerable<Ticker>> GetTickersAsync(string pair)
        {
            var pairs = $"t{pair.ToUpper()}";
            var url = $"https://api-pub.bitfinex.com/v2/ticker/{pairs}";
            var option = new RestClientOptions(url);
            var client = new RestClient(option);
            var request = new RestRequest("");
            request.AddHeader("accept", "application/json");
            var response = await client.GetAsync(request);
            if (!response.IsSuccessful)
            {
                throw new HttpRequestException($"ошибка:{response.StatusCode} - {response.Content}");
            }

            var tickerData = JsonConvert.DeserializeObject<List<object>>(response.Content); // а вот он возвращает массив
            var tiker = tickerData.Select(d => new Ticker 
            {
                Pair = pair,
                Bid = Convert.ToDecimal(tickerData[0]),
                BidSize = Convert.ToDecimal(tickerData[1]),
                Ask = Convert.ToDecimal(tickerData[2]),
                AskSize = Convert.ToDecimal(tickerData[3]),
                DailyChange = Convert.ToDecimal(tickerData[4]),
                DailyChangeRelative = Convert.ToDecimal(tickerData[5]),
                LastPrice = Convert.ToDecimal(tickerData[6]),
                Volume = Convert.ToDecimal(tickerData[7]),
                High = Convert.ToDecimal(tickerData[8]),
                Low = Convert.ToDecimal(tickerData[9])
            });
            return tiker;
        }

        public async void SubscribeCandles(string pair, int periodInSec, DateTimeOffset? from = null, DateTimeOffset? to = null, long? count = 0)
        {
            Uri uri = new Uri("wss://api-pub.bitfinex.com/ws/2");
            await _ws.ConnectAsync(uri, CancellationToken.None);
            var period = periodInSec switch
            {
                60 => "1m",
                300 => "5m",
                900 => "15m",
                _ => throw new ArgumentException("иного периода нет")
            };
            var key = $"trade:{period}:t{pair.ToUpper()}";
            await SendJsonAsync(_ws, new
            {
                @event = "subscribe",
                channel = "candles",
                key = key
            });
            _ = Task.Run(() => ReceiveLoopAsync());
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[8192];

            while (_ws.State == WebSocketState.Open)
            {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                Console.WriteLine(message);
            }
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

        private async Task SendJsonAsync(ClientWebSocket ws, object message)
        {
            string json = JsonConvert.SerializeObject(message);
            var bytes = Encoding.UTF8.GetBytes(json);
            var buffer = new ArraySegment<byte>(bytes);
            await ws.SendAsync(buffer, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        }
    }
}
