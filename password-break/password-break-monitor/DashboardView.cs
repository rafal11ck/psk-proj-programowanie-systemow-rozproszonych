using System.Collections.ObjectModel;
using System.Data;
using Terminal.Gui;

namespace password_break_monitor;

public class DashboardView : Window
{
    private readonly MonitorState _state;
    private readonly Label _headerLabel;
    private readonly ProgressBar _taskBar;
    private readonly Label _taskLabel;
    private readonly FrameView _taskFrame;
    private readonly ProgressBar _foundBar;
    private readonly Label _foundLabel;
    private readonly FrameView _foundFrame;
    private readonly TableView _workersTable;
    private readonly DataTable _workersData;
    private readonly TableView _tasksTable;
    private readonly DataTable _tasksData;
    private readonly ListView _logList;
    private bool _showWorkers = true;
    private bool _showTasks = true;
    private bool _showLog = true;
    private TileView? _split;

    public DashboardView(MonitorState state, string serverUrl)
    {
        _state = state;
        Title = "Password Breaker Monitor (Ctrl+C to quit)";

        // Header
        _headerLabel = new Label
        {
            X = 1, Y = 0,
            Width = Dim.Fill(1),
            Height = 1,
            Text = $"Connecting to {serverUrl}..."
        };

        // Task progress
        _taskFrame = new FrameView
        {
            Title = "Tasks",
            X = 0, Y = 1,
            Width = Dim.Fill(),
            Height = 3
        };
        _taskBar = new ProgressBar
        {
            X = 0, Y = 0,
            Width = Dim.Percent(60),
            Height = 1,
            ProgressBarStyle = ProgressBarStyle.Continuous
        };
        _taskLabel = new Label
        {
            X = Pos.Right(_taskBar) + 1, Y = 0,
            Width = Dim.Fill(),
            Height = 1
        };
        _taskFrame.Add(_taskBar, _taskLabel);

        // Found progress
        _foundFrame = new FrameView
        {
            Title = "Found",
            X = 0, Y = Pos.Bottom(_taskFrame),
            Width = Dim.Fill(),
            Height = 3
        };
        _foundBar = new ProgressBar
        {
            X = 0, Y = 0,
            Width = Dim.Percent(60),
            Height = 1,
            ProgressBarStyle = ProgressBarStyle.Continuous
        };
        _foundLabel = new Label
        {
            X = Pos.Right(_foundBar) + 1, Y = 0,
            Width = Dim.Fill(),
            Height = 1
        };
        _foundFrame.Add(_foundBar, _foundLabel);

        // Workers table
        _workersData = new DataTable();
        _workersData.Columns.Add("Client");
        _workersData.Columns.Add("IP");
        _workersData.Columns.Add("Last seen");
        _workersData.Columns.Add("Timeout");

        _workersTable = new TableView
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            FullRowSelect = true,
            Style =
            {
                AlwaysShowHeaders = true,
                ExpandLastColumn = true,
                ShowHorizontalHeaderOverline = true,
                ShowHorizontalHeaderUnderline = true,
                ShowHorizontalBottomline = true,
                ShowVerticalHeaderLines = true,
                ShowVerticalCellLines = true,
            }
        };
        _workersTable.Table = new DataTableSource(_workersData);
        _workersTable.VerticalScrollBar.AutoShow = true;
        _workersTable.Style.ColumnStyles.Add(0, new() { MinWidth = 10 });
        _workersTable.Style.ColumnStyles.Add(1, new() { MinWidth = 15 });

        // Active Tasks table
        _tasksData = new DataTable();
        _tasksData.Columns.Add("Task");
        _tasksData.Columns.Add("Client");
        _tasksData.Columns.Add("Range");
        _tasksData.Columns.Add("Elapsed");

