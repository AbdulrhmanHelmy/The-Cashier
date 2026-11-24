namespace AlKhayal.Core.Models
{
    public sealed class CustomerPage
    {
        public System.Collections.Generic.IReadOnlyList<Customer> Items { get; set; }
        public long TotalCount { get; set; }
        public long Page { get; set; }
        public int PageSize { get; set; }
    }

    public sealed class Customer
    {
        public long Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string SecondaryPhone { get; set; }
        public string Address { get; set; }
        public decimal Balance { get; set; }
        public decimal CreditLimit { get; set; }
        public string Subscription { get; set; }
        public string Status { get; set; }
    }
}
