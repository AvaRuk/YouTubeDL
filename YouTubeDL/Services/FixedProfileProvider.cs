using YoutubeCulvert.Configuration;
using YoutubeCulvert.InnerTube;

namespace YouTubeDL.Services;

/// <summary>Forces one InnerTube client profile (diagnostics / workarounds).</summary>
public sealed class FixedProfileProvider(InnerTubeClientProfile profile) : IClientProfileProvider
{
    public InnerTubeClientProfile GetFor(InnerTubeRequest request, AuthContext auth) => profile;
}
