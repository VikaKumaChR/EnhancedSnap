using System.Numerics;
using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Core.Localization;
using EnhancedSnap.Engine.Overlay;
using Windows.UI.Composition;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Ui;

/// <summary>
/// Snap Assist: fills the next empty zone of a snap group with live thumbnails of the other open windows.
/// Pick one with the mouse or the keyboard (arrows, Enter, Esc) — the chosen window joins the group and the
/// assist moves on to the next empty zone, like Windows Snap Assist.
/// </summary>
internal sealed class SnapAssist(CompositionHost host, DrawingSurfaces surfaces) : PopupWindow(host)
{
    private readonly ThumbnailSet _thumbnails = new();
    private readonly List<Tile> _tiles = [];
    private List<nint> _candidates = [];
    private int _hover = -1;
    private int _selected = -1;

    public event Action<nint>? Chosen;

    public event Action? Dismissed;

    public void ShowIn(RectI zone, IReadOnlyList<nint> candidates, FluentTheme theme, float scale)
    {
        // UPDATE: Paging or scrolling for more than nine candidates (large or spanned layouts).
        _candidates = candidates.Take(9).ToList();
        _hover = -1;
        _selected = 0;
        ShowAt(zone, theme, scale);
    }

    public void HandleKey(int vk, bool shift)
    {
        if (_tiles.Count == 0)
        {
            Dismissed?.Invoke();
            return;
        }

        int columns = _tiles.Count(t => t.Row == 0);
        int next = _selected;
        switch (vk)
        {
            case VK_ESCAPE:
                Dismissed?.Invoke();
                return;
            case VK_RETURN:
            case VK_SPACE:
                Chosen?.Invoke(_tiles[Math.Max(0, _selected)].Window);
                return;
            case VK_LEFT:
                next--;
                break;
            case VK_RIGHT:
                next++;
                break;
            case VK_UP:
                next -= columns;
                break;
            case VK_DOWN:
                next += columns;
                break;
            case VK_TAB:
                next += shift ? -1 : 1;
                break;
        }

        Select((next + _tiles.Count) % _tiles.Count);
    }

    public override void Hide()
    {
        _thumbnails.Clear();
        base.Hide();
    }

    protected override void Build(int width, int height)
    {
        _thumbnails.Clear();
        _tiles.Clear();
        float padding = Px(16);
        float titleHeight = Px(28);
        float spacing = Px(12);

        var title = surfaces.Text(Strings.Current.Assist_Title, (int)(width - (padding * 2)), (int)titleHeight, Px(14), true, Theme.Text);
        AddSurface(title, new Vector2(padding, padding), new Vector2(width - (padding * 2), titleHeight));

        int count = _candidates.Count;
        if (count == 0)
        {
            return;
        }

        float areaLeft = padding;
        float areaTop = padding + titleHeight + Px(8);
        float areaWidth = width - (padding * 2);
        float areaHeight = height - areaTop - padding;
        int columns = Math.Clamp((int)Math.Round(Math.Sqrt(count * (areaWidth / Math.Max(1, areaHeight)) / 1.3)), 1, count);
        int rows = (int)Math.Ceiling(count / (double)columns);
        float tileWidth = (areaWidth - ((columns - 1) * spacing)) / columns;
        float tileHeight = MathF.Min((areaHeight - ((rows - 1) * spacing)) / rows, tileWidth * 0.82f);
        float header = Px(32);

        for (int i = 0; i < count; i++)
        {
            int column = i % columns;
            int row = i / columns;
            var offset = new Vector2(areaLeft + (column * (tileWidth + spacing)), areaTop + (row * (tileHeight + spacing)));
            var size = new Vector2(tileWidth, tileHeight);
            var (card, shape, _) = Shapes.RoundedRect(C, offset, size, Px(8), Theme.CardFill, Theme.CardStroke);
            Root.Children.InsertAtTop(card);

            nint window = _candidates[i];
            float iconSize = Px(16);
            var icon = WindowIcons.GetIcon(window, out bool owned);
            var iconSurface = surfaces.Icon(icon, (int)iconSize);
            if (owned && icon != 0)
            {
                DestroyIcon(icon);
            }

            AddSurface(iconSurface, offset + new Vector2(Px(10), (header - iconSize) / 2), new Vector2(iconSize, iconSize));
            float textLeft = Px(10) + iconSize + Px(8);
            var text = surfaces.Text(WindowIcons.GetTitle(window), (int)(tileWidth - textLeft - Px(10)), (int)header, Px(12), false, Theme.Text);
            AddSurface(text, offset + new Vector2(textLeft, 0), new Vector2(tileWidth - textLeft - Px(10), header));

            var preview = RectI.FromSize((int)(offset.X + Px(8)), (int)(offset.Y + header), (int)(tileWidth - Px(16)), (int)(tileHeight - header - Px(8)));
            if (!_thumbnails.Add(Hwnd, window, preview))
            {
                // Minimized windows have no live content: show the app icon large instead.
                float big = MathF.Min(Px(48), MathF.Min(preview.Width, preview.Height));
                var bigIcon = WindowIcons.GetIcon(window, out bool bigOwned);
                var bigSurface = surfaces.Icon(bigIcon, (int)big);
                if (bigOwned && bigIcon != 0)
                {
                    DestroyIcon(bigIcon);
                }

                AddSurface(bigSurface, new Vector2(preview.Left + ((preview.Width - big) / 2), preview.Top + ((preview.Height - big) / 2)), new Vector2(big, big));
            }

            var screen = RectI.FromSize(Bounds.Left + (int)offset.X, Bounds.Top + (int)offset.Y, (int)tileWidth, (int)tileHeight);
            _tiles.Add(new Tile(window, screen, shape, row));
        }

        Select(0);
    }

    protected override void OnPointerMoved(PointI screen)
    {
        int hit = _tiles.FindIndex(t => t.Bounds.Contains(screen));
        if (hit >= 0)
        {
            Select(hit);
        }
    }

    protected override void OnReleased(PointI screen)
    {
        int hit = _tiles.FindIndex(t => t.Bounds.Contains(screen));
        if (hit >= 0)
        {
            Chosen?.Invoke(_tiles[hit].Window);
        }
    }

    private void Select(int index)
    {
        if (index == _selected && _hover == index)
        {
            return;
        }

        if (_selected >= 0 && _selected < _tiles.Count)
        {
            _tiles[_selected].Shape.FillBrush = C.CreateColorBrush(Theme.CardFill);
            _tiles[_selected].Shape.StrokeBrush = C.CreateColorBrush(Theme.CardStroke);
            _tiles[_selected].Shape.StrokeThickness = MathF.Max(1, Px(1));
        }

        _selected = index;
        _hover = index;
        if (index >= 0 && index < _tiles.Count)
        {
            _tiles[index].Shape.FillBrush = C.CreateColorBrush(Theme.CardHover);
            _tiles[index].Shape.StrokeBrush = C.CreateColorBrush(Theme.Accent);
            _tiles[index].Shape.StrokeThickness = Px(2);
        }
    }

    private sealed record Tile(nint Window, RectI Bounds, CompositionSpriteShape Shape, int Row);
}
