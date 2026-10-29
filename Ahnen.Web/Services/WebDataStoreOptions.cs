using Ahnen.Core.Services;

namespace Ahnen.Web.Services;

/// <summary>Maps web configuration to host-neutral storage settings: <c>data/</c> under the content root.</summary>
public static class WebDataStoreOptions
{
    public static DataStoreOptions Create(IConfiguration config, string contentRootPath) =>
        new(Path.Combine(contentRootPath, "data"), config["DataFileName"]);
}
