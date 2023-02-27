namespace StudyProjectBackend
{
    public class Employee
    {
        public long Id { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Email { get; set; } = "";
        public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
        public DateTime HireDate { get; set; } = DateTime.MinValue;
        public float Wage { get; set; }
        public string PhoneNumber { get; set; } = "";
        public string SocialSecurityNumber { get; set; } = "";
        public long Location { get; set; }
    }
}
