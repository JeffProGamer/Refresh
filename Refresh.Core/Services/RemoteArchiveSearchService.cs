using System.Text.Json;
using System.Text.Json.Serialization;
using Bunkum.Core.Services;
using Refresh.Common.Verification;
using Refresh.Database.Models.Authentication;
using Refresh.Database.Models.Levels;
using Refresh.Database.Models.Statistics;
using Refresh.Database.Query;
using NotEnoughLogs;

namespace Refresh.Core.Services;

public sealed class RemoteArchiveSearchService : Service
{
    private static readonly HttpClient DefaultClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Uri? _apiBaseUri;
    private readonly HttpClient _client;

    public RemoteArchiveSearchService(Logger logger, string? apiBaseUrl, HttpClient? client = null) : base(logger)
    {
        if (apiBaseUrl != null)
            this._apiBaseUri = new Uri(apiBaseUrl, UriKind.Absolute);
        this._client = client ?? DefaultClient;
    }

    public RemoteArchiveSearchPage? Search(string query, int skip, int count, LevelFilterSettings filters)
    {
        if (this._apiBaseUri == null || count <= 0) return null;

        List<string> parameters =
        [
            $"query={Uri.EscapeDataString(query)}",
            $"skip={Math.Max(1L, (long)skip + 1)}",
            $"count={Math.Clamp(count, 1, 100)}",
        ];

        foreach (string game in GetGameFilters(filters))
            parameters.Add($"game={Uri.EscapeDataString(game)}");

        if (filters.Players > 0)
            parameters.Add($"players={filters.Players}");

        Uri requestUri = new(new Uri(this._apiBaseUri, "levels/search"),
            $"?{string.Join('&', parameters)}");
        using HttpResponseMessage response = this._client.GetAsync(requestUri).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        RemoteArchiveSearchPage? page = System.Text.Json.JsonSerializer.Deserialize<RemoteArchiveSearchPage>(json, JsonOptions);
        if (page == null || !page.Success || page.Data == null)
            throw new InvalidDataException("Remote archive search returned an invalid response.");

        return page;
    }

    private static IEnumerable<string> GetGameFilters(LevelFilterSettings filters)
    {
        IEnumerable<(TokenGame Game, bool Enabled)> available = filters.GameVersion switch
        {
            TokenGame.LittleBigPlanet1 =>
                [(TokenGame.LittleBigPlanet1, filters.DisplayLbp1)],
            TokenGame.LittleBigPlanet2 =>
                [(TokenGame.LittleBigPlanet1, filters.DisplayLbp1), (TokenGame.LittleBigPlanet2, filters.DisplayLbp2)],
            TokenGame.LittleBigPlanet3 =>
                [(TokenGame.LittleBigPlanet1, filters.DisplayLbp1), (TokenGame.LittleBigPlanet2, filters.DisplayLbp2), (TokenGame.LittleBigPlanet3, filters.DisplayLbp3)],
            TokenGame.LittleBigPlanetVita =>
                [(TokenGame.LittleBigPlanetVita, filters.DisplayVita)],
            TokenGame.LittleBigPlanetPSP =>
                [(TokenGame.LittleBigPlanetPSP, filters.DisplayPSP)],
            TokenGame.BetaBuild =>
                [(TokenGame.BetaBuild, filters.DisplayBeta)],
            TokenGame.Website =>
                [
                    (TokenGame.LittleBigPlanet1, filters.DisplayLbp1),
                    (TokenGame.LittleBigPlanet2, filters.DisplayLbp2),
                    (TokenGame.LittleBigPlanet3, filters.DisplayLbp3),
                    (TokenGame.LittleBigPlanetVita, filters.DisplayVita),
                    (TokenGame.LittleBigPlanetPSP, filters.DisplayPSP),
                    (TokenGame.BetaBuild, filters.DisplayBeta),
                ],
            _ => [],
        };

        return available.Where(item => item.Enabled).Select(item => item.Game switch
        {
            TokenGame.LittleBigPlanet1 => "lbp1",
            TokenGame.LittleBigPlanet2 => "lbp2",
            TokenGame.LittleBigPlanet3 => "lbp3",
            TokenGame.LittleBigPlanetVita => "vita",
            TokenGame.LittleBigPlanetPSP => "psp",
            TokenGame.BetaBuild => "beta",
            _ => throw new ArgumentOutOfRangeException(nameof(item.Game)),
        });
    }
}

public sealed class RemoteArchiveSearchPage
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("listInfo")] public RemoteArchiveSearchListInfo? ListInfo { get; set; }
    [JsonPropertyName("data")] public List<RemoteArchiveSearchLevel>? Data { get; set; }
}

public sealed class RemoteArchiveSearchListInfo
{
    [JsonPropertyName("nextPageIndex")] public int NextPageIndex { get; set; }
    [JsonPropertyName("totalItems")] public int TotalItems { get; set; }
}

