using ICSharpCode.SharpZipLib.GZip;
using IMDBScraper.Source;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace IMDBScraper
{
    public class IMDBReader
    {
        #region Const

        public bool ShutdownRequested { get; set; }

        public const int MaxActiveFetches = 1;
        public const int TargetFetchesPerMinute = 500;
        public static readonly string CacheFolder = @"C:\temp\IMDBScraper\Cache";

        static readonly Regex url_ShowIdRecognizer = new Regex(@"tt(?<id>[0-9]+)", RegexOptions.Compiled);
        static readonly Regex url_PersonIdRecognizer = new Regex(@"nm(?<id>[0-9]+)", RegexOptions.Compiled);
        static readonly Regex url_RefRecognizer = new Regex(@"ref_=[^&]*&?", RegexOptions.Compiled);
        static readonly Regex xml_badTagRecognizer = new Regex(@"[ \t]*\<input[^\n]*", RegexOptions.Compiled | RegexOptions.Singleline);
        static readonly Regex xml_divBlockRecognizer = new Regex(@"(?<div>\<\s*div)|(?<undiv>\</\s*div\s*\>)", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);
        static readonly Regex xml_imgAltRecognizer = new Regex(@"\<img alt=.*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex xml_imgFixer = new Regex(@"(?<=\<img[^\>]*[^\/])\>", RegexOptions.Compiled | RegexOptions.Singleline);
        static readonly Regex height_metersRecognizer = new Regex(@"\((?<meters>[^ ]+) m\)", RegexOptions.Compiled);

        Dictionary<string, ShowType> showTypeMap = new Dictionary<string, ShowType>
        {
            { "TVSeries", ShowType.TVSeries },
            { "Movie", ShowType.Movie },
            { "VideoGame", ShowType.VideoGame },
            { "PodcastSeries", ShowType.Podcast },
            { "MusicVideoObject", ShowType.MusicVideo },
            { "TVEpisode", ShowType.Episode },
            { "tvMiniSeries", ShowType.TVMiniSeries },
            { "tvSeries", ShowType.TVSeries },
            { "tvEpisode", ShowType.Episode },
            { "movie", ShowType.Movie },
            { "musicVideo", ShowType.MusicVideo },
            { "short", ShowType.Short },
            { "tvMovie", ShowType.TVMovie },
            { "video", ShowType.Short },
            { "podcastSeries", ShowType.Podcast },
            { "tvSpecial", ShowType.TVSpecial },
            { "videoGame", ShowType.VideoGame },
            { "tvShort", ShowType.TVSpecial }
        };

        Dictionary<string, Rating> contentRatingMap = new Dictionary<string, Rating>
        {
            { "Not Rated", Rating.Unrated },
            { "Unrated", Rating.Unrated },
            { "Open", Rating.Unrated },
            { "MA-13", Rating.Unrated },
            { "G", Rating.G },
            { "PG", Rating.PG },
            { "GP", Rating.PG },
            { "PG-13", Rating.PG13 },
            { "M/PG", Rating.PG13 },
            { "R", Rating.R },
            { "18", Rating.R },
            { "NC-17", Rating.NC17 },
            { "X", Rating.X },
            { "AO", Rating.X },
            { "TV-Y", Rating.TVY },
            { "TV-Y7", Rating.TVY7 },
            { "TV-Y7-FV", Rating.TVY7FV },
            { "TV-G", Rating.TVG },
            { "TV-PG", Rating.TVPG },
            { "TV-13", Rating.TV14 },
            { "TV-14", Rating.TV14 },
            { "TV-MA", Rating.TVMA },
            { "16+", Rating.TVMA },
            { "MA-17", Rating.TVMA },
            { "13+", Rating.PG13 },
            { "18+", Rating.R },
            { "E", Rating.VG_E },
            { "K-A", Rating.VG_E },
            { "CE", Rating.VG_E },
            { "EC", Rating.VG_E },
            { "GA", Rating.VG_E },
            { "E10+", Rating.VG_E10 },
            { "12", Rating.VG_T },
            { "T", Rating.VG_T },
            { "M", Rating.VG_M },
            { "Approved", Rating.Approved },
            { "Passed", Rating.Approved }
        };

        #endregion

        HttpClient httpClient;
        SHA512 hashCodec = SHA512.Create();
        int activeFetches = 0;
        Queue<TaskCompletionSource> fetchQueue = new Queue<TaskCompletionSource>();
        Stopwatch stopwatch = new Stopwatch();

        public IMDBReader()
        {
            if (!Directory.Exists(CacheFolder))
                Directory.CreateDirectory(CacheFolder);

            httpClient = new HttpClient();
            stopwatch.Start();
        }

        #region Procedures

        public async Task ScrapeImage(string url, string localPath)
        {
            if (!File.Exists(localPath))
            {
                try
                {
                    await download(url, localPath);
                }
                catch (HttpRequestException)
                {
                }
            }
        }

        const string SearchPage_EntryPath = "//div[@class='lister-item mode-simple']";
        const string SearchPage_TitlePath = ".//span[@class='lister-item-header']/span/a";
        const string SearchPage_TinyThumbPath = ".//div[@class='lister-item-image']/a/img/@loadlate";
        public async Task<List<ShowHeader>> ScrapeHeadersFromSearchPage(DateTime startDate, DateTime endDate, int startIndex)
        {
            List<ShowHeader> headers = new List<ShowHeader>();

            var fetching = fetch(titleListingUrl(startDate, endDate, startIndex));

            var xml = asXml(cleanTitleSearchPage(await fetching));
            if (xml == null)
                return headers;

            var entries = xml.SelectNodes(SearchPage_EntryPath);
            if (entries == null || entries.Count == 0)
                return headers;

            foreach (var entry in entries.OfType<XmlNode>())
            {
                var titleElement = entry.SelectSingleNode(SearchPage_TitlePath);
                var title = titleElement?.InnerText;
                var showUrl = stripRef(titleElement?.Attributes?["href"]?.InnerText ?? "");

                if (showUrl == null) continue;

                var id = long.Parse(url_ShowIdRecognizer.Match(showUrl).Groups["id"].Value);
                var tinyThumbUrl = entry.SelectSingleNode(SearchPage_TinyThumbPath)?.InnerText ?? throw new Exception();
                var posterUrl = stripImageModifiers(tinyThumbUrl);

                // This happens when there is no poster and IMDB replaces it with a placeholder.
                if (posterUrl == tinyThumbUrl)
                    posterUrl = null;

                headers.Add(new ShowHeader
                {
                    id = id,
                    title = title,
                    url = showUrl,
                    posterUrl = posterUrl
                });
            }

            return headers;
        }

        #region Scrape Show Helpers
        ShowType Show_TranslateShowType(string? type)
            => type == null ? ShowType.Unknown : showTypeMap[type];

        DateTime? Show_TranslateSourceReleaseDate(string? datePublished)
            => datePublished != null ? DateTime.Parse(datePublished) : null;

        Rating Show_TranslateContentRating(string? contentRating)
        {
            return contentRating == null ? Rating.Unrated : contentRatingMap[contentRating];
        }

        RoleType Show_TranslatePrincipalCreditCategoryId(string? id)
        {
            switch(id)
            {
                case "writer":
                    return RoleType.Writer;
                case "cast":
                    return RoleType.Actor;
                case "director":
                    return RoleType.Director;
                default:
                    return RoleType.Unspecified;
            }
        }

        List<long> Show_TranslatePrincipalCredits(Source.BigShow show, long showId, TheStarsDB db)
        {
            var output = new List<long>();

            var data = show.props?.pageProps?.aboveTheFoldData?.principalCredits;

            if (data == null)
                return output;

            foreach(var category in data)
            {
                var type = Show_TranslatePrincipalCreditCategoryId(category.category?.id);

                if (type == RoleType.Unspecified) continue;

                category.credits?.ForEach(person =>
                {
                    if (person.name?.id == null) return;

                    output.Add(db.GetRole(type, showId, long.Parse(url_PersonIdRecognizer.Match(person.name.id).Groups["id"].Value)).id);
                });
            }

            return output;
        }

        TimeSpan? Show_TranslateDuration(string? duration)
        {
            if (duration == null) return null;

            var match = Show_DurationRecognizer.Match(duration);
            var hours = 0;
            var minutes = 0;

            if (match.Groups["hours"].Success)
                hours = int.Parse(match.Groups["hours"].Value);

            if (match.Groups["minutes"].Success)
                minutes = int.Parse(match.Groups["minutes"].Value);

            return TimeSpan.FromMinutes(minutes + 60 * hours);
        }

        #endregion

        static Regex Show_DurationRecognizer = new Regex(@"PT((?<hours>[0-9]+)H)?((?<minutes>[0-9]+)M)?");
        public async Task ScrapeShowDetails(Show show, TheStarsDB db, bool force = false)
        {
            try
            {
                string fetchedResult = "";
                var url = showPageUrl(show.id);

                bool gotBig = show.principalCredits != null;
                bool gotSmall = gotBig || show.releaseDate != null;
                
                if (force || !gotBig || !gotSmall)
                    fetchedResult = await cachedFetch(url);

                if (force || !gotSmall)
                {
                    var source = Json.Deserialize<Source.Show>(cleanTitleDetailsPage(fetchedResult));

                    if (source != null)
                    {
                        show.releaseDate = Show_TranslateSourceReleaseDate(source.datePublished);
                        // endDate
                        show.duration = Show_TranslateDuration(source.duration);
                        show.contentRating = Show_TranslateContentRating(source.contentRating);
                        show.qualityRating = source.aggregateRating?.ratingValue;
                        show.votes = source.aggregateRating?.ratingCount;
                        show.genre = source.genre;
                        show.description = source.description;
                        show.trailerUrl = source.trailer?.url;
                        show.trailerThumbnailUrl = source.trailer?.thumbnailUrl;
                        // trailerThumbnailLocalPath
                        // credits
                        // episodes

                        gotSmall = true;
                    }
                }

                if (force || !gotBig)
                {
                    var bigsource = Json.Deserialize<Source.BigShow>(cleanSeriesDetailsPage(fetchedResult));
                    var bigMain = bigsource?.props?.pageProps?.mainColumnData;

                    if (bigMain != null && bigsource != null)
                    {
                        show.type = Show_TranslateShowType(bigsource.props?.pageProps?.aboveTheFoldData?.titleType?.id);
                        show.principalCredits = Show_TranslatePrincipalCredits(bigsource, show.id, db);
                        show.totalEpisodes = bigMain.episodes?.totalEpisodes?.total;
                        show.totalSeasons = bigMain.episodes?.seasons?.Count;
                        show.isAdult = bigMain.isAdult;
                        show.triviaMarkdown = bigMain.trivia?.edges?.FirstOrDefault()?.node?.text?.plaidHtml;
                        show.goofMarkdown = bigMain.goofs?.edges?.FirstOrDefault()?.node?.text?.plaidHtml;
                        show.productionBudget = bigMain.productionBudget?.budget;
                        show.worldwideGross = bigMain.worldwideGross?.total;
                        show.lifetimeGross = bigMain.lifetimeGross?.total;
                        show.openingWeekendGross = bigMain.openingWeekendGross?.gross?.total;
                        show.openingWeekendEnd = bigMain.openingWeekendGross?.weekendEndDate;
                        show.spokenLanguages = bigMain.spokenLanguages?.spokenLanguages;
                        show.countriesOfOrigin = bigMain.countriesOfOrigin?.countries;

                        gotBig = true;
                    }
                }

                if (gotSmall && gotBig && show.type != ShowType.TVSeries && show.type != ShowType.TVMiniSeries)
                    discardCache(url);
            }
            catch (Exception ex)
            {
                lock (Console.Out)
                {
                    ConsoleWriteLine(ex.Message);
                    ConsoleWriteLine(show.url);
                    ConsoleWriteLine();
                }
            }
        }

        const string CreditsPage_DirectorPath = "/div/h4[@id='director']/following-sibling::table[1]/tbody/tr/td[@class='name']/a/@href";
        const string CreditsPage_WriterNamePath = "/div/h4[@id='writer']/following-sibling::table[1]/tbody/tr/td[@class='name']/a/@href";
        const string CreditsPage_WriterRolePath = "./../../following-sibling::td[@class='credit']";
        const string CreditsPage_ActorPath = "/div/table[@class='cast_list']/tr[@class='even' or @class='odd']/td[2]/a/@href";
        const string CreditsPage_ActorRolePath = "./../../following-sibling::td[@class='character']/a";
        const string CreditsPage_ProducerPath = "/div/h4[@id='producer']/following-sibling::table[1]/tbody/tr/td[@class='name']/a/@href";
        const string CreditsPage_ProducerRolePath = "./../../following-sibling::td[@class='credit']";
        public async Task ScrapeCredits(Show show, TheStarsDB db, int maxActorsPerShow = 0)
        {
            var castUrl = fullCastUrl(show.id);

            try
            {
                var credits = new List<long>();

                var xml = asXml(cleanCreditsPage(await cachedFetch(castUrl)));

                if (xml == null)
                {
                    return;
                }

                bool hasDirector = false;
                bool hasWriter = false;
                bool hasActor = false;
                bool hasProducer = false;

                foreach (var directorElement in xml.SelectNodes(CreditsPage_DirectorPath).NullAsEmpty().OfType<XmlNode>())
                {
                    var id = long.Parse(url_PersonIdRecognizer.Match(directorElement.InnerText).Groups["id"].Value);
                    var role = db.GetRole(RoleType.Director, show.id, id);
                    credits.Add(role.id);
                    hasDirector = true;
                }

                foreach (var writerElement in xml.SelectNodes(CreditsPage_WriterNamePath).NullAsEmpty().OfType<XmlNode>())
                {
                    var id = long.Parse(url_PersonIdRecognizer.Match(writerElement.InnerText).Groups["id"].Value);
                    var role = db.GetRole(RoleType.Writer, show.id, id);
                    credits.Add(role.id);
                    hasWriter = true;

                    role.name = writerElement.SelectSingleNode(CreditsPage_WriterRolePath)?.InnerText.Trim();
                }

                int actors = 0;
                foreach (var actorElement in xml.SelectNodes(CreditsPage_ActorPath).NullAsEmpty().OfType<XmlNode>())
                {
                    if (maxActorsPerShow != 0 && actors >= maxActorsPerShow)
                        break;

                    var id = long.Parse(url_PersonIdRecognizer.Match(actorElement.InnerText).Groups["id"].Value);
                    var role = db.GetRole(RoleType.Actor, show.id, id);
                    credits.Add(role.id);
                    hasActor = true;
                    actors++;

                    role.name = actorElement.SelectSingleNode(CreditsPage_ActorRolePath)?.InnerText.Trim();
                }

                foreach (var producerElement in xml.SelectNodes(CreditsPage_ProducerPath).NullAsEmpty().OfType<XmlNode>())
                {
                    var id = long.Parse(url_PersonIdRecognizer.Match(producerElement.InnerText).Groups["id"].Value);
                    var role = db.GetRole(RoleType.Producer, show.id, id);
                    credits.Add(role.id);
                    hasProducer = true;

                    role.name = producerElement.SelectSingleNode(CreditsPage_ProducerRolePath)?.InnerText.Trim();
                }

                show.credits = credits;

                if (hasDirector && hasWriter && hasActor && hasProducer && show.principalCredits?.All(p => show.credits.Contains(p)) == true)
                {
                    discardCache(castUrl);
                    return;
                }
            }
            catch (Exception ex)
            {
                lock (Console.Out)
                {
                    ConsoleWriteLine(ex.Message);
                    ConsoleWriteLine(fullCastUrl(show.id));
                }
            }

        }

        string? ExtractMarkdownText(MarkdownText? text)
        {
            return text?.plainText == null ? text?.plaidHtml : text?.plainText;
        }

        float? ExtractHeight(MarkdownText? text)
        {
            var heightText = ExtractMarkdownText(text);

            if (heightText == null)
                return null;

            return float.Parse(height_metersRecognizer.Match(heightText).Groups["meters"].Value) * 100;
        }

        public async Task<Person?> ScrapePersonDetails(long id)
        {
            try
            {
                var source = Json.Deserialize<BigPerson>(cleanPersonDetailsPage(await cachedFetch(personPageUrl(id))));

                var aboveTheFold = source?.props?.pageProps?.aboveTheFold;
                var main = source?.props?.pageProps?.mainColumnData;

                var output = new Person
                {
                    id = id,
                    name = aboveTheFold?.nameText?.text,
                    url = personPageUrl(id),
                    imageUrl = aboveTheFold?.primaryImage?.url,
                    // imageLocalPath
                    bio = ExtractMarkdownText(aboveTheFold?.bio?.text),
                    birthday = aboveTheFold?.birthDate?.date,
                    deathDate = aboveTheFold?.deathDate?.date,
                    isDead = aboveTheFold?.deathStatus == null ? null : (aboveTheFold.deathStatus == DeathStatus.DEAD || aboveTheFold.deathStatus == DeathStatus.PRESUMED_DEAD),
                    trivia = ExtractMarkdownText(main?.trivia?.edges?.FirstOrDefault()?.node?.displayableArticle?.body),
                    quote = ExtractMarkdownText(main?.quotes?.edges?.FirstOrDefault()?.node?.displayableArticle?.body),
                    heightCm = ExtractHeight(main?.height?.displayableProperty?.value)
                };

                return output;
            }
            catch (Exception ex)
            {
                lock (Console.Out)
                {
                    ConsoleWriteLine(ex.Message);
                    ConsoleWriteLine(personPageUrl(id));
                    ConsoleWriteLine();
                }
            }

            return null;
        }

        const string EpisodeListingPage_EpisodePath = "/div/div";
        const string EpisodeListingPage_TitlePath = "./div[@class='image']/a/@title";
        const string EpisodeListingPage_UrlPath = "./div[@class='image']/a/@href";
        const string EpisodeListingPage_PosterPath = "./div[@class='image']/a/div/img/@src";
        const string EpisodeListingPage_EpisodeNumberPath = "./div[@class='info']/meta[@itemprop='episodeNumber']/@content";
        public async Task<List<ShowHeader>> ScrapeEpisodesFromSeries(Show show)
        {
            List<ShowHeader> episodeHeaders = new List<ShowHeader>();

            try
            {
                if (show.totalSeasons == null)
                {
                    var source = Json.Deserialize<Source.BigShow>(cleanSeriesDetailsPage(await cachedFetch(showPageUrl(show.id))));
                    show.totalEpisodes = source?.props?.pageProps?.mainColumnData?.episodes?.totalEpisodes?.total;
                    show.totalSeasons = source?.props?.pageProps?.mainColumnData?.episodes?.seasons?.Count;
                }

                if (show.totalEpisodes == null || show.episodes == null || show.episodes.Count == show.totalEpisodes)
                    return new List<ShowHeader>();

                if (show.totalSeasons == null)
                    throw new Exception("Could not read totalSeasons");

                for (int season = 1; season <= show.totalSeasons; season++)
                {
                    var xml = asXml(cleanEpisodeListingPage(await fetch(episodeListingUrl(show.id, season))));
                    var episodeNodes = xml?.SelectNodes(EpisodeListingPage_EpisodePath)?.OfType<XmlNode>();

                    if (episodeNodes != null)
                        foreach (var node in episodeNodes)
                        {
                            var url = stripRef(node.SelectSingleNode(EpisodeListingPage_UrlPath)?.InnerText);
                            var episodeNode = node.SelectSingleNode(EpisodeListingPage_EpisodeNumberPath);

                            if(url == null) continue;

                            var header = new ShowHeader
                            {
                                url = url,
                                id = long.Parse(url_ShowIdRecognizer.Match(url).Groups["id"].Value),
                                title = node.SelectSingleNode(EpisodeListingPage_TitlePath)?.InnerText,
                                posterUrl = stripImageModifiers(node.SelectSingleNode(EpisodeListingPage_PosterPath)?.InnerText),
                                season = season,
                                episode = episodeNode == null ? null : int.Parse(episodeNode.InnerText)
                            };

                            episodeHeaders.Add(header);
                        }
                }

                show.episodes = episodeHeaders.Select(header => header.id).ToList();
            }
            catch (Exception ex)
            {
                lock (Console.Out)
                {
                    ConsoleWriteLine(ex.Message);
                    ConsoleWriteLine(show.url);
                    ConsoleWriteLine();
                }
            }

            return episodeHeaders;
        }

        #endregion

        #region Utilities

        public static async Task<FileStream> PatientOpenWrite(string path)
        {
            FileStream? file = null;
            for (int c = 0; c < 3; c++)
            {
                try
                {
                    file = File.OpenWrite(path);
                    break;
                }
                catch (Exception) { }

                await Task.Delay(1000);
            }
            if (file == null)
                file = File.OpenWrite(path);

            return file;
        }

        public void MigrateToCache(string url, string localPath)
        {
            if (!File.Exists(localPath))
                return;

            string cachePath = GetCachePathForUrl(url);
            File.Move(localPath, cachePath, true);
        }

        int parallelOps = 0;

        public Task ParallelOp<T>(ICollection<T> source, string opname, Func<T, ValueTask> handler, int threads = 0)
        {
            int total = source.Count;
            int n = 0;
            int pctk = 0;

            var options = new ParallelOptions();
            if (threads != 0)
                options.MaxDegreeOfParallelism = threads;

            return Parallel.ForEachAsync(source, options,
                async (x, cancel) =>
                {
                    try
                    {
                        int opCount = Interlocked.Increment(ref parallelOps);
                        if (ShutdownRequested) return;

                        lock (handler)
                        {
                            n++;

                            int mypctk = (int)(n * 100000L / total);

                            if (pctk != mypctk)
                            {
                                pctk = mypctk;

                                lock (Console.Out)
                                {
                                    if (Console.CursorTop == Console.BufferHeight - 1)
                                    {
                                        Console.Write(new string(' ', Console.BufferWidth));
                                    }

                                    var cacheFetches = _recentCacheMisses.Count + _recentCacheHits.Count;
                                    float cacheMissRate = cacheFetches == 0 ? 0 : 100.0f * (float)_recentCacheMisses.Count / cacheFetches;
                                    var fetches = _recentFetches.Count;

                                    Console.CursorLeft = 0;
                                    Console.CursorTop = Console.BufferHeight - 2;
                                    Console.CursorLeft = 0;
                                    Console.Write($"    Fetches: {fetches}");
                                    if (Console.CursorLeft < 25)
                                        Console.Write(new string(' ', 25 - Console.CursorLeft));
                                    Console.Write($"Cache Fetches: {cacheFetches}");
                                    if (Console.CursorLeft < 50)
                                        Console.Write(new string(' ', 50 - Console.CursorLeft));
                                    Console.Write($"Cache Miss Rate: {cacheMissRate:0.0}%");
                                    Console.Write(new string(' ', Console.BufferWidth - Console.CursorLeft));
                                    Console.Write($"    {opname}   -   {n} / {total}   {(pctk / 1000.0f):00.0}%   ({new string('.', opCount)})");
                                    Console.Write(new string(' ', Console.BufferWidth - Console.CursorLeft - 1));
                                    Console.CursorLeft = 0;
                                    Console.CursorTop = Console.BufferHeight - 3;
                                }
                            }
                        }

                        await handler(x);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref parallelOps);
                    }
                });
        }

        string titleListingUrl(DateTime startDate, DateTime endDate, int startIndex)
            => $"https://www.imdb.com/search/title/?view=simple&count=250&release_date={startDate:yyyy-MM-dd},{endDate:yyyy-MM-dd}&start={startIndex}";

        string showPageUrl(long showId)
            => $"https://www.imdb.com/title/tt{showId:0000000}/?";

        string fullCastUrl(long showId)
            => $"https://www.imdb.com/title/tt{showId:0000000}/fullcredits/";

        string personPageUrl(long personId)
            => $"https://www.imdb.com/name/nm{personId:0000000}/";

        string episodeListingUrl(long showId, int season)
            => $"https://www.imdb.com/title/tt{showId}/episodes?season={season}";

        float PruneHistory(ConcurrentQueue<long> queue)
        {
            var now = stopwatch.ElapsedTicks;
            var relevant = now - Stopwatch.Frequency * 60;

            long oldest = 0;

            lock (queue)
            {
                while (queue.TryPeek(out var next) && next < relevant)
                    queue.TryDequeue(out next);

                if (!queue.TryPeek(out oldest))
                    oldest = 0;
            }

            return oldest == 0 ? 0.0f : (float)(queue.Count * (double)Stopwatch.Frequency / (double)(now - oldest));
        }

        Task EnqueueForFetch()
        {
            var tcs = new TaskCompletionSource();

            lock (fetchQueue)
            {
                fetchQueue.Enqueue(tcs);
            }

            PushFetchQueue();

            return tcs.Task;
        }

        void PushFetchQueue()
        {
            int safeActiveFetches;
            TaskCompletionSource? tcs = null;

            do
            {
                lock (fetchQueue)
                {
                    safeActiveFetches = activeFetches;
                    if (safeActiveFetches < MaxActiveFetches && fetchQueue.Count != 0)
                    {
                        tcs = fetchQueue.Dequeue();
                        safeActiveFetches++;
                    }
                    else
                        tcs = null;
                }

                tcs?.SetResult();
            } while (tcs != null);
        }

        ConcurrentQueue<long> _recentFetches = new ConcurrentQueue<long>();

        async Task<HttpContent> fetchContent(string uri)
        {
            HttpResponseMessage? response = null;

            await EnqueueForFetch();

            while (true)
            {
                if (PruneHistory(_recentFetches) * 60 < TargetFetchesPerMinute)
                {
                    _recentFetches.Enqueue(stopwatch.ElapsedTicks);
                    break;
                }

                Thread.Sleep(100);
            }

            try
            {
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, uri);
                        response = (await httpClient.SendAsync(request)).EnsureSuccessStatusCode();
                        break;
                    }
                    catch (HttpRequestException ex)
                    {
                        if (ex.StatusCode == (HttpStatusCode)443)
                        {
                            await Task.Delay(TimeSpan.FromMinutes(1));
                        }
                        else if (ex.StatusCode == (HttpStatusCode)403)
                        {
                            await Task.Delay(TimeSpan.FromMinutes(2));
                        }
                        else if (ex.StatusCode == (HttpStatusCode)503)
                        {
                            await Task.Delay(TimeSpan.FromMinutes(2));
                        }
                        else if (ex.StatusCode == (HttpStatusCode)500)
                        {
                            await Task.Delay(TimeSpan.FromMinutes(2));
                        }
                        else if (ex.StatusCode == (HttpStatusCode)501)
                        {
                            await Task.Delay(TimeSpan.FromMinutes(2));
                        }
                        else
                            throw;
                    }
                }
            }
            finally
            {
                lock (fetchQueue)
                {
                    activeFetches--;
                }

                PushFetchQueue();
            }

            if (response == null)
                throw new HttpRequestException(uri);

            //if (!response.IsSuccessStatusCode)
            //    throw new HttpRequestException(uri, null, response.StatusCode);

            return response.Content;
        }

        public async Task<string> fetch(string url)
        {
            using (var content = await fetchContent(url))
            {
                return await content.ReadAsStringAsync();
            }
        }

        public async Task download(string url, string filePath)
        {
            using (var content = await fetchContent(url))
            {
                File.WriteAllBytes(filePath, await content.ReadAsByteArrayAsync());
            }
        }

        ConcurrentQueue<long> _recentCacheHits = new ConcurrentQueue<long>();
        ConcurrentQueue<long> _recentCacheMisses = new ConcurrentQueue<long>();

        public async Task<string> cachedFetch(string url, string cache)
        {
            string output;

            if (!File.Exists(cache))
            {
                _recentCacheMisses.Enqueue(stopwatch.ElapsedTicks);
                output = await fetch(url);
                WriteCache(cache, output);
            }
            else
            {
                _recentCacheHits.Enqueue(stopwatch.ElapsedTicks);
                output = ReadCache(cache);
            }

            PruneHistory(_recentCacheHits);
            PruneHistory(_recentCacheMisses);

            return output;
        }

        public async Task hintCachedFetch(string url, string cache)
        {
            if (!File.Exists(cache))
            {
                var output = await fetch(url);
                WriteCache(cache, output);
            }
        }

        ConcurrentBag<MemoryStream> memoryStreamPool = new ConcurrentBag<MemoryStream>();

        string ReadCache(string cachePath)
        {
            MemoryStream? outStream;
            if (!memoryStreamPool.TryTake(out outStream))
            {
                outStream = new MemoryStream(10 * 1024 * 1024);
            }
            else
            {
                outStream.Position = 0;
                outStream.SetLength(0);
            }

            try
            {
                using (var inStream = File.OpenRead(cachePath))
                {
                    GZip.Decompress(inStream, outStream, false);
                    return Encoding.UTF8.GetString(outStream.GetBuffer(), 0, (int)outStream.Length);
                }
            }
            finally
            {
                memoryStreamPool.Add(outStream);
            }
        }

        void WriteCache(string cachePath, string content)
        {
            using (var inStream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
            {
                using (var outStream = File.OpenWrite(cachePath))
                {
                    GZip.Compress(inStream, outStream, false);
                }
            }
        }

        public string GetCachePathForUrl(string url)
        {
            lock (hashCodec)
            {
                return Path.Combine(CacheFolder, Convert.ToHexString(hashCodec.ComputeHash(Encoding.UTF8.GetBytes(url))));
            }
        }

        public void discardCache(string url)
        {
            var cache = GetCachePathForUrl(url);
            if (File.Exists(cache))
                File.Delete(cache);
        }

        public Task<string> cachedFetch(string url) => cachedFetch(url, GetCachePathForUrl(url));

        public Task hintCachedFetch(string url) => hintCachedFetch(url, GetCachePathForUrl(url));

        string fixBadImageTags(string xml) => xml_imgFixer.Replace(xml, "/>");

        string? stripRef(string? url) => url == null ? null : url_RefRecognizer.Replace(url, "");

        static Regex ImageModifiers_ImageFluffRecognizer = new Regex(@"\.[^\.]*\.jpg", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        string? stripImageModifiers(string? url) => url == null ? null : ImageModifiers_ImageFluffRecognizer.Replace(url, ".jpg");

        int findClosingDivElement(string page, int divElementStartingIndex)
        {
            int divDepth = 0;

            foreach (var match in xml_divBlockRecognizer.Matches(page, divElementStartingIndex).OfType<Match>())
            {
                if (match.Groups["div"].Success)
                    divDepth++;
                else if (match.Groups["undiv"].Success)
                    divDepth--;
                else
                    break;

                if (divDepth == 0)
                    return match.Index + match.Length;

                if (divDepth < 0)
                    break;
            }

            return -1;
        }

        string cleanTitleSearchPage(string page)
        {
            var listerListIndex = page.IndexOf("lister-list");

            if (listerListIndex == -1) return "";

            var startIndex = page.LastIndexOf('\n', listerListIndex) + 1;
            var endIndex = findClosingDivElement(page, startIndex);

            if (endIndex == -1)
                throw new Exception("Wut");

            string divBlock = page.Substring(startIndex, endIndex - startIndex);
            var validXml = xml_imgAltRecognizer.Replace(xml_badTagRecognizer.Replace(divBlock, "").Replace("&", "&amp;"), "<img");

            return validXml;
        }

        string cleanTitleDetailsPage(string page)
        {
            const string RelevantTagStart = "<script type=\"application/ld+json\">";
            const string RelevantTagEnd = "</script>";

            var start = page.IndexOf(RelevantTagStart) + RelevantTagStart.Length;
            var end = page.IndexOf(RelevantTagEnd, start);

            return page.Substring(start, end - start);
        }

        string cleanSeriesDetailsPage(string page)
        {
            const string RelevantTagStart = "<script id=\"__NEXT_DATA__\" type=\"application/json\">";
            const string RelevantTagEnd = "</script>";

            var start = page.IndexOf(RelevantTagStart) + RelevantTagStart.Length;
            var end = page.IndexOf(RelevantTagEnd, start);

            return page.Substring(start, end - start);
        }

        string cleanEpisodeListingPage(string page)
        {
            const string RelevantTagStart = "<div class=\"list detail eplist\">";

            var start = page.IndexOf(RelevantTagStart);
            var end = findClosingDivElement(page, start);

            var xml = page.Substring(start, end - start).Replace(" itemscope ", " ").Replace("&", "&amp;").Replace("<br>", "", StringComparison.CurrentCultureIgnoreCase);

            xml = xml_badTagRecognizer.Replace(xml, "");
            xml = fixBadImageTags(xml);

            return xml;
        }

        static readonly Regex CleanCredits_ColRecognizer = new Regex(@"<col class=""[^""]*"">", RegexOptions.Compiled);
        static readonly Regex CleanCredits_AmpersandRecognizer = new Regex(@"&(?!lt;|gt;|amp;|quot;|apos)", RegexOptions.Compiled);
        string cleanCreditsPage(string page)
        {
            const string RelevantTagStart = "<div id=\"fullcredits_content\" class=\"header\">";
            const string RelevantTagLandmark = "<div class=\"article\" id=\"see_also\">";
            const string RelevantTagEnd = "</div>";

            var start = page.IndexOf(RelevantTagStart);
            var end = page.LastIndexOf(RelevantTagEnd, -1 + page.LastIndexOf(RelevantTagEnd, page.IndexOf(RelevantTagLandmark, start))) + RelevantTagEnd.Length;

            var almostClean = page.Substring(start, end - start).Replace("&nbsp;", "");
            almostClean = CleanCredits_ColRecognizer.Replace(almostClean, "");
            return CleanCredits_AmpersandRecognizer.Replace(almostClean, "&amp;");
        }

        string cleanPersonDetailsPage(string page)
        {
            const string RelevantTagStart = "<script id=\"__NEXT_DATA__\" type=\"application/json\">";
            const string RelevantTagEnd = "</script>";

            var start = page.IndexOf(RelevantTagStart) + RelevantTagStart.Length;
            var end = page.IndexOf(RelevantTagEnd, start);

            return page.Substring(start, end - start);
        }

        XmlDocument? asXml(string xml)
        {
            var doc = new XmlDocument();
            try { doc.LoadXml(xml); } catch (Exception) { return null; }
            return doc;
        }

        public void ConsoleWriteLine()
        {
            Console.WriteLine();
            Console.CursorTop = Console.BufferHeight - 3;
        }

        public void ConsoleWriteLine(object? data)
        {
            if (data == null)
                Console.WriteLine("<null>");
            else
                Console.WriteLine(data);

            Console.MoveBufferArea(0, 1, Console.BufferWidth, Console.BufferHeight - 3, 0, 0);
            Console.CursorTop = Console.BufferHeight - 3;
        }

        #endregion
    }
}
