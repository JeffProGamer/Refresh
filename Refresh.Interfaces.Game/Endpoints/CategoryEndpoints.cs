using Bunkum.Core;
using Bunkum.Core.Endpoints;
using Bunkum.Listener.Protocol;
using Refresh.Common;
using Refresh.Core.Authentication.Permission;
using Refresh.Core.Configuration;
using Refresh.Core.RateLimits.EndpointRateLimiting;
using Refresh.Core.Services;
using Refresh.Core.Types.Categories;
using Refresh.Core.Types.Categories.Levels;
using Refresh.Core.Types.Data;
using Refresh.Database;
using Refresh.Database.Extensions;
using Refresh.Database.Models.Authentication;
using Refresh.Database.Models.Levels;
using Refresh.Database.Models.Users;
using Refresh.Database.Query;
using Refresh.Interfaces.Game.Endpoints.DataTypes.Response;
using Refresh.Interfaces.Game.Types.Categories;
using Refresh.Interfaces.Game.Types.Levels;
using Refresh.Interfaces.Game.Types.Lists;

namespace Refresh.Interfaces.Game.Endpoints;

public class CategoryEndpoints : EndpointGroup
{
    [GameEndpoint("searches", ContentType.Xml)]
    [GameEndpoint("genres", ContentType.Xml)]
    [MinimumRole(GameUserRole.Restricted)]
    [EndpointRateLimit(EndpointBucketId.GameGetListOfCategories)]
    public SerializedCategoryList GetModernCategories(RequestContext context, CategoryService categoryService, DataContext dataContext, GameServerConfig config)
    {
        (int skip, int count) = context.GetPageData();

        IEnumerable<SerializedCategory> levelCategories = categoryService.LevelCategories
            .Where(c => !c.Hidden)
            .Select(c => SerializedCategory.FromCategory(c, context, dataContext, 0, 1));
        
        IEnumerable<SerializedCategory> userCategories = config.PermitShowingOnlineUsers ? categoryService.UserCategories
            .Where(c => !c.Hidden)
            .Select(c => SerializedCategory.FromCategory(c, context, dataContext, 0, 1)) : [];

        IEnumerable<SerializedCategory> allCategories = [];
        allCategories = allCategories.Concat(levelCategories).Concat(userCategories);
        
        DatabaseList<SerializedCategory> paginatedCategories = new(allCategories, skip, count);

        SearchLevelCategory searchCategory = (SearchLevelCategory)categoryService.LevelCategories
            .First(c => c is SearchLevelCategory);
        
        return new SerializedCategoryList
        (
            paginatedCategories.Items, 
            searchCategory, 
            paginatedCategories.TotalItems
        );
    }

    [GameEndpoint("searches/levels/{apiRoute}", ContentType.Xml)]
    [NullStatusCode(NotFound)]
    [MinimumRole(GameUserRole.Restricted)]
    [EndpointRateLimit(EndpointBucketId.GameGetListOfLevels)]
    public SerializedCategoryResultsList? GetLevelsFromCategory(RequestContext context, CategoryService categories, GameUser user, 
        string apiRoute, DataContext dataContext, RemoteArchiveSearchService remoteArchiveSearch)
    {
        (int skip, int count) = context.GetPageData();
        LevelFilterSettings filters = LevelFilterSettings.FromGameRequest(context, dataContext.Game, true);

        DatabaseResultList? results = categories.LevelCategories
            .FirstOrDefault(c => c.ApiRoute.StartsWith(apiRoute))?
            .Fetch(context, skip, count, dataContext, filters, user);
        
        if (results == null) return null;

        int localLevelCount = results.Levels?.Items.Count() ?? 0;
        if (string.Equals(apiRoute, "search", StringComparison.OrdinalIgnoreCase) &&
            results.Levels != null && localLevelCount < count && count > 0)
        {
            string? query = context.QueryString["query"] ?? context.QueryString["textFilter"];
            if (!string.IsNullOrWhiteSpace(query))
            {
                try
                {
                    int remoteCount = Math.Min(100, count + localLevelCount);
                    RemoteArchiveSearchPage? remotePage = remoteArchiveSearch.Search(query, skip, remoteCount, filters);
                    if (remotePage != null)
                    {
                        List<GameLevel> levels = results.Levels.Items.ToList();
                        HashSet<string> hashes = levels.Select(level => level.RootResource).ToHashSet(StringComparer.Ordinal);

                        foreach (RemoteArchiveSearchLevel remoteLevel in remotePage.Data ?? [])
                        {
                            GameLevel? candidate = remoteLevel.ToGameLevel();
                            if (candidate == null ||
                                !filters.GameVersion.CanPlay(candidate) ||
                                !new[] { candidate }.FilterByLevelFilterSettings(user, filters).Any())
                            {
                                continue;
                            }

                            GameLevel imported = dataContext.Database.AddArchivedLevel(candidate);
                            if (hashes.Add(imported.RootResource))
                                levels.Add(imported);

                            if (levels.Count >= count) break;
                        }

                        if (levels.Count > localLevelCount)
                        {
                            int nextPage = Math.Max(results.NextPageIndexMax, remotePage.ListInfo?.NextPageIndex ?? 0);
                            int total = Math.Max(results.TotalItemsSum, remotePage.ListInfo?.TotalItems ?? 0);
                            return new SerializedCategoryResultsList(
                                GameMinimalLevelResponse.FromOldList(levels, dataContext),
                                GameUserResponse.FromOldList(results.Users?.Items.ToArray() ?? [], dataContext),
                                nextPage,
                                total);
                        }
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
                                                   System.Text.Json.JsonException or InvalidDataException)
                {
                    context.Logger.LogWarning(RefreshContext.Database,
                        $"Remote archive search failed; returning local search results. {exception.Message}");
                }
            }
        }
        
        return new SerializedCategoryResultsList
        (
            GameMinimalLevelResponse.FromOldList(results.Levels?.Items.ToArray() ?? [], dataContext),
            GameUserResponse.FromOldList(results.Users?.Items.ToArray() ?? [], dataContext),
            results.NextPageIndexMax,
            results.TotalItemsSum
        );
    }

    [GameEndpoint("searches/users/{apiRoute}", ContentType.Xml)]
    [NullStatusCode(NotFound)]
    [MinimumRole(GameUserRole.Restricted)]
    [EndpointRateLimit(EndpointBucketId.GameGetListOfUsers)]
    public SerializedCategoryResultsList? GetUsersFromCategory(RequestContext context, CategoryService categories, GameUser user, 
        string apiRoute, DataContext dataContext, GameServerConfig config)
    {
        if (!config.PermitShowingOnlineUsers) return null;

        (int skip, int count) = context.GetPageData();

        DatabaseList<GameUser>? users = categories.UserCategories
            .FirstOrDefault(c => c.ApiRoute.StartsWith(apiRoute))?
            .Fetch(context, skip, count, dataContext, LevelFilterSettings.FromGameRequest(context, dataContext.Game, true), user)?
            .Users;
        
        if (users == null) return null;
        
        // No user categories which return levels or playlists yet.
        return new SerializedCategoryResultsList
        (
            users.Items.ToArray().Select(u => GameUserResponse.FromOld(u, dataContext))!,
            users.TotalItems,
            users.NextPageIndex
        );
    }
}