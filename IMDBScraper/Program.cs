using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.GZip;
using IMDBScraper;
using SixLabors.ImageSharp;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

#if DEBUG
const bool ENABLE_AUTOSAVE = false;
#else
const bool ENABLE_AUTOSAVE = true; // Just pulling cache files
#endif

const bool SCRAPE_SHOW_HEADERS = true;
const bool SCRAPE_SHOW_DETAILS = true;
const bool SCRAPE_EPISODES = true;
const bool SCRAPE_SHOW_CAST = true;
const bool SCRAPE_PEOPLE_DATA = true;
const bool SCRAPE_SHOW_POSTERS = true;
const bool SCRAPE_TRAILER_THUMBNAILS = true;
const bool SCRAPE_PEOPLE_IMAGES = true;
//const bool NORMALIZE_IMAGE_SIZES = true;
//const bool GENERATE_THUMBNAILS = true;
const bool VALIDATE_DB = true;

if(args.Length != 3)
{
    Console.WriteLine($"Usage: {Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().GetName().Name)} <Desired Show Count> <Max Actors Per Show> <Output Folder>");
    return;
}

Console.WriteLine("Loading Existing Database...");
var reader = new IMDBReader();
var db = new TheStarsDB(args[2]);
var desiredShows = int.Parse(args[0]);
var maxActorsPerShow = int.Parse(args[1]);

#region Management

Console.CursorTop = Console.BufferHeight - 1;

EventWaitHandle shutdownHandle = new EventWaitHandle(false, EventResetMode.ManualReset);
EventWaitHandle autosaveHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
var inputTask = Task.Run(() =>
{
    Console.TreatControlCAsInput = true;
    while (!reader.ShutdownRequested)
    {
        var press = Console.ReadKey(true);

        if (press.Modifiers.HasFlag(ConsoleModifiers.Control) && press.Key == ConsoleKey.C)
        {
            shutdownHandle.Set();
            reader.ShutdownRequested = true;
            lock (Console.Out)
            {
                reader.ConsoleWriteLine("Shutting Down...");
            }
        }
    }
});

void triggerSave() => autosaveHandle.Set();

var autosaveTask = Task.Run(async () =>
{
#pragma warning disable CS0162 // Unreachable code detected
    if (!ENABLE_AUTOSAVE)
        return;
#pragma warning restore CS0162 // Unreachable code detected

    var waitingStuff = new WaitHandle[] { shutdownHandle, autosaveHandle };

    while (WaitHandle.WaitAny(waitingStuff, TimeSpan.FromMinutes(5)) != 0)
    {
        if (reader.ShutdownRequested)
            return;

        if (Debugger.IsAttached)
            continue;

        await db.SaveAll();
    }
});

#endregion

if (SCRAPE_SHOW_HEADERS && !reader.ShutdownRequested)
{
    const int SearchSpan = 30;

    DateTime startDate = DateTime.Today;
    List<DateTime> datesToSearch = new List<DateTime>();
    for (DateTime date = startDate; date.Year > 1990 && !reader.ShutdownRequested; date = date.Subtract(TimeSpan.FromDays(SearchSpan)))
        datesToSearch.Add(date);

    for (int u = 0; u < 10000 && !reader.ShutdownRequested && db.Shows.Count < desiredShows; u += 2000)
    {
        var maxThreads = (desiredShows / 250) + 1;

        if (maxThreads > 16)
            maxThreads = 0;

        await reader.ParallelOp(datesToSearch, $"Scraping top {u + 2000} shows monthly", async (date) =>
        {
            var endDate = date.AddDays(SearchSpan);
            int showsOnDate = 0;
            int i = u + 1;

            while (showsOnDate < 2000 && !reader.ShutdownRequested && db.Shows.Count < desiredShows)
            {
                var fetched = await reader.ScrapeHeadersFromSearchPage(date, endDate, i);
                if (fetched.Count == 0)
                    break;

                showsOnDate += fetched.Count;
                i += fetched.Count;
                fetched.ForEach(db.Merge);
            }

            if (showsOnDate != 0)
            {
                lock (Console.Out)
                {
                    reader.ConsoleWriteLine($"Collected {showsOnDate} Shows {date:M/d/yyyy} to {endDate:M/d/yyyy}\t\t({db.ShowHeaders.Count} total)");
                }
            }
        }, maxThreads);
    }
}

