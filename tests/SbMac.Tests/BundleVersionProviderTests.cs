using SbMac.App.Services;

using Xunit;

namespace SbMac.Tests;

public sealed class BundleVersionProviderTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("0.0.0", 0, 0, 0)]
    public void ReadsThreePartVersion(string text, int major, int minor, int patch)
    {
        using var bundle = TestBundle.Create(text);

        var version = new BundleVersionProvider(bundle.ExecutableDirectory).GetCurrentVersion();

        Assert.Equal(new Version(major, minor, patch), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3")]
    [InlineData("one.two.three")]
    [InlineData("+2.3.4")]
    [InlineData("-0.2.3")]
    [InlineData("1. 2.3")]
    public void RejectsInvalidBundleVersion(string text)
    {
        using var bundle = TestBundle.Create(text);

        Assert.Null(new BundleVersionProvider(bundle.ExecutableDirectory).GetCurrentVersion());
    }

    [Fact]
    public void MissingBundleMetadataReturnsNull()
    {
        using var bundle = TestBundle.CreateWithoutInfoPlist();

        Assert.Null(new BundleVersionProvider(bundle.ExecutableDirectory).GetCurrentVersion());
    }

    sealed class TestBundle : IDisposable
    {
        TestBundle(string root)
        {
            Root = root;
            ExecutableDirectory = Path.Combine(root, "SB-Mac.app", "Contents", "MacOS");
            Directory.CreateDirectory(ExecutableDirectory);
        }

        public string Root { get; }
        public string ExecutableDirectory { get; }

        public static TestBundle Create(string version)
        {
            var bundle = CreateWithoutInfoPlist();
            File.WriteAllText(
                Path.Combine(bundle.ExecutableDirectory, "..", "Info.plist"),
                $"""<plist><dict><key>CFBundleShortVersionString</key><string>{version}</string></dict></plist>""");
            return bundle;
        }

        public static TestBundle CreateWithoutInfoPlist() =>
            new(Path.Combine(Path.GetTempPath(), $"sbmac-bundle-{Guid.NewGuid():N}"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