public sealed class RemoteArchiveSearchLevel
{
    [JsonPropertyName("rootLevelHash")] public string? RootLevelHash { get; set; }
    [JsonPropertyName("iconHash")] public string? IconHash { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("gameVersion")] public int GameVersion { get; set; }
    [JsonPropertyName("location")] public RemoteArchiveSearchLocation? Location { get; set; }
    [JsonPropertyName("publishDate")] public DateTimeOffset? PublishDate { get; set; }
    [JsonPropertyName("updateDate")] public DateTimeOffset? UpdateDate { get; set; }
    [JsonPropertyName("minPlayers")] public int MinPlayers { get; set; } = 1;
    [JsonPropertyName("maxPlayers")] public int MaxPlayers { get; set; } = 4;
    [JsonPropertyName("enforceMinMaxPlayers")] public bool EnforceMinMaxPlayers { get; set; }
    [JsonPropertyName("sameScreenGame")] public bool SameScreenGame { get; set; }
    [JsonPropertyName("isAdventure")] public bool IsAdventure { get; set; }
    [JsonPropertyName("isModded")] public bool IsModded { get; set; }
    [JsonPropertyName("isReUpload")] public bool IsReUpload { get; set; }
    [JsonPropertyName("originalPublisher")] public string? OriginalPublisher { get; set; }
    [JsonPropertyName("publisher")] public RemoteArchiveSearchPublisher? Publisher { get; set; }
    [JsonPropertyName("levelType")] public int LevelType { get; set; }
    [JsonPropertyName("isLocked")] public bool IsLocked { get; set; }
    [JsonPropertyName("isSubLevel")] public bool IsSubLevel { get; set; }
    [JsonPropertyName("isCopyable")] public bool IsCopyable { get; set; }
    [JsonPropertyName("score")] public float Score { get; set; }
    [JsonPropertyName("yayRatings")] public int YayRatings { get; set; }
    [JsonPropertyName("booRatings")] public int BooRatings { get; set; }
    [JsonPropertyName("hearts")] public int Hearts { get; set; }
    [JsonPropertyName("photosTaken")] public int PhotosTaken { get; set; }
    [JsonPropertyName("levelComments")] public int LevelComments { get; set; }
    [JsonPropertyName("reviews")] public int Reviews { get; set; }
    [JsonPropertyName("uniquePlays")] public int UniquePlays { get; set; }
    [JsonPropertyName("dateTeamPicked")] public DateTimeOffset? DateTeamPicked { get; set; }

    public GameLevel? ToGameLevel()
    {
        if (this.RootLevelHash == null || !CommonPatterns.Sha1Regex().IsMatch(this.RootLevelHash) ||
            string.IsNullOrWhiteSpace(this.Title) ||
            !Enum.IsDefined(typeof(TokenGame), this.GameVersion) ||
            this.LevelType < (int)GameLevelType.Normal ||
            this.LevelType > (int)GameLevelType.Cutscene ||
            this.MinPlayers < 0 || this.MaxPlayers < this.MinPlayers ||
            !float.IsFinite(this.Score))
        {
            return null;
        }

        string originalPublisher = this.OriginalPublisher ?? this.Publisher?.Username ?? "";
        string title = this.Title.Length > 64 ? this.Title[..64] : this.Title;
        string description = this.Description ?? "";
        if (description.Length > 512) description = description[..512];

        GameLevelStatistics statistics = new()
        {
            FavouriteCount = Math.Max(0, this.Hearts),
            UniquePlayCount = Math.Max(0, this.UniquePlays),
            YayCount = Math.Max(0, this.YayRatings),
            BooCount = Math.Max(0, this.BooRatings),
            PhotoInLevelCount = Math.Max(0, this.PhotosTaken),
            CommentCount = Math.Max(0, this.LevelComments),
            ReviewCount = Math.Max(0, this.Reviews),
        };

        return new GameLevel
        {
            IsAdventure = this.IsAdventure,
            Title = title,
            IconHash = this.IconHash != null && CommonPatterns.Sha1Regex().IsMatch(this.IconHash) ? this.IconHash : "0",
            Description = description,
            LocationX = this.Location?.X ?? 0,
            LocationY = this.Location?.Y ?? 0,
            RootResource = this.RootLevelHash,
            PublishDate = this.PublishDate ?? DateTimeOffset.UtcNow,
            UpdateDate = this.UpdateDate ?? this.PublishDate ?? DateTimeOffset.UtcNow,
            MinPlayers = this.MinPlayers,
            MaxPlayers = this.MaxPlayers,
            EnforceMinMaxPlayers = this.EnforceMinMaxPlayers,
            SameScreenGame = this.SameScreenGame,
            DateTeamPicked = this.DateTeamPicked,
            IsModded = this.IsModded,
            GameVersion = (TokenGame)this.GameVersion,
            LevelType = (GameLevelType)this.LevelType,
            IsLocked = this.IsLocked,
            IsSubLevel = this.IsSubLevel,
            IsCopyable = this.IsCopyable,
            CoolRating = this.Score,
            IsReUpload = this.IsReUpload,
            OriginalPublisher = originalPublisher,
            Statistics = statistics,
        };
    }
}

public sealed class RemoteArchiveSearchLocation
{
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
}

public sealed class RemoteArchiveSearchPublisher
{
    [JsonPropertyName("username")] public string? Username { get; set; }
}
