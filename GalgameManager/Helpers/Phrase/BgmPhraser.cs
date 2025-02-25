﻿#nullable enable
using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using System.Reflection;
using System.Web;

using GalgameManager.Contracts.Phrase;
using GalgameManager.Enums;
using GalgameManager.Models;
using Newtonsoft.Json.Linq;

namespace GalgameManager.Helpers.Phrase;

public class BgmPhraser : IGalInfoPhraser
{
    private const string ProducerFile = @"Assets\Data\producers.json";
    private HttpClient _httpClient;
    private bool _init;
    private readonly List<string> _developerList = new();

    public BgmPhraser(BgmPhraserData data)
    {
        _httpClient = new HttpClient();
        GetHttpClient(data);
    }

    public void UpdateData(IGalInfoPhraserData data)
    {
        if(data is BgmPhraserData bgmData)
            GetHttpClient(bgmData);
    }
    
    private void GetHttpClient(BgmPhraserData data)
    {
        var bgmToken = data.Token;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "GoldenPotato/GalgameManager/1.0-dev (Windows) (https://github.com/GoldenPotato137/GalgameManager)");
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if(bgmToken != null)
            _httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + bgmToken);
    }
    
    private async Task InitAsync()
    {
        _init = true;
        Assembly assembly = Assembly.GetExecutingAssembly();
        var file = Path.Combine(Path.GetDirectoryName(assembly.Location)!, ProducerFile);
        if (!File.Exists(file)) return;

        JToken json = JToken.Parse(await File.ReadAllTextAsync(file));
        List<JToken>? producers = json.ToObject<List<JToken>>();
        producers!.ForEach(dev =>
        {
            if (IsNullOrEmpty(dev["name"]!.ToString()) == false)
                _developerList.Add(dev["name"]!.ToString());
            if (IsNullOrEmpty(dev["latin"]!.ToString()) == false)
                _developerList.Add(dev["latin"]!.ToString());
            if (IsNullOrEmpty(dev["alias"]!.ToString()) == false)
            {
                var tmp = dev["alias"]!.ToString();
                _developerList.AddRange(tmp.Split("\n"));
            }
        });
    }

    private static bool IsNullOrEmpty(string str) => str is "null" or "";

    private async Task<int?> GetId(string name)
    {
        try
        {
            var url = "https://api.bgm.tv/search/subject/" + HttpUtility.UrlEncode(name) + "?type=4";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;
            var jsonToken = JToken.Parse(await response.Content.ReadAsStringAsync());
            var games = jsonToken["list"]!.ToObject<List<JToken>>();
            if (games==null || games.Count == 0) return null;
            
            double maxSimilarity = 0;
            var target = 0;
            foreach (JToken game in games)
                if (IGalInfoPhraser.Similarity(name, game["name_cn"]!.ToObject<string>()!) > maxSimilarity ||
                    IGalInfoPhraser.Similarity(name, game["name"]!.ToObject<string>()!) > maxSimilarity)
                {
                    maxSimilarity = Math.Max
                    (
                        IGalInfoPhraser.Similarity(name, game["name_cn"]!.ToObject<string>()!),
                        IGalInfoPhraser.Similarity(name, game["name"]!.ToObject<string>()!)
                    );
                    target = games.IndexOf(game);
                }
                
            return games[target]["id"]!.ToObject<int>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    #region tempDisable

    // private async Task<int?> SendPostRequestAsync()
    // {
    //     try
    //     {
    //         var url = "https://api.bgm.tv/v0/search/subjects?";
    //         var keyword = "糖调！-sugarfull tempering-";
    //         int[] typeFilter = { 4 };
    //         var requestData = new
    //         {
    //             keyword,
    //             filter = new
    //             {
    //                 type = typeFilter
    //             }
    //         };
    //         // _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/json");
    //         var content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
    //         var response = await _httpClient.PostAsync(url, content);
    //         if (!response.IsSuccessStatusCode)
    //             return null;
    //
    //         var jToken = JToken.Parse(await response.Content.ReadAsStringAsync());
    //         var games = jToken["data"];
    //         if (games[0] != null)
    //             return games[0]["id"].ToObject<int>();
    //
    //         return null;
    //     }
    //     catch (Exception e)
    //     {
    //         Console.WriteLine(e);
    //         throw;
    //     }
    // }

    #endregion
    
    public async Task<Galgame?> GetGalgameInfo(Galgame galgame)
    {
        if (_init == false)
            await InitAsync();
        
        var name = galgame.Name;
        int? id;
        try
        {
            if (galgame.RssType != RssType.Bangumi) throw new Exception();
            id = Convert.ToInt32(galgame.Id ?? "");
        }
        catch (Exception)
        {
            id = await GetId(name!);
        }
        
        if (id == null) return null;
        var url = "https://api.bgm.tv/v0/subjects/" + id;
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;
        
        var jsonToken = JToken.Parse(await response.Content.ReadAsStringAsync());

        Galgame result = new();
        // rssType
        result.RssType = RssType.Bangumi;
        // id
        result.Id = jsonToken["id"]!.ToObject<string>()!;
        // name
        result.Name = jsonToken["name"]!.ToObject<string>()!;
        // description
        result.Description = jsonToken["summary"]!.ToObject<string>()!;
        // imageUrl
        result.ImageUrl = jsonToken["images"]!["large"]!.ToObject<string>()!;
        // rating
        result.Rating = jsonToken["rating"]!["score"]!.ToObject<float>();
        // tags
        var tags = jsonToken["tags"]!.ToObject<List<JToken>>()!;
        result.Tags.Value = new ObservableCollection<string>();
        tags.ForEach(tag => result.Tags.Value.Add(tag["name"]!.ToObject<string>()!));
        // developer
        var infoBox = jsonToken["infobox"]!.ToObject<List<JToken>>()!;
        var developerInfoBox = infoBox.Find(x => x["key"]!.ToObject<string>()!.Contains("开发"));
        developerInfoBox = developerInfoBox == null ? Galgame.DefaultString : developerInfoBox["value"] ?? Galgame.DefaultString;
        if (developerInfoBox.Type.ToString() == "Array")
        {
            IEnumerable<char> tmp = developerInfoBox.SelectMany(dev => dev["v"]!.ToString());
            developerInfoBox = string.Join(",", tmp);
        }
        result.Developer = developerInfoBox.ToString();
        if (result.Developer == Galgame.DefaultString)
        {
            var tmp = GetDeveloperFromTags(result);
            if (tmp != null)
                result.Developer = tmp;
        }
        return result;
    }

    private string? GetDeveloperFromTags(Galgame galgame)
    {
        string? result = null;
        foreach (var tag in galgame.Tags.Value!)
        {
            double maxSimilarity = 0;
            foreach(var dev in _developerList)
                if (IGalInfoPhraser.Similarity(dev, tag) > maxSimilarity)
                {
                    maxSimilarity = IGalInfoPhraser.Similarity(dev, tag);
                    result = dev;
                }

            if (result != null && maxSimilarity > 0.75) // magic number: 一个tag和开发商的相似度大于0.75就认为是开发商
                break;
        }
        return result;
    }

    public RssType GetPhraseType() => RssType.Bangumi;
}

public class BgmPhraserData : IGalInfoPhraserData
{
    public string? Token;

    public BgmPhraserData() { }
    
    public BgmPhraserData(string? token)
    {
        Token = token;
    }
}