if (SCRAPE_SHOW_DETAILS && !reader.ShutdownRequested)
{
    await reader.ParallelOp(db.ShowHeaders, "Scraping Show Details", async header =>
    {
        if (!db.TryGetShow(header.id, out var show))
            show = new Show(header);

        await reader.ScrapeShowDetails(show, db);

        db.Merge(show);
    });

    triggerSave();
}

if (SCRAPE_EPISODES && !reader.ShutdownRequested)
{
    int oldShowCount = db.Shows.Count;

    await reader.ParallelOp(db.Shows.Where(s => (
        s.type == ShowType.TVMiniSeries ||
        s.type == ShowType.TVSeries) &&
        s.totalEpisodes == null).ToList(),
        "Scraping Series Episodes", async show =>
        {
            foreach (var header in await reader.ScrapeEpisodesFromSeries(show))
            {
                db.Merge(header);
            }
        });

    if(SCRAPE_SHOW_DETAILS && db.Shows.Count > oldShowCount)
    {
        await reader.ParallelOp(db.ShowHeaders, "Scraping Episode Details", async header =>
        {
            if (!db.TryGetShow(header.id, out var show))
                show = new Show(header);

            await reader.ScrapeShowDetails(show, db);

            db.Merge(show);
        });
    }

    triggerSave();
}

if (SCRAPE_SHOW_CAST && !reader.ShutdownRequested)
{
    await reader.ParallelOp(db.Shows, "Scraping Show Credits", async show => await reader.ScrapeCredits(show, db, maxActorsPerShow));

    triggerSave();
}

if (SCRAPE_PEOPLE_DATA && !reader.ShutdownRequested)
{
    var personIds = db.GetFullRoleManifest().Select(role => role.talent).Distinct().Where(id => !db.HasPerson(id)).ToList();

    await reader.ParallelOp(personIds, "Scraping People Data", async id =>
    {
        db.Merge(await reader.ScrapePersonDetails(id));
    });

    triggerSave();
}

if (SCRAPE_TRAILER_THUMBNAILS && !reader.ShutdownRequested)
{
#pragma warning disable CS8604 // Possible null reference argument.
    await reader.ParallelOp(db.Shows.Select(s => s.trailerThumbnailUrl).Where(url => !string.IsNullOrEmpty(url)).Distinct().ToList(),
        "Scraping Trailer Thumbnails", async (url) => await reader.ScrapeImage(url, db.GetLocalImagePath(url)));
#pragma warning restore CS8604 // Possible null reference argument.

    foreach (var show in db.Shows)
    {
        if (show.trailerThumbnailUrl == null)
            continue;

        show.trailerThumbnailLocalPath = db.GetLocalImagePath(show.trailerThumbnailUrl);
    }

    triggerSave();
}

if (SCRAPE_SHOW_POSTERS && !reader.ShutdownRequested)
{
    await reader.ParallelOp(db.ShowHeaders.Select(h => h.posterUrl).Where(url => !string.IsNullOrEmpty(url)).Distinct().ToList(),
        "Scraping Posters", async (url) =>
        {
#pragma warning disable CS8604 // Possible null reference argument.
            await reader.ScrapeImage(url, db.GetLocalImagePath(url));
#pragma warning restore CS8604 // Possible null reference argument.
        });

    foreach (var header in db.ShowHeaders)
    {
        if (header.posterUrl == null)
            continue;

        header.posterLocalPath = db.GetLocalImagePath(header.posterUrl);

        if (db.TryGetShow(header.id, out var show))
            show.posterLocalPath = header.posterLocalPath;
    }

    triggerSave();
}

