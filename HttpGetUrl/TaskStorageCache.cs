﻿using System.Collections.Concurrent;

namespace HttpGetUrl;

public class TaskStorageCache(StorageService storageService)
{
    private readonly StorageService _storageService = storageService;
    private readonly ConcurrentDictionary<string, TaskFile[]> _statusToBeSavedDeferred = new();
    private Task _running = null;

    public TaskFile GetNextTaskItemSequence(string taskId)
    {
        StringLock.LockString($"seq-{taskId}");
        try
        {
            var tasks = GetTaskItems(taskId);
            var taskItem = new TaskFile { TaskId = taskId, Seq = tasks.Length };
            SaveTaskStatusDeferred(taskItem);
            return taskItem;
        }
        finally
        {
            StringLock.ReleaseString($"seq-{taskId}");
        }
    }

    public TaskFile[] GetTaskItems(string taskId)
    {
        if (_statusToBeSavedDeferred.TryGetValue(taskId, out var task))
            return task;
        return _storageService.GetTaskItems(taskId);
    }

    public void SaveTaskStatusDeferred(TaskFile task, TaskStatus? status = null)
    {
        if (status != null)
            task.Status = (TaskStatus)status;
        StringLock.LockString($"task-{task.TaskId}");
        try
        {
            var tasks = GetTaskItems(task.TaskId);
            if (tasks == null)
                return;
            if (tasks.Length <= task.Seq)
                Array.Resize(ref tasks, task.Seq + 1);
            tasks[task.Seq] = task;
            if (_statusToBeSavedDeferred.ContainsKey(task.TaskId))
                _statusToBeSavedDeferred[task.TaskId] = tasks;
            else
                _statusToBeSavedDeferred.TryAdd(task.TaskId, tasks);
        }
        finally
        {
            StringLock.ReleaseString($"task-{task.TaskId}");
        }
        PluseSaveTask();
    }

    public void DeleteTask(string taskId)
    {
        StringLock.LockString($"task-{taskId}");
        try
        {
            _statusToBeSavedDeferred.TryRemove(taskId, out _);
        }
        finally
        {
            StringLock.ReleaseString($"task-{taskId}");
        }

        _storageService.DeleteTask(taskId);
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
                var item = _statusToBeSavedDeferred.Values.First();
                StringLock.LockString($"task-{item[0].TaskId}");
                try
                {
                    _statusToBeSavedDeferred.Remove(item[0].TaskId, out _);
                    _storageService.SaveTasks(item);
                }
                finally
                {
                    StringLock.ReleaseString($"task-{item[0].TaskId}");
                }
            }
            idleCount = 0;
        }
        _running = null;
    }
}