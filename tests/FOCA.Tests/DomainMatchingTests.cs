using FOCA.Database.Entities;
using Xunit;

namespace FOCA.Tests
{
    public class DomainMatchingTests
    {
        [Theory]
        [InlineData("acme.com", "acme.com", true)]
        [InlineData("www.acme.com", "acme.com", true)]
        [InlineData("WWW.ACME.COM", "acme.com", true)]
        [InlineData("notacme.com", "acme.com", false)]
        [InlineData("acme.com.evil.net", "acme.com", false)]
        [InlineData("other.org", "acme.com", false)]
        [InlineData("www.acme.com", "", false)]   // document analysis project: no main domain
        [InlineData("www.acme.com", null, false)]
        [InlineData("", "acme.com", false)]
        public void IsSameOrSubdomain(string host, string domain, bool expected)
        {
            Assert.Equal(expected, Data.IsSameOrSubdomain(host, domain));
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("  ", false)]
        [InlineData("acme.com", true)]
        [InlineData("*", true)]
        public void HasDomain(string domain, bool expected)
        {
            Assert.Equal(expected, new Project { Domain = domain }.HasDomain);
        }
    }
}
