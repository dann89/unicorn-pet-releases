using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    public class SmokeTests
    {
        [Fact]
        public void Smoke_CoreAssemblyLoads()
        {
            Assert.NotNull(typeof(Settings).Assembly);
        }
    }
}
