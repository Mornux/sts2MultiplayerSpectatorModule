using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 待批准申请保留可点击通知，只有链接响应鼠标并呈现轻微变色。
// Keep a clickable pending-request notice with link-only hover feedback.
public partial class SpectatorDanmaku
{
    private RichTextLabel? _requestNotice;
    private void RefreshWatchRequests()
    {
        if (_requestNotice == null)
        {
            _requestNotice = LogLabel(true);
            _requestNotice.Name = "WatchRequestNotice";
            _root.AddChild(_requestNotice);
            _requestNotice.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
            _requestNotice.OffsetLeft = 24; _requestNotice.OffsetRight = 900;
            _requestNotice.OffsetTop = -145; _requestNotice.OffsetBottom = -65;
            _requestNotice.MouseFilter = Control.MouseFilterEnum.Pass;
            void Text(bool hovered) => _requestNotice.Text = L.T("management.request_notice") +
                " [url=manage][u][color=#" + (hovered ? "fff0b0" : "edc87d") + "]" + L.T("management.request_link") + "[/color][/u][/url]";
            Text(false);
            _requestNotice.MetaHoverStarted += _ => { Text(true); _requestNotice.MouseDefaultCursorShape = Control.CursorShape.PointingHand; };
            _requestNotice.MetaHoverEnded += _ => { Text(false); _requestNotice.MouseDefaultCursorShape = Control.CursorShape.Arrow; };
            _requestNotice.MetaClicked += _ => SpectatorPanel.Instance?.OpenRequestManagement();
        }
        _requestNotice.Visible = _session.IsHost && _session.Requests.Any(pair => !pair.Value.Approved);
    }
}

public partial class SpectatorPanel
{
    // 通知入口和暂停菜单入口打开同一个原版管理页面。
    // Open the same native management page from either the notice or the pause menu.
    internal void OpenRequestManagement()
    {
        if (!IsHost || NRun.Instance == null) return;
        var menu = (NPauseMenu)NRun.Instance.GlobalUi.SubmenuStack.ShowScreen(MegaCrit.Sts2.Core.Nodes.Screens.CapstoneSubmenuType.PauseMenu);
        menu.Initialize(RunManager.Instance.DebugOnlyGetState()!);
        OpenManagement(menu);
    }
}
