﻿using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using GalgameManager.Contracts.Phrase;
using GalgameManager.Enums;
using GalgameManager.Models;
using GalgameManager.Services;
using Newtonsoft.Json.Linq;
using SharpCompress;
using VndbSharp;
using VndbSharp.Models;
using VndbSharp.Models.Errors;
using VndbSharp.Models.VisualNovel;

namespace GalgameManager.Helpers.Phrase;

[SuppressMessage("ReSharper", "EnforceIfStatementBraces")]
public class VndbPhraser : IGalInfoPhraser
{
    private readonly Vndb _vndb;
    private readonly Dictionary<int, JToken> _tagDb = new();
    private bool _init;
    private const string TagDbFile = @"Assets\Data\vndb-tags-2023-04-15.json";
    
    public VndbPhraser()
    {
        _vndb = new Vndb(true).WithClientDetails("GalgameManager", "1.0-dev").WithFlagsCheck(true);
    }

    private async Task Init()
    {
        _init = true;
        var assembly = Assembly.GetExecutingAssembly();
        var file = Path.Combine(Path.GetDirectoryName(assembly.Location)!, TagDbFile);
        if (!File.Exists(file)) return;

        var json = JToken.Parse(await File.ReadAllTextAsync(file));
        var tags = json.ToObject<List<JToken>>();
        tags!.ForEach(tag => _tagDb.Add(int.Parse(tag["id"]!.ToString()), tag));
    }
    
    public async Task<Galgame?> GetGalgameInfo(Galgame galgame)
    {
        if (!_init) await Init();
        var result = new Galgame();
        try
        {
            VndbResponse<VisualNovel> visualNovels;
            try
            {
                if(galgame.RssType != RssType.Vndb) throw new Exception();
                var idString = galgame.Id;
                if(!string.IsNullOrEmpty(idString) && idString[0]=='v')
                    idString = idString[1..];
                var id = Convert.ToUInt32(idString);
                visualNovels = await _vndb.GetVisualNovelAsync(VndbFilters.Id.Equals(id), VndbFlags.FullVisualNovel);
            }
            catch (Exception)
            {
                visualNovels = await _vndb.GetVisualNovelAsync(VndbFilters.Search.Fuzzy(galgame.Name), VndbFlags.FullVisualNovel);
            }
            
            if (visualNovels == null || visualNovels.Count == 0)
            {
                var error = _vndb.GetLastError();
                if (error is not { Type: ErrorType.Throttled }) return null;
                await Task.Delay(60 * 1000); // 1 minute
                visualNovels = await _vndb.GetVisualNovelAsync(VndbFilters.Search.Fuzzy(galgame.Name), VndbFlags.FullVisualNovel);
                if (visualNovels == null) return null;
            }
            var rssItem = visualNovels.Items[0];
            result.Name = rssItem.OriginalName;
            result.Description = rssItem.Description;
            result.RssType = GetPhraseType();
            result.Id = rssItem.Id.ToString();
            result.Rating = (float)rssItem.Rating;
            result.ExpectedPlayTime = rssItem.Length.ToString() ?? Galgame.DefaultString;
            result.ImageUrl = rssItem.Image;
            //Tags
            result.Tags.Value = new ObservableCollection<string>();
            var tmpTags = new List<TagMetadata>(rssItem.Tags).OrderByDescending(t => t.Score);
            tmpTags.ForEach(tag =>
            {
                if (_tagDb.TryGetValue((int)tag.Id, out var tagInfo))
                    result.Tags.Value.Add(tagInfo["name"]!.ToString());
            });
        }
        catch (Exception)
        {
            return null;
        }
        return result;
    }

    public RssType GetPhraseType() => RssType.Vndb;
}
