using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace StudyProjectBackend
{
    public enum KnownLocationIds : long
    {
        YakimaCallCenter = 1099,
        IssaquahCallCenter = 892,
        HomeOffice = 099
    }

    public enum LocationType
    {
        Office,
        CallCenter,
        Warehouse
    }

    public class Location
    {
        public long Id { get; set; }

        public LocationType Type { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string PostalCode { get; set; }
        public string Country { get; set; }
    }
}
