using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;

namespace SearcherCore.Searcher.BingAPI
{
    public class SearchBingApi
    {
        private static readonly HttpClient client = new HttpClient { BaseAddress = new Uri("https://api.bing.microsoft.com/v7.0/") };
        private readonly string _secretKey;
        private const int ResultCountPerPage = 50;

        public SearchBingApi(string secretKey)
        {
            // secret key to authenticate requests
            _secretKey = secretKey;
        }

        public List<Uri> Search(string q, int pageNumber, CancellationToken cancelToken, out bool moreResults)
        {
            List<Uri> results = new List<Uri>();
            int currentOffset = ResultCountPerPage * pageNumber;
            using (var request = new HttpRequestMessage(HttpMethod.Get, $"search?count={ResultCountPerPage}&safeSearch=Off&textFormat=Raw&offset={currentOffset}&q={Uri.EscapeDataString(q)}"))
            {
                // Request header which sends the private key to the server
                request.Headers.Add("Ocp-Apim-Subscription-Key", _secretKey);
                using (HttpResponseMessage response = client.SendAsync(request, cancelToken).GetAwaiter().GetResult())
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException($"The request could not be completed. Please check your API key. Response status: {response.ReasonPhrase}");
                    }

                    JToken token = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    JToken webPagesToken = token["webPages"];
                    if (webPagesToken == null)
                    {
                        moreResults = false;
                        return results;
                    }

                    var webpages = (JArray)webPagesToken["value"];
                    foreach (string foundUrl in webpages.Select(link => ((string)link.SelectToken("url"))))
                    {
                        if (Uri.TryCreate(foundUrl, UriKind.Absolute, out Uri url))
                        {
                            results.Add(url);
                        }
                        currentOffset++;
                    }

                    moreResults = ((int)webPagesToken.SelectToken("totalEstimatedMatches")) > currentOffset;
                    return results;
                }
            }
        }
    }
}
