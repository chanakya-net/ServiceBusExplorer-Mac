namespace SbMac.App.Services;

public sealed record UpdateInfo(
    Version InstalledVersion,
    Version AvailableVersion,
    Uri ReleaseUri);

public interface IUpdateChecker
{
    Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default);
}

public interface IAppVersionProvider
{
    Version? GetCurrentVersion();
}
