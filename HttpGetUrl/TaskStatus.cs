namespace HttpGetUrl;

public enum TaskStatus
{
    Error = -1,
    Pending = 0,
    NotFound = 1,
    Downloading = 2,
    Merging = 3,
    PartiallyCompleted = 4,
    Completed = 5,
}
