using StarCitizenUA.Services;
using Xunit;

namespace StarCitizenUA.Tests
{
    public class SettingsServiceTests
    {
        [Fact(Skip = "Налаштувати тестове оточення для ізольованих Settings.")]
        public void TrySetGameFolder_ShouldRejectInvalidPaths()
        {
            var service = new SettingsService();

            var result = service.TrySetGameFolder("Z:/non-existent-path");

            Assert.False(result);
        }
    }
}
