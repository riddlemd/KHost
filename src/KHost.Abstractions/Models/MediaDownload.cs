namespace KHost.Abstractions.Models
{
    public class MediaDownload
    {
        public required Media Media { get; set; }

        public string Message { get; set; } = string.Empty;

        public MediaDownloadStatus Status = MediaDownloadStatus.Downloading;

        public enum MediaDownloadStatus
        {
            Unknown,
            Downloading,
            Completed,
            Cancelced,
            Error
        }
    }
}
