﻿using System.Collections.Concurrent;

namespace HttpGetUrl;

public class TaskService(IConfiguration configuration, ILogger<TaskService> logger)
{
    private readonly object _lock = new();
    private readonly ConcurrentQueue<TaskInfo> _queue = new();
    private readonly ConcurrentDictionary<string, TaskInfo> _running = new();
    private readonly ILogger<TaskService> _logger = logger;

    private int _maxConcurrency = configuration.GetValue("Hget:MaxConcurrentDownloads", 2);

    public int RemainedConcurrency => _maxConcurrency;

    public void QueueTask(TaskInfo task)
    {
        _queue.Enqueue(task);
        PluseTask();
    }

    private void PluseTask()
    {
        if (_maxConcurrency > 0)
            lock (_lock)
                if (_maxConcurrency > 0)
                {
                    _maxConcurrency--;
                    Task.Run(Worker);
                }
    }

    private async Task Worker()
    {
        while (_queue.TryDequeue(out TaskInfo taskInfo))
        {
            if (taskInfo.Cts.IsCancellationRequested)
            {
                continue;
            }

            var id = $"{taskInfo.TaskId}.{taskInfo.Seq}";
            try
            {
                _running.TryAdd(id, taskInfo);
                await taskInfo.Action();
            }
            catch (Exception ex)
            {
                taskInfo.Exception = ex;
                _logger.LogError($"{id} {ex.Message}");
            }
            finally
            {
                _running.TryRemove(id, out _);
            }
        }
        lock (_lock)
        {
            _maxConcurrency++;
        }
    }

    public void CancelTask(string taskId)
    {
        foreach (var info in _queue.Where(x => x.TaskId == taskId))
            info.Cts.Cancel();
        foreach (var info in _running.Values.Where(x => x.TaskId == taskId))
            info.Cts.Cancel();
    }

    public string[] ListTasks()
    {
        var list = new List<string>();
        foreach (var info in _running.Values)
            list.Add($"_running {info.TaskId}.{info.Seq}:{info.Exception};");
        foreach (var info in _queue)
            list.Add($"_queue {info.TaskId}.{info.Seq}:{info.Exception};");
        return list.ToArray();
    }

    public class TaskInfo(string taskId, int seq, Func<Task> action, CancellationTokenSource cts)
    {
        public string TaskId = taskId;
        public int Seq = seq;
        public Func<Task> Action = action;
        public CancellationTokenSource Cts { get; set; } = cts;
        public Exception Exception { get; set; }
    }
}