if (SCRAPE_PEOPLE_IMAGES && !reader.ShutdownRequested)
{
#pragma warning disable CS8604 // Possible null reference argument.
    await reader.ParallelOp(db.People.Select(p => p.imageUrl).Where(url => !string.IsNullOrEmpty(url)).Distinct().ToList(),
        "Scraping Person Images", async url => await reader.ScrapeImage(url, db.GetLocalImagePath(url)));
#pragma warning restore CS8604 // Possible null reference argument.

    foreach (var person in db.People)
    {
        if (person.imageUrl == null)
            continue;

        person.imageLocalPath = db.GetLocalImagePath(person.imageUrl);
    }
}

reader.ShutdownRequested = true;
shutdownHandle.Set();
await autosaveTask;
await db.SaveAll();

if (VALIDATE_DB)
{
    if (File.Exists(db.AnalysisPath))
        File.Delete(db.AnalysisPath);

    using (var f = File.OpenWrite(db.AnalysisPath))
    {
        using (var w = new StreamWriter(f))
        {

            /* 
             *  Validation points:
             *  Roles identified in Show.topBilling and Show.credits exist
             *  People and Shows identified by Roles exist
             *  Episodes of series exist and are identified as episodes
             *  
             *  How many times each rating is used - flag any unused rating.
             *  How many time each ShowType is used - flag any unused.
             *  How many times each role type is used - flag any unused.
             *  
             *  For every show with a poster URL, there is also a poster local path.
             *  For every show with a trailer URL, there is also a trailer thumbnail URL and local path
             *  For every person with an image url, there is also a local path
             *  
             *  % of shows with titles
             *  % of shows with urls
             *  % of shows with poster URLs
             *  % of shows with known release date
             *  % of shows with known end date
             *  % of shows with known duration
             *  % of shows with known quality rating
             *  % of shows with votes
             *  % of shows with a description
             *  % of shows with a trailer URL
             *  % of shows known to be adult, vs unknown
             *  % of shows with trivia
             *  % of shows with goofs
             *  % of shows with production budget
             *  % of shows with worldwideGross
             *  % of shows with lifetimeGross
             *  % of shows with openingWeekendGross
             *  % of shows with openingWeekendEnd
             *  % of shows with spokenLanguages
             *  % of shows with countriesOfOrigin
             *  % of series with listed episodes
             *  % of shows with top billing
             *  % of shows with known credits
             *  
             *  % of named roles
             *  % of named talent
             *  % of people with urls
             *  % of people with imageUrl
             *  % of people with bios
             *  % of people with birthdays
             *  % of people with death dates
             *  Breakdown by death status (unknown, dead, alive)
             *  % of people with quotes
             *  % or people with trivia
             *  % of people with heights
             *  
             *  histogram of heights
             *  histogram of death dates per year
             *  histogram of birthdays per year
             *  histogram of bio length
             *  histogram of credits quantity
             *  histogram of openingWeekendEnd
             *  histogram of openingWeekendEnd per year
             *  histogram of openingWeekendGross
             *  histogram of lifetimeGross
             *  histogram of worldwideGross
             *  historygram of production budgets
             *  histogram of vote quantity
             *  histogram of quality ratings
             *  histogram of genre quantity
             *  total # of unique genres
             *  total # of unique spoken languages
             *  total # of countriesOfOrigin
             *  
             *  largest openingWeekendGross, lifetimeGross, worldwideGross, productionBudget, vote quantity, quality rating
             */

            var roles = db.GetFullRoleManifest();

            void writeHeader(string header)
            {
                w.WriteLine();
                w.WriteLine(header);
                w.WriteLine(new string('=', header.Length));
                w.WriteLine();
            }

            void writeError(string msg)
            {
                w.WriteLine($"!!!! ERROR !!!! - {msg}");
            }

            void writeEnumUsage<T>(IEnumerable<(T, int)> usage) where T : struct, Enum
            {
                var name = typeof(T).Name;

                w.WriteLine($"{name} Usage");
                w.WriteLine("---------------------------------");
                w.WriteLine();
                w.WriteLine($"Unused Values: {string.Join(',', Enum.GetNames<T>().Except(usage.Select(p => p.Item1.ToString())))}");
                foreach (var p in usage)
                    w.WriteLine($"{p.Item1} - {p.Item2}");
                w.WriteLine();
            }

            void writeShowStat(string name, int c)
            {
                var pct = c * 100.00 / db.Shows.Count;

                if (c == 0)
                    w.WriteLine("!!!! WARNING !!!!");
                w.WriteLine($"{name} - {pct:0.00}%    ({c})");
            }

            void writeRoleStat(string name, int c)
            {
                var pct = c * 100.00 / roles.Count;

                if (c == 0)
                    w.WriteLine("!!!! WARNING !!!!");
                w.WriteLine($"{name} - {pct:0.00}%    ({c})");
            }

            void writePersonStat(string name, int c)
            {
                var pct = c * 100.00 / db.People.Count;

                if (c == 0)
                    w.WriteLine("!!!! WARNING !!!!");
                w.WriteLine($"{name} - {pct:0.00}%    ({c})");
            }

            writeHeader("Data Integrity Check");

            foreach (var show in db.Shows)
            {
                if (show.credits != null)
                {
                    if (show.principalCredits != null)
                    {
                        foreach (var roleId in show.principalCredits)
                            if (!show.credits.Contains(roleId))
                            {
                                if (!db.TryGetRole(roleId, out var role))
                                    writeError($"{show.title} has an unidentified credited role ({roleId})");
                                else
                                {
                                    if (!db.TryGetPerson(role.talent, out var person))
                                        writeError($"Role {role.id} \"{role.name}\" references unknown talent {role.talent}");
                                    else
                                        writeError($"{show.title} has principal credit not present in regular credits:\n\t({role.name}, {person.name}, {role.type}, {roleId})");
                                }
                            }
                    }

                    foreach (var roleId in show.credits)
                        if (!db.TryGetRole(roleId, out _))
                        {
                            writeError($"{show.title} has an unidentified credited role ({roleId})");
                        }
                }

                if (show.episodes != null)
                {
                    foreach (var episode in show.episodes)
                    {
                        if (!db.HasShow(episode))
                        {
                            writeError($"{show.title} has an unidentified episode ({episode})");
                        }
                    }
                }
            }

            foreach(var role in db.GetFullRoleManifest())
            {
                if (!db.TryGetShow(role.show, out var show))
                    writeError($"Role {role.id} \"{role.name}\" is orphaned, with unknown show {role.show}");
                else
                {
                    if (show.credits == null)
                        writeError($"Role {role.id} \"{role.name}\" identified for show {show.id} \"{show.title}\", but the show has no listed credits");
                    else if(!show.credits.Contains(role.id))
                        writeError($"Role {role.id} \"{role.name}\" identified for show {show.id} \"{show.title}\", but is not a member of the show's credits");
                }

                if (!db.TryGetPerson(role.talent, out var person))
                    writeError($"Role {role.id} \"{role.name}\" is taken by unknown person {role.talent}");
            }

            writeHeader("Enum Identification Check");

            var ratingUsage = db.Shows.GroupBy(s => s.contentRating).Select(g => (g.Key, g.Count())).ToList();
            var typeUsage = db.Shows.GroupBy(s => s.type).Select(g => (g.Key, g.Count())).ToList();
            var roleTypeUsage = roles.GroupBy(r => r.type).Select(g => (g.Key, g.Count())).ToList();

            writeEnumUsage(ratingUsage);
            writeEnumUsage(typeUsage);
            writeEnumUsage(roleTypeUsage);


            writeHeader("Data Extraction Check");

            var showsWithTitles = db.Shows.Count(show => show.title != null);
            writeShowStat("Shows With Titles", showsWithTitles);
            var showsWithUrls = db.Shows.Count(show => show.url != null);
            writeShowStat("Shows With Urls", showsWithUrls);
            var showsWithPosterUrls = db.Shows.Count(show => show.posterUrl != null);
            writeShowStat("Shows With Poster Urls", showsWithPosterUrls);
            var showsWithReleaseDate = db.Shows.Count(show => show.releaseDate != null);
            writeShowStat("Shows With Release Dates", showsWithReleaseDate);
            var showsWithEndDate = db.Shows.Count(show => show.endDate != null);
            writeShowStat("Shows With End Dates", showsWithEndDate);
            var showsWithDuration = db.Shows.Count(show => show.duration != null);
            writeShowStat("Shows With Duration", showsWithDuration);
            var showsWithQuality = db.Shows.Count(show => show.qualityRating != null);
            writeShowStat("Shows With Quality Rating", showsWithQuality);
            var showsWithVotes = db.Shows.Count(show => show.votes != null);
            writeShowStat("Shows With Votes", showsWithVotes);
            var showsWithDescriptions = db.Shows.Count(show => show.description != null);
            writeShowStat("Shows With Descriptions", showsWithDescriptions);
            var showsWithTrailer = db.Shows.Count(show => show.trailerUrl != null);
            writeShowStat("Shows With Trailers", showsWithTrailer);
            var showsWithKnownForAdults = db.Shows.Count(show => show.isAdult == true);
            writeShowStat("Shows For Adults", showsWithKnownForAdults);
            var showsWithTrivia = db.Shows.Count(show => show.triviaMarkdown != null);
            writeShowStat("Shows With Trivia", showsWithTrivia);
            var showsWithGoofs = db.Shows.Count(show => show.goofMarkdown != null);
            writeShowStat("Shows With Goofs", showsWithGoofs);
            var showsWithProductionBudget = db.Shows.Count(show => show.productionBudget != null);
            writeShowStat("Shows With Production Budgets", showsWithProductionBudget);
            var showsWithWorldwideGross = db.Shows.Count(show => show.worldwideGross != null);
            writeShowStat("Shows With Worldwide Gross", showsWithWorldwideGross);
            var showsWithLifetimeGross = db.Shows.Count(show => show.lifetimeGross != null);
            writeShowStat("Shows With Lifetime Gross", showsWithLifetimeGross);
            var showsWithOpeningWeekendGross = db.Shows.Count(show => show.openingWeekendGross != null);
            writeShowStat("Shows With Opening Weekend Gross", showsWithOpeningWeekendGross);
            var showsWithOpeningWeekendEnd = db.Shows.Count(show => show.openingWeekendEnd != null);
            writeShowStat("Shows With Opening Weekend Date", showsWithOpeningWeekendEnd);
            var showsWithSpokenLanguages = db.Shows.Count(show => show.spokenLanguages != null);
            writeShowStat("Shows With Spoken Languages", showsWithSpokenLanguages);
            var showsWithCountriesOfOrigin = db.Shows.Count(show => show.countriesOfOrigin != null);
            writeShowStat("Shows With Countries of Origin", showsWithCountriesOfOrigin);
            var showsWithEpisodes = db.Shows.Count(show => show.episodes != null);
            writeShowStat("Shows With Episodes", showsWithEpisodes);
            var showsWithPrincipalCredits = db.Shows.Count(show => show.principalCredits != null);
            writeShowStat("Shows With Principal Credits", showsWithPrincipalCredits);
            var showsWithCredits = db.Shows.Count(show => show.credits != null);
            writeShowStat("Shows With Credits", showsWithCredits);

            var rolesWithNames = roles.Count(role => role.name != null);
            writeRoleStat("Roles With Names", rolesWithNames);

            var peopleWithNames = db.People.Count(person => person.name != null);
            writePersonStat("People With Names", peopleWithNames);
            var peopleWithUrls = db.People.Count(person => person.url != null);
            writePersonStat("People With Urls", peopleWithUrls);
            var peopleWithImageUrls = db.People.Count(person => person.imageUrl != null);
            writePersonStat("People With Images", peopleWithImageUrls);
            var peopleWithBios = db.People.Count(person => person.bio != null);
            writePersonStat("People With Bios", peopleWithBios);
            var peopleWithBirthdays = db.People.Count(person => person.birthday != null);
            writePersonStat("People With Birthdays", peopleWithBirthdays);
            var peopleWithDeathDays = db.People.Count(person => person.deathDate != null);
            writePersonStat("People With Death Dates", peopleWithDeathDays);
            var peopleWithDeathStatus = db.People.Count(person => person.isDead != null);
            writePersonStat("People With Death Status", peopleWithDeathStatus);
            var peopleDead = db.People.Count(person => person.isDead == true);
            writePersonStat("People Dead", peopleDead);
            var peopleWithQuote = db.People.Count(person => person.quote != null);
            writePersonStat("People With Quotes", peopleWithQuote);
            var peopleWithTrivia = db.People.Count(person => person.trivia != null);
            writePersonStat("People With Trivia", peopleWithTrivia);
            var peopleWithHeight = db.People.Count(person => person.heightCm != null);
            writePersonStat("People With Heights", peopleWithHeight);

            writeHeader("Data Quality Check");

#pragma warning disable CS8603 // Possible null reference return.
            var uniqueGenres = db.Shows.Where(s => s.genre != null).SelectMany(s => s.genre).Distinct();
#pragma warning restore CS8603 // Possible null reference return.
            w.WriteLine("Unique Genres Identified:");
            w.WriteLine(string.Join(',', uniqueGenres));
            w.WriteLine();

            var uniqueSpokenLanguages = db.Shows.SelectManyNonNull(s => s.spokenLanguages).Select(s => s.text).NotNull().Distinct();
            w.WriteLine("Unique Spoken Languages:");
            w.WriteLine(string.Join(',', uniqueSpokenLanguages));
            w.WriteLine();

            var uniqueCountriesOfOrigin = db.Shows.SelectManyNonNull(s => s.countriesOfOrigin).Select(s => s.text).NotNull().Distinct();
            w.WriteLine("Unique Countries of Origin:");
            w.WriteLine(string.Join(',', uniqueCountriesOfOrigin));
            w.WriteLine();

            void histogram<T>(string title, IEnumerable<T?> data) where T : struct, IConvertible
            {
                var dataCollection = (ICollection<T>)data.NotNull().ToList();
                w.WriteHistogram(title, dataCollection, Console.BufferWidth, 25);
                w.WriteLine();
            }

            histogram("Person Heights", db.People.Select(p => p.heightCm));
            histogram("Person Birthdays", db.People.Select(p => p.birthday?.DayOfYear));
            histogram("Person Death Dates", db.People.Select(p => p.deathDate?.DayOfYear));
            histogram("Person Bio Lengths", db.People.Select(p => p.bio?.Length));
            histogram("Show Credits Length", db.Shows.Select(s => s.credits?.Count));
            histogram("Opening Weekend", db.Shows.Select(s => s.openingWeekendEnd?.DayOfYear));
            histogram("Opening Weekend Gross", db.Shows.Select(s => s.openingWeekendGross?.amount));
            histogram("Lifetime Gross", db.Shows.Select(s => s.lifetimeGross?.amount));
            histogram("Worldwide Gross", db.Shows.Select(s => s.worldwideGross?.amount));
            histogram("Production Budgets", db.Shows.Select(s => s.productionBudget?.amount));
            histogram("Votes", db.Shows.Select(s => s.votes));
            histogram("Qualities", db.Shows.Select(s => s.qualityRating));
            histogram("Genre Counts", db.Shows.Select(s => s.genre?.Count));
        }
    }
}