        _tasksTable = new TableView
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            FullRowSelect = true,
            Style =
            {
                AlwaysShowHeaders = true,
                ExpandLastColumn = true,
                ShowHorizontalHeaderOverline = true,
                ShowHorizontalHeaderUnderline = true,
                ShowHorizontalBottomline = true,
                ShowVerticalHeaderLines = true,
                ShowVerticalCellLines = true,
            }
        };
        _tasksTable.Table = new DataTableSource(_tasksData);
        _tasksTable.VerticalScrollBar.AutoShow = true;
        _tasksTable.Style.ColumnStyles.Add(2, new() { MinWidth = 20 });

        // Log
        _logList = new ListView
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _logList.VerticalScrollBar.AutoShow = true;
        _logList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == KeyCode.End) { _logList.MoveEnd(); e.Handled = true; }
            else if (e.KeyCode == KeyCode.Home) { _logList.MoveHome(); e.Handled = true; }
            else if (e.KeyCode == KeyCode.CursorUp && _logList.SelectedItem <= 0)
            {
                // Let focus escape upward to the previous tile instead of
                // being swallowed by the ListView selection.
                Application.Top?.AdvanceFocus(NavigationDirection.Backward, TabBehavior.TabStop);
                e.Handled = true;
            }
        };

        _split = BuildTileView();
        Add(_headerLabel, _taskFrame, _foundFrame, _split);

        // Toggle pane visibility — use Application.KeyDown so it works
        // regardless of which child view currently has focus.
        Application.KeyDown += OnAppKeyDown;
    }

    private void OnAppKeyDown(object? sender, Key e)
    {
        var k = e.KeyCode & ~KeyCode.ShiftMask;
        if (k == KeyCode.W)
        {
            _showWorkers = !_showWorkers;
            RebuildLayout();
            e.Handled = true;
        }
        else if (k == KeyCode.T)
        {
            _showTasks = !_showTasks;
            RebuildLayout();
            e.Handled = true;
        }
        else if (k == KeyCode.L)
        {
            _showLog = !_showLog;
            RebuildLayout();
            e.Handled = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Application.KeyDown -= OnAppKeyDown;
        base.Dispose(disposing);
    }

    public void RefreshData()
    {
        var s = _state.GetSnapshot();

        var status = s.Connected ? "Connected" : "Disconnected";
        var toggles = $"[W]orkers {(_showWorkers ? "on" : "off")} | [T]asks {(_showTasks ? "on" : "off")} | [L]og {(_showLog ? "on" : "off")}";
        _headerLabel.Text = $"Mode: {(string.IsNullOrEmpty(s.AttackMode) ? "?" : s.AttackMode)} | Targets: {s.TargetTotal} | {status} | {toggles}";

        _taskBar.Fraction = s.TaskFraction;
        _taskLabel.Text = $"{s.CompletedTasks}/{s.TotalTasks} ({s.InProgress} active, {s.PendingTasks} pending)";

        _foundBar.Fraction = s.FoundFraction;
        _foundLabel.Text = s.AllFound
            ? $"{s.FoundCount}/{s.TargetTotal} DONE{(s.Saved ? " [saved]" : "")}"
            : $"{s.FoundCount}/{s.TargetTotal} ({s.RemainingCount} remaining)";

        // Workers — update rows in-place, keep same DataTable
        _workersData.Rows.Clear();
        foreach (var c in s.Clients)
            _workersData.Rows.Add(c.ClientId, c.Ip, $"{c.SecondsAgo}s", $"{c.TimeoutRemaining}s");
        ClampRowOffset(_workersTable, _workersData);
        _workersTable.SetNeedsDraw();

        // Tasks — update rows in-place, keep same DataTable
        _tasksData.Rows.Clear();
        foreach (var t in s.ActiveTasks)
            _tasksData.Rows.Add(t.TaskId, t.ClientId, $"{t.StartIndex}-{t.EndIndex}", $"{t.ElapsedSeconds}s");
        ClampRowOffset(_tasksTable, _tasksData);
        _tasksTable.SetNeedsDraw();

        // Clamp table scroll so rows don't scroll out of view when they all fit
        static void ClampRowOffset(TableView table, DataTable data)
        {
            // Header decoration lines (overline + underline) take 2 rows, bottomline takes 1
            var visibleRows = table.Viewport.Height - 3;
            if (data.Rows.Count <= visibleRows)
                table.RowOffset = 0;
            else
                table.RowOffset = Math.Min(table.RowOffset, Math.Max(0, data.Rows.Count - visibleRows));
        }

        // Log — auto-scroll only if user is already at the bottom
        bool wasAtBottom = _logList.Source == null
            || _logList.TopItem + _logList.Viewport.Height >= _logList.Source.Count;
        _logList.SetSource(new ObservableCollection<string>(s.LogLines));
        if (wasAtBottom && s.LogLines.Count > 0)
            _logList.MoveEnd();
    }

    private TileView BuildTileView()
    {
        var visiblePanes = new List<(string Title, View Content)>();
        if (_showWorkers)
            visiblePanes.Add(("Workers", _workersTable));
        if (_showTasks)
            visiblePanes.Add(("Active Tasks", _tasksTable));
        if (_showLog)
            visiblePanes.Add(("Log", _logList));

        var split = new TileView(visiblePanes.Count)
        {
            X = 0, Y = Pos.Bottom(_foundFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Orientation = Orientation.Horizontal,
            LineStyle = LineStyle.Single
        };

        for (int i = 0; i < visiblePanes.Count; i++)
        {
            split.Tiles.ElementAt(i).Title = visiblePanes[i].Title;
            split.Tiles.ElementAt(i).ContentView!.Add(visiblePanes[i].Content);
        }

        if (visiblePanes.Count == 2)
        {
            split.SetSplitterPos(0, Pos.Percent(50));
        }
        else if (visiblePanes.Count == 3)
        {
            split.SetSplitterPos(0, Pos.Percent(33));
            split.SetSplitterPos(1, Pos.Percent(67));
        }

        return split;
    }

    private void RebuildLayout()
    {
        // Ensure at least one pane is visible
        if (!_showWorkers && !_showTasks && !_showLog)
        {
            _showLog = true;
        }

        if (_split != null)
        {
            foreach (var tile in _split.Tiles)
            {
                tile.ContentView?.RemoveAll();
            }
            Remove(_split);
            _split.Dispose();
        }

        _split = BuildTileView();
        Add(_split);
    }
}