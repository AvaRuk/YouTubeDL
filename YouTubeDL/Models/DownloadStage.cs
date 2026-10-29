namespace YouTubeDL.Models;

public enum DownloadStage
{
    Resolving = 0,
    DownloadingVideo = 1,
    DownloadingAudio = 2,
    Muxing = 3,
    Converting = 4,
    SavingArtifacts = 5,
    Done = 6
}
