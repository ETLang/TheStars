using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace IMDBScraper.Source
{
    public class AggregateRating
    {
        public int ratingCount { get; set; }
        public float bestRating { get; set; }
        public float worstRating { get; set; }
        public float ratingValue { get; set; }
    }

    public class Trailer
    {
        public string? name { get; set; }
        public string? embedUrl { get; set; }
        public string? thumbnailUrl { get; set; }
        public string? url { get; set; }
        public string? description { get; set; }
        public string? duration { get; set; }
        public string? uploadDate { get; set; }
    }

    public class Talent
    {
        [JsonPropertyName("@type")]
        public string? type { get; set; }
        public string? url { get; set; }
        public string? name { get; set; }
    }

    public class Show
    {
        [JsonPropertyName("@type")]
        public string? type { get; set; }
        public string? url { get; set; }
        public string? image { get; set; }
        public string? description { get; set; }
        public AggregateRating? aggregateRating { get; set; }
        public string? contentRating { get; set; }
        public List<string>? genre { get; set; }
        public string? datePublished { get; set; }
        public string? keywords { get; set; }
        public Trailer? trailer { get; set; }
        public List<Talent>? actor { get; set; }
        public List<Talent>? director { get; set; }
        public List<Talent>? creator { get; set; }
        public string? duration { get; set; }
    }

    public class BigShow
    {
        public BigShowProps? props { get; set; }
    }

    public class BigShowProps
    {
        public PageProps? pageProps { get; set; }
    }

    public class PageProps
    {
        public AboveTheFoldData? aboveTheFoldData { get; set; }
        public MainColumnData? mainColumnData { get; set; }
    }

    public class AboveTheFoldData
    {
        public TitleType? titleType { get; set; }
        public List<CreditCategory>? principalCredits { get; set; }
    }

    public class TitleType
    {
        public string? text { get; set; }
        public string? id { get; set; }
        public bool? isSeries { get; set; }
        public bool? isEpisode { get; set; }
    }

    public class CreditCategory
    {
        public TextWithId? category { get; set; }
        public List<CreditedPerson>? credits { get; set; }
    }

    public class CreditedPerson
    {
        public CreditedName? name { get; set; }
    }

    public class CreditedName
    {
        public string? id { get; set; }
        public Text? nameText { get; set; }
    }

    public class MainColumnData
    {
        public Episodes? episodes { get; set; }
        public bool? isAdult { get; set; }
        public SpokenLanguageCollection? spokenLanguages { get; set; }
        public CountriesCollection? countriesOfOrigin { get; set; }
        public ProductionBudget? productionBudget { get; set; }
        public OpeningWeekendGross? openingWeekendGross { get; set; }
        public Gross? lifetimeGross { get; set; }
        public Gross? worldwideGross { get; set; }
        public bool? canHaveEpisodes { get; set; }
        public MarkdownCollection? trivia { get; set; }
        public MarkdownCollection? goofs { get; set; }
    }

    public class MarkdownCollection
    {
        public List<MarkdownEdge>? edges { get; set; }
    }

    public class MarkdownEdge
    {
        public MarkdownNode? node { get; set; }
    }

    public class MarkdownNode
    {
        public MarkdownText? text { get; set; }
    }

    public class MarkdownText
    {
        public string? plainText { get; set; }
        public string? plaidHtml { get; set; }
    }

    public class Episodes
    {
        public List<Season>? seasons { get; set; }
        public TotalEpisodeData? totalEpisodes { get; set; }
    }

    public class Season
    {
        public int? number { get; set; }
    }

    public class TotalEpisodeData
    {
        public int? total { get; set; }
    }

    public class CountriesCollection
    {
        public List<TextWithId>? countries { get; set; }
    }

    public class SpokenLanguageCollection
    {
        public List<TextWithId>? spokenLanguages { get; set; }
    }

    public class FilmingLocationsCollection
    {
        public List<Node>? edges { get; set; }
        public int? total { get; set; }
    }

    public class Node
    {
        public Text? node { get; set; }
    }

    public class Text
    {
        public string? text { get; set; }
    }

    public class OpeningWeekendGross
    {
        public Gross? gross { get; set; }
        public DateTime? weekendEndDate { get; set; }
    }

    public class Gross
    {
        public Money? total { get; set; }
    }

    public class ProductionBudget
    {
        public Money? budget { get; set; }
    }

    public class BigPerson
    {
        public PersonProps? props { get; set; }
    }

    public class PersonProps
    {
        public PersonPageProps? pageProps { get; set; }
    }

    public class PersonPageProps
    {
        public AboveTheFoldPersonData? aboveTheFold { get; set; }
        public MainColumnPersonData? mainColumnData { get; set; }
    }

    public enum DeathStatus
    {
        ALIVE,
        DEAD,
        PRESUMED_DEAD
    }

    public class AboveTheFoldPersonData
    {
        public Text? nameText { get; set; }
        public Image? primaryImage { get; set; }
        public NameBio? bio { get; set; }
        public DisplayableDate? birthDate { get; set; }
        public DisplayableDate? deathDate { get; set; }
        public DeathStatus? deathStatus { get; set; }
    }

    public class MainColumnPersonData
    {
        public Text? nameText { get; set; }
        public Image? primaryImage { get; set; }
        public PersonHeight? height { get; set; }
        public DisplayableArticleCollection? trivia { get; set; }
        public DisplayableArticleCollection? quotes { get; set; }
    }

    public class PersonHeight
    {
        public MarkdownDisplayable? displayableProperty { get; set; }
    }

    public class MarkdownDisplayable
    {
        public MarkdownText? value { get; set; }
    }

    public class Image
    {
        public string? url { get; set; }
    }

    public class NameBio
    {
        public MarkdownText? text { get; set; }
    }

    public class DisplayableDate
    {
        public DateTime? date { get; set; }
    }

    public class DisplayableArticleCollection
    {
        public List<DisplayableArticleEdge>? edges { get; set; }
    }

    public class DisplayableArticleEdge
    {
        public DisplayableArticleNode? node { get; set; }
    }

    public class DisplayableArticleNode
    {
        public DisplayableArticle? displayableArticle { get; set; }
    }

    public class DisplayableArticle
    {
        public MarkdownText? body { get; set; }
    }
}
