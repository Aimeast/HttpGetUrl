namespace HttpGetUrl;

public class TaskFile
{
    public string DownloaderType { get; set; }
    public string TaskId { get; set; }
    public Uri Url { get; set; }
    public Uri Referrer { get; set; }
    public int Seq { get; set; }    // Start with 0
    public string ContentText { get; set; }
    public string FileName { get; set; }    // null means no file name specified
    public long EstimatedLength { get; set; }   // -1 means unknown length.
    public long DownloadedLength { get; set; }
    public TaskStatus Status { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsHide { get; set; }
    public int ParentSeq { get; set; }
    public string ErrorMessage { get; set; }
}
