using StarCitizenUA.Services.Common;
using Xunit;

namespace StarCitizenUA.Tests
{
    public class IgnoreRulesProviderTests
    {
        [Fact(Skip = "Додати реальні перевірки після впровадження тестових даних.")]
        public void ShouldExposeDefaultIgnoredDirectories()
        {
            var provider = new IgnoreRulesProvider();

            var ignored = provider.GetIgnoredDirectories();

            Assert.Contains("Windows", ignored);
            Assert.True(provider.ShouldIgnore("Windows"));
        }
    }
}
