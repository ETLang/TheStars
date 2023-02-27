using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IMDBScraper
{
    public enum Rating
    {
        Unrated,
        G,
        PG,
        PG13,
        R,
        NC17,
        X,
        TVY,
        TVY7,
        TVY7FV,
        TVG,
        TVPG,
        TV14,
        TVMA,
        VG_E,
        VG_E10,
        VG_T,
        VG_M,
        Approved
    }

    public enum ShowType
    {
        Unknown,
        Movie,
        TVMovie,
        TVSeries,
        TVMiniSeries,
        TVSpecial,
        Episode,
        Short,
        MusicVideo,
        Podcast,
        VideoGame
    }

    public enum RoleType
    {
        Unspecified,
        Director,
        Writer,
        Producer,
        Actor
    }

    public class ShowHeader
    {
        public long id { get; set; }
        public string? title { get; set; }
        public string? url { get; set; }
        public string? posterUrl { get; set; }
        public string? posterLocalPath { get; set; }
        public int? season { get; set; }
        public int? episode { get; set; }
    }

    public class Show : ShowHeader
    {
        public Show() { }
        public Show(ShowHeader header)
        {
            id = header.id;
            title = header.title;
            url = header.url;
            posterUrl = header.posterUrl;
            posterLocalPath = header.posterLocalPath;
            season = header.season;
            episode = header.episode;
        }

        public Show(Show copy)
        {
            id = copy.id;
            title = copy.title;
            url = copy.url;
            posterUrl = copy.posterUrl;
            posterLocalPath = copy.posterLocalPath;
            type = copy.type;
            releaseDate = copy.releaseDate;
            endDate = copy.endDate;
            duration = copy.duration;
            contentRating = copy.contentRating;
            qualityRating = copy.qualityRating;
            votes = copy.votes;
            genre = copy.genre;
            description = copy.description;
            trailerUrl = copy.trailerUrl;
            trailerThumbnailUrl = copy.trailerThumbnailUrl;
            trailerThumbnailLocalPath = copy.trailerThumbnailLocalPath;
            isAdult = copy.isAdult;
            triviaMarkdown = copy.triviaMarkdown;
            goofMarkdown = copy.goofMarkdown;
            productionBudget = copy.productionBudget;
            worldwideGross = copy.worldwideGross;
            lifetimeGross = copy.lifetimeGross;
            openingWeekendGross = copy.openingWeekendGross;
            openingWeekendEnd = copy.openingWeekendEnd;
            spokenLanguages = copy.spokenLanguages;
            countriesOfOrigin = copy.countriesOfOrigin;
            principalCredits = copy.principalCredits;
            credits = copy.credits;
            episodes = copy.episodes;
            season = copy.season;
            episode = copy.episode;
        }

        public ShowType type { get; set; }
        public DateTime? releaseDate { get; set; }
        public DateTime? endDate { get; set; }
        public int? totalSeasons { get; set; }
        public int? totalEpisodes { get; set; }
        public TimeSpan? duration { get; set; }
        public Rating contentRating { get; set; }
        public float? qualityRating { get; set; }
        public int? votes { get; set; }
        public IReadOnlyList<string>? genre { get; set; }
        public string? description { get; set; }
        public string? trailerUrl { get; set; }
        public string? trailerThumbnailUrl { get; set; }
        public string? trailerThumbnailLocalPath { get; set; }
        public bool? isAdult { get; set; }
        public string? triviaMarkdown { get; set; }
        public string? goofMarkdown { get; set; }
        public Money? productionBudget { get; set; }
        public Money? worldwideGross { get; set; }
        public Money? lifetimeGross { get; set; }
        public Money? openingWeekendGross { get; set; }
        public DateTime? openingWeekendEnd { get; set; }
        public IReadOnlyList<TextWithId>? spokenLanguages { get; set; }
        public IReadOnlyList<TextWithId>? countriesOfOrigin { get; set; }
        public IReadOnlyList<long>? principalCredits { get; set; }
        public IReadOnlyList<long>? credits { get; set; }
        public IReadOnlyList<long>? episodes { get; set; }
    }

    public class RoleKey
    {
        public RoleType type { get; set;  }
        public long show { get; set;  }
        public long talent { get; set;  }

        public RoleKey() { }

        public RoleKey(RoleType type, long show, long talent)
        {
            this.type = type;
            this.show = show;
            this.talent = talent;
        }

        public override int GetHashCode()
        {
            return type.GetHashCode() ^ show.GetHashCode() ^ talent.GetHashCode();
        }

        public override bool Equals(object? obj)
        {
            var other = obj as RoleKey;

            if (other == null) return false;

            return type == other.type && show == other.show && talent == other.talent;
        }
    }

    public class Role : RoleKey
    {
        public long id { get; set; }
        public string? name { get; set; }
    }

    public class Person
    {
        public long id { get; set; }
        public string? name { get; set; }
        public string? url { get; set; }
        public string? imageUrl { get; set; }
        public string? imageLocalPath { get; set; }
        public string? bio { get; set; }
        public DateTime? birthday { get; set; }
        public DateTime? deathDate { get; set; }
        public bool? isDead { get; set; }
        public string? trivia { get; set; }
        public string? quote { get; set; }
        public float? heightCm { get; set; }
    }

    public class TextWithId
    {
        public string id { get; set; } = "";
        public string? text { get; set; }

        public override int GetHashCode()
        {
            return id.GetHashCode();
        }

        public override bool Equals(object? obj)
        {
            var other = obj as TextWithId;
            if (other == null) return false;
            return id == other.id && text == other.text;
        }
    }

    public class Money
    {
        public long? amount { get; set; }
        public string? currency { get; set; }
    }
}
