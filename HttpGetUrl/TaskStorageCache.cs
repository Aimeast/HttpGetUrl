using HttpGetUrl.Models;
using System.Collections.Concurrent;

namespace HttpGetUrl;

public class TaskStorageCache(StorageService storageService)
{
    private readonly StorageService _storageService = storageService;
    private readonly ConcurrentDictionary<string, TaskFile[]> _statusToBeSavedDeferred = new();
    private Task _running = null;

    public TaskFile GetExistTaskItem(string userSpace, string taskId, Uri url)
    {
        var tasks = GetTaskItems(userSpace, taskId);
        return tasks.Skip(1).FirstOrDefault(x => x.Url == url);
    }

    public TaskFile GetNextTaskItemSequence(string userSpace, string taskId)
    {
        using (StringLock.LockString($"seq-{userSpace}.{taskId}"))
        {
            var tasks = GetTaskItems(userSpace, taskId);
            var taskItem = new TaskFile { UserSpace = userSpace, TaskId = taskId, Seq = tasks.Length, EstimatedLength = -1 };
            SaveTaskStatusDeferred(taskItem);
            return taskItem;
        }
    }

    public TaskFile[] GetTaskItems(string userSpace, string taskId)
    {
        if (_statusToBeSavedDeferred.TryGetValue($"{userSpace}.{taskId}", out var task))
            return task;
        return _storageService.GetTaskItems(userSpace, taskId);
    }

    public void SaveTaskStatusDeferred(TaskFile task, TaskStatus? status = null)
    {
        if (status != null)
            task.Status = (TaskStatus)status;
        var userSpace = task.UserSpace;
        using (StringLock.LockString($"task-{userSpace}.{task.TaskId}"))
        {
            var tasks = GetTaskItems(userSpace, task.TaskId);
            if (tasks == null)
                return;
            if (tasks.Length <= task.Seq)
                Array.Resize(ref tasks, task.Seq + 1);
            tasks[task.Seq] = task;
            if (_statusToBeSavedDeferred.ContainsKey($"{userSpace}.{task.TaskId}"))
                _statusToBeSavedDeferred[$"{userSpace}.{task.TaskId}"] = tasks;
            else
                _statusToBeSavedDeferred.TryAdd($"{userSpace}.{task.TaskId}", tasks);
        }
        PluseSaveTask();
    }

    public void FlushTasks(string userSpace, TaskFile[] tasks)
    {
        using (StringLock.LockString($"task-{userSpace}.{tasks[0].TaskId}"))
        {
            _statusToBeSavedDeferred.TryRemove($"{userSpace}.{tasks[0].TaskId}", out var _);
            _storageService.SaveTasks(userSpace, tasks);
        }
    }

    public void DeleteTask(string userSpace, string taskId)
    {
        using (StringLock.LockString($"task-{userSpace}.{taskId}"))
            _statusToBeSavedDeferred.TryRemove($"{userSpace}.{taskId}", out _);

        _storageService.DeleteTask(userSpace, taskId);
    }

    private void PluseSaveTask()
    {
        if (_running == null)
            lock (this)
                _running ??= Task.Run(() =>
                {
                    Thread.Sleep(2000);
                    InternalFlushTask();
                });
    }

    private void InternalFlushTask()
    {
        var idleCount = 0;
        while (idleCount++ < 5)
        {
            if (_statusToBeSavedDeferred.IsEmpty)
            {
                Thread.Sleep(2000);
                continue;
            }

            while (!_statusToBeSavedDeferred.IsEmpty)
            {
                var (key, value) = _statusToBeSavedDeferred.First();
                using (StringLock.LockString($"task-{key}"))
                {
                    _statusToBeSavedDeferred.Remove($"{key}", out _);
                    _storageService.SaveTasks(key.Substring(0, key.IndexOf('.')), value);
                }
            }
            idleCount = 0;
        }
        _running = null;
    }
}
