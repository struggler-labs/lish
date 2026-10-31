using System;
using Lish.Core;
using Xunit;

namespace Lish.Tests.Core
{
    public class AddressTests
    {
        [Theory]
        [InlineData("host", null, "host", 22)]
        [InlineData("  host  ", null, "host", 22)]
        [InlineData("root@10.0.0.5", "root", "10.0.0.5", 22)]
        [InlineData("user@example.com:2222", "user", "example.com", 2222)]
        [InlineData("[::1]:2200", null, "::1", 2200)]
        [InlineData("admin@[fe80::1]", "admin", "fe80::1", 22)]
        [InlineData("fe80::1", null, "fe80::1", 22)]                 // bare IPv6: colons are not a port
        [InlineData("me@corp.com@jump.local", "me@corp.com", "jump.local", 22)] // last '@' separates user
        [InlineData("@host", null, "host", 22)]
        public void Parse_ValidDestinations(string dest, string user, string host, int port)
        {
            var p = Address.Parse(dest);
            Assert.Equal(user, p.User);
            Assert.Equal(host, p.Host);
            Assert.Equal(port, p.Port);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("user@")]
        [InlineData("host:0")]
        [InlineData("host:65536")]
        [InlineData("host:ssh")]
        [InlineData("[::1")]
        [InlineData("bad host")]
        public void Parse_InvalidDestinations_Throw(string dest)
        {
            Assert.Throws<FormatException>(() => Address.Parse(dest));
        }

        [Fact]
        public void ResolveDestination_PrefersSavedSessionAndReturnsCopy()
        {
            var store = ProfileStore.Empty();
            var saved = new HostProfile { Name = "web", Host = "10.1.1.1", Port = 2022, User = "deploy" };
            store.Upsert(saved);

            var resolved = Address.ResolveDestination("WEB", store);
            Assert.NotSame(saved, resolved);
            Assert.Equal("10.1.1.1", resolved.Host);
            Assert.Equal(2022, resolved.Port);

            resolved.Host = "changed";
            Assert.Equal("10.1.1.1", saved.Host);
        }

        [Fact]
        public void ResolveDestination_FallsBackToParsing()
        {
            var resolved = Address.ResolveDestination("u@h:23", ProfileStore.Empty());
            Assert.Equal("u", resolved.User);
            Assert.Equal("h", resolved.Host);
            Assert.Equal(23, resolved.Port);
            Assert.Null(resolved.Name);
        }
    }
}
