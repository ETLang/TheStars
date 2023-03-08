using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IMDBScraper
{
    public class TheStarsDB
    {
        public readonly string DataRoot;
        public readonly string ImageFolder;
        public readonly string ShowHeaderListingPath;
        public readonly string ShowDBPath;
        public readonly string PeopleDBPath;
        public readonly string RoleDBPath;
        public readonly string AnalysisPath;

        ConcurrentDictionary<long, ShowHeader> showHeaders = new ConcurrentDictionary<long, ShowHeader>();
        ConcurrentDictionary<long, Show> shows = new ConcurrentDictionary<long, Show>();
        ConcurrentDictionary<long, Person> people = new ConcurrentDictionary<long, Person>();
        RoleDB roleDB = new RoleDB();

        public TheStarsDB(string dataRoot)
        {
            DataRoot = dataRoot;

            ImageFolder = Path.Combine(DataRoot, "Images");
            ShowHeaderListingPath = Path.Combine(DataRoot, "AllShowHeaders.json");
            ShowDBPath = Path.Combine(DataRoot, "Shows.json");
            PeopleDBPath = Path.Combine(DataRoot, "People.json");
            RoleDBPath = Path.Combine(DataRoot, "Roles.json");
            AnalysisPath = Path.Combine(DataRoot, "Analysis.txt");

            // Create folders, if necessary
            if(!Directory.Exists(DataRoot)) 
                Directory.CreateDirectory(DataRoot);

            if(!Directory.Exists(ImageFolder))
                Directory.CreateDirectory(ImageFolder);

            // Load known data
            var showHeaderList = LoadList<ShowHeader>(ShowHeaderListingPath);
            foreach (var header in showHeaderList)
                showHeaders[header.id] = header;

            var showList = LoadList<Show>(ShowDBPath);
            foreach (var show in showList)
                shows[show.id] = show;

            var peopleList = LoadList<Person>(PeopleDBPath);
            foreach (var person in peopleList)
                people[person.id] = person;

            roleDB.Load(RoleDBPath);
        }

        public ICollection<ShowHeader> ShowHeaders => showHeaders.Values;
        public ICollection<Show> Shows => shows.Values;
        public ICollection<Person> People => people.Values;

        public bool HasShow(long id) => shows.ContainsKey(id);
        public bool HasPerson(long id) => people.ContainsKey(id);

        public bool TryGetShow(long id, [MaybeNullWhen(false)] out Show show) { return shows.TryGetValue(id, out show); }
        public bool TryGetPerson(long id, [MaybeNullWhen(false)] out Person person) { return people.TryGetValue(id, out person); }

        public string GetLocalImagePath(string url)
        {
            return Path.Combine(ImageFolder, url.Substring(url.LastIndexOf('/') + 1));
        }

        public List<Role> GetFullRoleManifest()
        {
            return roleDB.GetFullManifest();
        }

        public Role GetRole(RoleType type, long showId, long personId)
        {
            return roleDB.GetOrCreate(type, showId, personId);
        }

        public bool TryGetRole(long id, [MaybeNullWhen(false)] out Role role)
        {
            if (!roleDB.Has(id))
            {
                role = null;
                return false;
            }

            role = roleDB[id];
            return true;
        }

        public async Task SaveAll()
        {
            await SaveShowHeaders();
            await SaveShowsPassive();
            await SavePeoplePassive();
            await SaveRolesPassive();
        }

        #region Merging Data

        public void Merge(ShowHeader header)
        {
            ShowHeader? target;

            lock (showHeaders)
            {
                if (!showHeaders.TryGetValue(header.id, out target))
                {
                    target = new ShowHeader { id = header.id };
                    showHeaders.TryAdd(header.id, target);
                }
            }

            if (header == target) return;

            if (header.id != target.id)
                throw new InvalidOperationException("Shows aren't the same");

            target.posterLocalPath = header.posterLocalPath ?? target.posterLocalPath;
            target.posterUrl = header.posterUrl ?? target.posterUrl;
            target.episode = header.episode ?? target.episode;
            target.season = header.season ?? target.season;
            target.title = header.title ?? target.title;
            target.url = header.url ?? target.url;

            Show? show;

            lock (shows)
            {
                if (!shows.TryGetValue(header.id, out show))
                {
                    show = new Show(header);
                    shows.TryAdd(header.id, show);
                    return;
                }
            }

            show.posterLocalPath = header.posterLocalPath ?? show.posterLocalPath;
            show.posterUrl = header.posterUrl ?? show.posterUrl;
            show.episode = header.episode ?? show.episode;
            show.season = header.season ?? show.season;
            show.title = header.title ?? show.title;
            show.url = header.url ?? show.url;
        }

        public void Merge(Show? show)
        {
            if (show == null)
                return;

            Show? target;

            lock (shows)
            {
                if (!shows.TryGetValue(show.id, out target))
                {
                    shows.TryAdd(show.id, show);
                    return;
                }
            }

            if (show == target) return;

            target.posterLocalPath = show.posterLocalPath ?? target.posterLocalPath;
            target.posterUrl = show.posterUrl ?? target.posterUrl;
            target.episode = show.episode ?? target.episode;
            target.season = show.season ?? target.season;
            target.title = show.title ?? target.title;
            target.url = show.url ?? target.url;
            target.type = show.type != ShowType.Unknown ? show.type : target.type;
            target.releaseDate = show.releaseDate ?? target.releaseDate;
            target.endDate = show.endDate ?? target.endDate;
            target.totalSeasons = show.totalSeasons ?? target.totalSeasons;
            target.totalEpisodes = show.totalEpisodes ?? target.totalEpisodes;
            target.duration = show.duration ?? target.duration;
            target.contentRating = show.contentRating != Rating.Unrated ? show.contentRating : target.contentRating;
            target.qualityRating = show.qualityRating ?? target.qualityRating;
            target.votes = show.votes ?? target.votes;
            target.genre = MergeCollection(show.genre, target.genre);
            target.description = show.description ?? target.description;
            target.trailerUrl = show.trailerUrl ?? target.trailerUrl;
            target.trailerThumbnailUrl = show.trailerThumbnailUrl ?? target.trailerThumbnailUrl;
            target.trailerThumbnailLocalPath = show.trailerThumbnailLocalPath ?? target.trailerThumbnailLocalPath; ;
            target.isAdult = show.isAdult ?? target.isAdult;
            target.triviaMarkdown = show.triviaMarkdown ?? target.triviaMarkdown;
            target.goofMarkdown = show.goofMarkdown ?? target.goofMarkdown;
            target.productionBudget = show.productionBudget ?? target.productionBudget;
            target.worldwideGross = show.worldwideGross ?? target.worldwideGross;
            target.lifetimeGross = show.lifetimeGross ?? target.lifetimeGross;
            target.openingWeekendGross = show.openingWeekendGross ?? target.openingWeekendGross;
            target.openingWeekendEnd = show.openingWeekendEnd ?? target.openingWeekendEnd;
            target.spokenLanguages = MergeCollection(show.spokenLanguages, target.spokenLanguages);
            target.countriesOfOrigin = MergeCollection(show.countriesOfOrigin, target.countriesOfOrigin);
            target.principalCredits = MergeCollection(show.principalCredits, target.principalCredits);
            target.credits = MergeCollection(show.credits, target.credits);
            target.episodes = MergeCollection(show.episodes, target.episodes);
        }

        public void Merge(Person? person)
        {
            if (person == null)
                return;

            Person? target;

            lock (people)
            {
                if (!people.TryGetValue(person.id, out target))
                {
                    people.TryAdd(person.id, person);
                    return;
                }
            }

            if (person == target) return;

            target.name = person.name ?? target.name;
            target.url = person.url ?? target.url;
            target.imageUrl = person.imageUrl ?? target.imageUrl;
            target.imageLocalPath = person.imageLocalPath ?? target.imageLocalPath;
            target.bio = person.bio ?? target.bio;
            target.birthday = person.birthday ?? target.birthday;
            target.deathDate = person.deathDate ?? target.deathDate;
            target.isDead = person.isDead ?? target.isDead;
            target.trivia = person.trivia ?? target.trivia;
            target.quote = person.quote ?? target.quote;
            target.heightCm = person.heightCm ?? target.heightCm;
        }

        #endregion

        #region Private

        List<T> LoadList<T>(string path)
        {
            if (File.Exists(path))
            {
                using (var file = File.OpenRead(path))
                {
                    return Json.Deserialize<List<T>>(file) ?? new List<T>();
                }
            }
            else
                return new List<T>();
        }

        async Task SaveList<T>(string path, List<T> list)
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

            using (file)
            {
                Json.Serialize(file, list);
            }
        }

        Task SaveShowHeaders()
        {
            List<ShowHeader> toSave;

            lock (showHeaders)
            {
                toSave = showHeaders.Values.ToList();
            }

            return SaveList(ShowHeaderListingPath, toSave);
        }

        Task SaveShows()
        {
            List<Show> toSave;

            lock (shows)
            {
                toSave = shows.Values.ToList();
            }

            return SaveList(ShowDBPath, toSave);
        }

        async Task SaveShowsPassive()
        {
            try { await SaveShows(); } catch (Exception) { }
        }

        Task SaveRoles() => roleDB.Save(RoleDBPath);

        async Task SaveRolesPassive()
        {
            try { await SaveRoles(); } catch (Exception) { }
        }

        Task SavePeople()
        {
            List<Person> toSave;

            lock (people)
            {
                toSave = people.Values.ToList();
            }

            return SaveList(PeopleDBPath, toSave);
        }

        async Task SavePeoplePassive()
        {
            try { await SavePeople(); } catch (Exception) { }
        }

        IReadOnlyList<T>? MergeCollection<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b)
        {
            if (a == null) return b;
            if (b == null) return a;

            return a.Union(b).Distinct().ToList();
        }

        #endregion
    }
}
