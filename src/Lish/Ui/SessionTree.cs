using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Lish.Core;

namespace Lish.Ui
{
    /// <summary>폴더(하위 폴더 포함)별 세션 목록 (직접 그리는 TreeView). 폴더 노드의 Tag 는 폴더 경로 문자열.</summary>
    internal sealed class SessionTree : TreeView
    {
        private TreeNode _hover;
        private readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TreeNode> _folderNodes = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private bool _populating;

        // 끌어 놓기: 놓을 폴더 노드 강조 (최상위면 null)
        private TreeNode _dropNode;

        public event Action<HostProfile> ConnectRequested;
        /// <summary>끌어 놓기로 세션(HostProfile) 또는 폴더(경로 문자열)를 대상 폴더(null = 최상위)로 옮겨 달라는 요청.</summary>
        public event Action<object, string> MoveRequested;

        public SessionTree()
        {
            AllowDrop = true;
            DrawMode = TreeViewDrawMode.OwnerDrawAll;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Sidebar;
            ForeColor = Theme.Ink;
            Font = Theme.UiFont;
            ItemHeight = Theme.Scale(42);
            ShowLines = false;
            ShowPlusMinus = false;
            ShowRootLines = false;
            FullRowSelect = true;
            HideSelection = false;
            Indent = Theme.Scale(12);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        public HostProfile SelectedProfile => SelectedNode?.Tag as HostProfile;

        /// <summary>선택된 폴더 경로. 세션이 선택돼 있으면 그 세션의 폴더.</summary>
        public string SelectedGroup =>
            SelectedNode?.Tag is string g ? g : (SelectedNode?.Tag as HostProfile)?.Group;

        public string SelectedFolder => SelectedNode?.Tag as string;

        public void Populate(ProfileStore store, string filter)
        {
            var selectedProfile = SelectedProfile?.Name;
            var selectedFolder = SelectedFolder;
            var filtering = !string.IsNullOrWhiteSpace(filter);
            var needle = filter?.Trim();

            _populating = true;
            BeginUpdate();
            Nodes.Clear();
            _folderNodes.Clear();
            _counts.Clear();
            _hover = null;
            var items = store.All.Where(p => Matches(p, filter)).ToList();
            var folders = store.Folders.ToList();   // 상위 폴더가 항상 하위보다 먼저

            foreach (var f in folders)
            {
                var count = items.Count(p => FolderPath.IsUnder(p.Group, f));
                // 검색 중에는 맞는 세션이 있거나 (하위) 폴더 이름이 맞는 폴더만
                if (filtering && count == 0
                    && !folders.Any(x => FolderPath.IsUnder(x, f) && FolderPath.NameOf(x).IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0))
                    continue;
                var parentPath = FolderPath.ParentOf(f);
                TreeNodeCollection parent;
                if (parentPath == null)
                    parent = Nodes;
                else if (_folderNodes.TryGetValue(parentPath, out var pn))
                    parent = pn.Nodes;
                else
                    continue;
                var node = new TreeNode(FolderPath.NameOf(f)) { Tag = f };
                parent.Add(node);
                _folderNodes[f] = node;
                _counts[f] = count;
                if (string.Equals(f, selectedFolder, StringComparison.OrdinalIgnoreCase))
                    SelectedNode = node;
            }

            foreach (var p in items)
            {
                TreeNodeCollection parent = Nodes;
                if (p.Group != null && _folderNodes.TryGetValue(p.Group, out var fn))
                    parent = fn.Nodes;
                var n = new TreeNode(p.Name) { Tag = p };
                parent.Add(n);
                if (p.Name == selectedProfile)
                    SelectedNode = n;
            }

            // 자식을 다 넣은 뒤 펼친다 (빈 노드를 먼저 펼치면 상태가 남지 않음)
            foreach (var kv in _folderNodes)
                if (filtering || !_collapsed.Contains(kv.Key))
                    kv.Value.Expand();
            EndUpdate();
            _populating = false;
        }

        /// <summary>폴더 노드를 선택하고 보이게 한다 (상위 폴더 펼침).</summary>
        public void SelectFolder(string path)
        {
            if (path == null || !_folderNodes.TryGetValue(path, out var node))
                return;
            for (var p = node.Parent; p != null; p = p.Parent)
                p.Expand();
            SelectedNode = node;
            node.EnsureVisible();
        }

        /// <summary>폴더 이름 변경/이동 후 접힘 상태를 새 경로로 옮긴다.</summary>
        public void FolderMoved(string oldPath, string newPath)
        {
            var moved = _collapsed.Where(c => FolderPath.IsUnder(c, oldPath)).ToList();
            foreach (var c in moved)
            {
                _collapsed.Remove(c);
                if (newPath != null)
                    _collapsed.Add(FolderPath.Rebase(c, oldPath, newPath));
            }
        }

        private static bool Matches(HostProfile p, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;
            filter = filter.Trim();
            return (p.Name ?? "").IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
                   || (p.Host ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                   || (p.User ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                   || (p.Group ?? "").IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        protected override void OnDrawNode(DrawTreeNodeEventArgs e)
        {
            var g = e.Graphics;
            var bounds = new Rectangle(0, e.Bounds.Y, ClientSize.Width, e.Bounds.Height);
            if (bounds.Height <= 0)
                return;
            using (var bg = new SolidBrush(BackColor))
                g.FillRectangle(bg, bounds);
            Theme.Smooth(g);

            var selected = e.Node == SelectedNode;
            var hover = e.Node == _hover;
            var card = new RectangleF(bounds.X + Theme.Scale(8), bounds.Y + Theme.Scale(2), bounds.Width - Theme.Scale(16), bounds.Height - Theme.Scale(4));
            if (selected || hover)
            {
                using (var path = Theme.RoundRect(card, Theme.Scale(8)))
                using (var b = new SolidBrush(selected ? Theme.PrimarySoft : Color.FromArgb(0xF3, 0xF1, 0xFA)))
                    g.FillPath(b, path);
            }
            if (e.Node == _dropNode)
            {
                using (var path = Theme.RoundRect(card, Theme.Scale(8)))
                using (var pen = new Pen(Theme.Primary, Theme.Scale(2)))
                    g.DrawPath(pen, path);
            }

            var indent = e.Node.Level * Theme.Scale(14);
            var x = (int)card.X + Theme.Scale(8) + indent;

            if (e.Node.Tag is string folder)
            {
                if (e.Node.Nodes.Count > 0)
                    Theme.DrawIcon(g, e.Node.IsExpanded ? Theme.IconChevronDown : Theme.IconChevronRight, Theme.IconsSmall, Theme.SubText,
                        new Rectangle(x, bounds.Y, Theme.Scale(14), bounds.Height));
                Theme.DrawIcon(g, Theme.IconFolder, Theme.Icons, Theme.Accent, new Rectangle(x + Theme.Scale(16), bounds.Y, Theme.Scale(20), bounds.Height));
                TextRenderer.DrawText(g, e.Node.Text, Theme.UiBold, new Rectangle(x + Theme.Scale(40), bounds.Y, bounds.Width - x - Theme.Scale(80), bounds.Height),
                    Theme.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                // 하위 폴더까지 합친 세션 수
                var count = (_counts.TryGetValue(folder, out var c) ? c : 0).ToString();
                TextRenderer.DrawText(g, count, Theme.Small, new Rectangle(0, bounds.Y, (int)card.Right - Theme.Scale(10), bounds.Height),
                    Theme.Faint, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
                return;
            }

            if (!(e.Node.Tag is HostProfile p))
                return;

            // 아이콘 배지
            var badge = new RectangleF(x, bounds.Y + (bounds.Height - Theme.Scale(26)) / 2f, Theme.Scale(26), Theme.Scale(26));
            using (var path = Theme.RoundRect(badge, Theme.Scale(6)))
            using (var b = new SolidBrush(selected ? Theme.Primary : Color.White))
            {
                g.FillPath(b, path);
                if (!selected)
                    using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
            }
            Theme.DrawIcon(g, Theme.IconTerminal, Theme.IconsSmall, selected ? Color.White : Theme.Primary, Rectangle.Round(badge));

            var tx = (int)badge.Right + Theme.Scale(10);
            var tw = (int)card.Right - tx - Theme.Scale(6);
            var mid = bounds.Y + bounds.Height / 2;
            TextRenderer.DrawText(g, p.Name, selected ? Theme.UiBold : Theme.UiFont, new Rectangle(tx, mid - Theme.Scale(17), tw, Theme.Scale(18)),
                Theme.Ink, TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            var sub = p.Target + (string.IsNullOrEmpty(p.Jump) ? "" : "  ↪ " + p.Jump);
            TextRenderer.DrawText(g, sub, Theme.Small, new Rectangle(tx, mid + Theme.Scale(1), tw, Theme.Scale(16)),
                Theme.SubText, TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var n = GetNodeAt(e.Location);
            if (n != _hover)
            {
                var old = _hover;
                _hover = n;
                if (old != null) Invalidate(new Rectangle(0, old.Bounds.Y, ClientSize.Width, ItemHeight));
                if (n != null) Invalidate(new Rectangle(0, n.Bounds.Y, ClientSize.Width, ItemHeight));
                Cursor = n != null ? Cursors.Hand : Cursors.Default;
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = null;
            Invalidate();
        }

        protected override void OnNodeMouseClick(TreeNodeMouseClickEventArgs e)
        {
            base.OnNodeMouseClick(e);
            SelectedNode = e.Node;
            if (e.Button == MouseButtons.Left && e.Node.Tag is string)
            {
                if (e.Node.IsExpanded)
                    e.Node.Collapse();
                else
                    e.Node.Expand();
            }
        }

        // 접힘 상태는 클릭·키보드(←/→) 모두 여기서 기억한다
        protected override void OnAfterCollapse(TreeViewEventArgs e)
        {
            base.OnAfterCollapse(e);
            if (!_populating && e.Node.Tag is string folder)
                _collapsed.Add(folder);
        }

        protected override void OnAfterExpand(TreeViewEventArgs e)
        {
            base.OnAfterExpand(e);
            if (!_populating && e.Node.Tag is string folder)
                _collapsed.Remove(folder);
        }

        // ───────────── 끌어 놓기 (세션·폴더 이동) ─────────────

        protected override void OnItemDrag(ItemDragEventArgs e)
        {
            base.OnItemDrag(e);
            if (e.Button == MouseButtons.Left && e.Item is TreeNode node)
            {
                SelectedNode = node;
                DoDragDrop(node, DragDropEffects.Move);
                SetDropTarget(null);
            }
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            var dragged = e.Data.GetData(typeof(TreeNode)) as TreeNode;
            if (dragged == null || dragged.TreeView != this)
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            var pt = PointToClient(new Point(e.X, e.Y));
            AutoScrollWhileDragging(pt);
            var target = DropFolderAt(pt, out var targetNode);
            if (CanDrop(dragged.Tag, target))
            {
                e.Effect = DragDropEffects.Move;
                SetDropTarget(targetNode);
            }
            else
            {
                e.Effect = DragDropEffects.None;
                SetDropTarget(null);
            }
        }

        protected override void OnDragLeave(EventArgs e)
        {
            base.OnDragLeave(e);
            SetDropTarget(null);
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            SetDropTarget(null);
            if (!(e.Data.GetData(typeof(TreeNode)) is TreeNode dragged) || dragged.TreeView != this)
                return;
            var target = DropFolderAt(PointToClient(new Point(e.X, e.Y)), out _);
            if (CanDrop(dragged.Tag, target))
                MoveRequested?.Invoke(dragged.Tag, target);
        }

        /// <summary>놓을 위치의 폴더. 폴더 위 = 그 폴더, 세션 위 = 세션의 폴더, 빈 곳 = 최상위(null).</summary>
        private string DropFolderAt(Point pt, out TreeNode folderNode)
        {
            var n = GetNodeAt(pt);
            if (n?.Tag is HostProfile)
                n = n.Parent;
            folderNode = n;
            return n?.Tag as string;
        }

        private static bool CanDrop(object item, string target)
        {
            if (item is HostProfile p)
                return !string.Equals(p.Group, target, StringComparison.OrdinalIgnoreCase);
            if (item is string folder)
                return !FolderPath.IsUnder(target, folder)
                       && !string.Equals(FolderPath.ParentOf(folder), target, StringComparison.OrdinalIgnoreCase);
            return false;
        }

        private void SetDropTarget(TreeNode node)
        {
            if (node == _dropNode)
                return;
            _dropNode = node;
            Invalidate();
        }

        private void AutoScrollWhileDragging(Point pt)
        {
            var edge = ItemHeight / 2;
            var top = TopNode;
            if (top == null)
                return;
            if (pt.Y < edge && top.PrevVisibleNode != null)
                TopNode = top.PrevVisibleNode;
            else if (pt.Y > ClientSize.Height - edge && top.NextVisibleNode != null)
                TopNode = top.NextVisibleNode;
        }

        protected override void OnNodeMouseDoubleClick(TreeNodeMouseClickEventArgs e)
        {
            base.OnNodeMouseDoubleClick(e);
            if (e.Node.Tag is HostProfile p)
                ConnectRequested?.Invoke(p);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter && SelectedProfile != null)
            {
                e.Handled = e.SuppressKeyPress = true;
                ConnectRequested?.Invoke(SelectedProfile);
            }
        }

        protected override void OnAfterSelect(TreeViewEventArgs e)
        {
            base.OnAfterSelect(e);
            Invalidate();
        }
    }
}
