using System.Text;

namespace PyMCL.Services;

public sealed class TaskRow
{
    public string Id { get; init; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = L("排队中…");
    public string Speed { get; set; } = "";
    public double Progress { get; set; }
    public bool Indeterminate { get; set; } = true;
    public bool Finished { get; set; }
    public bool Success { get; set; }
    public DateTime Started { get; } = DateTime.Now;
    public StringBuilder Log { get; } = new();
    public int LogLines;

    public void Append(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        Log.AppendLine(line);
        LogLines++;
        if (LogLines <= 600) return;
        // 单任务日志上限，长下载不至于把内存吃光。
        var text = Log.ToString();
        var cut = text.IndexOf('\n', text.Length / 3);
        Log.Clear();
        Log.Append(cut > 0 ? text.Substring(cut + 1) : text);
        LogLines = 400;
    }
}

/// <summary>任务总线。下载坞、任务页、侧栏徽章都读这一份。</summary>
public static class TaskStore
{
    /// <summary>task_added 没带标题时的占位。界面看到它会去问桥要真标题（task_title）。</summary>
    public static string PlaceholderTitle => L("任务");

    private static readonly List<TaskRow> _rows = new();

    public static IReadOnlyList<TaskRow> Rows => _rows;
    public static event Action<TaskRow>? Added;
    public static event Action<TaskRow>? Updated;
    public static event Action? Cleared;

    public static int RunningCount => _rows.Count(r => !r.Finished);

    public static TaskRow? Get(string id) => _rows.FirstOrDefault(r => r.Id == id);

    public static TaskRow? Newest => _rows.LastOrDefault(r => !r.Finished) ?? _rows.LastOrDefault();

    public static void Handle(BridgeEvent ev)
    {
        switch (ev.Event)
        {
            case "task_added":
            {
                if (string.IsNullOrEmpty(ev.TaskId) || Get(ev.TaskId) != null) return;
                var row = new TaskRow { Id = ev.TaskId, Title = string.IsNullOrEmpty(ev.Title) ? PlaceholderTitle : ev.Title };
                _rows.Add(row);
                if (_rows.Count > 60) _rows.RemoveRange(0, _rows.Count - 60);
                Added?.Invoke(row);
                break;
            }
            case "progress":
            {
                var row = Get(ev.TaskId);
                if (row is null) return;
                Fmt.SplitMsg(ev.Message, out var st, out var sp);
                row.Status = string.IsNullOrEmpty(st) ? row.Status : st;
                row.Speed = sp;
                row.Indeterminate = ev.Total <= 0;
                if (ev.Total > 0) row.Progress = Clamp.Of(ev.Current * 100.0 / ev.Total, 0, 100);
                Updated?.Invoke(row);
                break;
            }
            case "log":
            {
                var row = Get(ev.TaskId);
                if (row is null) return;
                row.Append(ev.Text);
                Updated?.Invoke(row);
                break;
            }
            case "finished":
            {
                var row = Get(ev.TaskId);
                if (row is null) return;
                row.Finished = true;
                row.Success = ev.Success;
                row.Indeterminate = false;
                row.Progress = ev.Success ? 100 : row.Progress;
                row.Status = string.IsNullOrEmpty(ev.Message) ? (ev.Success ? L("已完成") : L("失败")) : ev.Message;
                row.Speed = "";
                Updated?.Invoke(row);
                break;
            }
        }
    }

    public static void ClearFinished()
    {
        _rows.RemoveAll(r => r.Finished);
        Cleared?.Invoke();
    }

    /// <summary>
    /// 断线对账（bridge/api.py:2687 的 list_tasks，C 桥 backend.c 同形）。
    /// SSE 断开的那段窗口里 task_added / finished 是彻底丢掉的，没有别的地方能补：
    /// 不跑这一趟，断线期间完成的下载会永远停在「进行中」，新起的任务根本不出现。
    /// 返回是否有任何一行被改动，调用方据此决定要不要刷 UI。
    /// </summary>
    public static bool Reconcile(IReadOnlyList<(string Id, string Title)> running,
                                 IReadOnlyList<(string Id, bool Success, string Message)> finished)
    {
        var changed = false;

        // 1) 桥说在跑的，本地没有就补一行（断线期间新起的任务）
        foreach (var (id, title) in running)
        {
            if (string.IsNullOrEmpty(id) || Get(id) != null) continue;
            var row = new TaskRow { Id = id, Title = string.IsNullOrEmpty(title) ? PlaceholderTitle : title };
            _rows.Add(row);
            Added?.Invoke(row);
            changed = true;
        }

        // 2) 桥已经给了结果的：本地还当「进行中」的按结果收口；本地整个没见过的补一行
        foreach (var (id, success, message) in finished)
        {
            if (string.IsNullOrEmpty(id)) continue;
            var row = Get(id);
            if (row is null)
            {
                row = new TaskRow { Id = id, Title = PlaceholderTitle };
                _rows.Add(row);
                Added?.Invoke(row);
            }
            else if (row.Finished && row.Success == success)
            {
                continue;   // 已经对上了，别重复通知
            }
            row.Finished = true;
            row.Success = success;
            row.Indeterminate = false;
            row.Progress = success ? 100 : row.Progress;
            row.Status = string.IsNullOrEmpty(message) ? (success ? L("已完成") : L("失败")) : message;
            row.Speed = "";
            Updated?.Invoke(row);
            changed = true;
        }

        // 3) 桥那边既不在跑、也没有结果的「进行中」行：list_tasks 的 finished 只保留
        //    最近几十条，翻页翻掉的老任务是正常现象。标成失败会误报，所以原样留着，
        //    交给用户手动清（ClearFinished）。
        if (_rows.Count > 60) _rows.RemoveRange(0, _rows.Count - 60);
        if (changed) Cleared?.Invoke();
        return changed;
    }
}
