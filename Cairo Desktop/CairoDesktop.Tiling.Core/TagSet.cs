using System;
using System.Collections.Generic;
using System.Linq;

namespace CairoDesktop.Tiling.Core
{
    public class Tag
    {
        public int Index { get; }

        public string Label { get; set; }

        public BspTree Tree { get; } = new BspTree();

        public List<long> Floating { get; } = new List<long>();

        /// <summary>Minimized windows are kept out of the tree until restored.</summary>
        public List<long> Minimized { get; } = new List<long>();

        /// <summary>The last-focused window, restored when the tag is viewed.</summary>
        public long? FocusedWindow { get; set; }

        public long? FullscreenWindow { get; set; }

        public bool IsUrgent { get; set; }

        public Tag(int index, string label)
        {
            Index = index;
            Label = label;
        }

        public bool IsEmpty => Tree.Count == 0 && Floating.Count == 0 && Minimized.Count == 0;

        /// <summary>Windows that are visible while the tag is viewed: tiled in tree order, then floating.</summary>
        public IEnumerable<long> VisibleWindows => Tree.Leaves().Concat(Floating);

        public IEnumerable<long> AllWindows => VisibleWindows.Concat(Minimized);

        public bool Contains(long windowId) => Tree.Contains(windowId) || Floating.Contains(windowId) || Minimized.Contains(windowId);
    }

    public class TagViewDelta
    {
        public static readonly TagViewDelta Empty = new TagViewDelta(new long[0], new long[0]);

        public IReadOnlyList<long> ToHide { get; }

        public IReadOnlyList<long> ToShow { get; }

        public TagViewDelta(IReadOnlyList<long> toHide, IReadOnlyList<long> toShow)
        {
            ToHide = toHide;
            ToShow = toShow;
        }
    }

    public enum WindowState
    {
        Tiled,
        Floating,
        Minimized
    }

    /// <summary>
    /// The tags of one monitor. Only one tag is viewed at a time.
    /// TODO: toggle-view (Win+Ctrl+N) to view several tags at once, as awesome does.
    /// </summary>
    public class TagSet
    {
        public const int DefaultTagCount = 10;

        private readonly Dictionary<long, int> _windowToTag = new Dictionary<long, int>();
        private readonly List<Tag> _tags;

        public IReadOnlyList<Tag> Tags => _tags;

        public int SelectedIndex { get; private set; }

        public int PreviousIndex { get; private set; }

        public Tag SelectedTag => _tags[SelectedIndex];

        /// <summary>
        /// The area windows are tiled into. Used to choose split orientations on insert.
        /// </summary>
        public RectI Area { get; set; }

        public TagSet(int count = DefaultTagCount)
        {
            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            _tags = new List<Tag>(count);
            for (int i = 0; i < count; i++)
            {
                _tags.Add(new Tag(i, DefaultLabel(i)));
            }
        }

        /// <summary>Tags display as "1".."9" then "0", matching the number row.</summary>
        public static string DefaultLabel(int index) => ((index + 1) % 10).ToString();

        public IEnumerable<long> Windows => _windowToTag.Keys;

        public bool Contains(long windowId) => _windowToTag.ContainsKey(windowId);

        public int? GetTagIndex(long windowId) => _windowToTag.TryGetValue(windowId, out int index) ? index : (int?)null;

        public Tag? GetTag(long windowId) => _windowToTag.TryGetValue(windowId, out int index) ? _tags[index] : null;

        public bool IsOccupied(int index) => !_tags[index].IsEmpty;

        public WindowState? GetState(long windowId)
        {
            Tag? tag = GetTag(windowId);
            if (tag == null) return null;
            if (tag.Tree.Contains(windowId)) return WindowState.Tiled;
            if (tag.Floating.Contains(windowId)) return WindowState.Floating;
            return WindowState.Minimized;
        }

        #region Membership

        public void AddWindow(long windowId, int tagIndex, bool floating)
        {
            AddWindow(windowId, tagIndex, floating ? WindowState.Floating : WindowState.Tiled);
        }

        public void AddWindow(long windowId, int tagIndex, WindowState state)
        {
            if (_windowToTag.ContainsKey(windowId))
            {
                return;
            }

            Tag tag = _tags[tagIndex];
            _windowToTag[windowId] = tagIndex;
            Place(tag, windowId, state);
        }

        private void Place(Tag tag, long windowId, WindowState state)
        {
            switch (state)
            {
                case WindowState.Tiled:
                    tag.Tree.Insert(windowId, tag.FocusedWindow, Area);
                    break;
                case WindowState.Floating:
                    tag.Floating.Add(windowId);
                    break;
                default:
                    tag.Minimized.Add(windowId);
                    break;
            }
        }

        private static WindowState Unplace(Tag tag, long windowId)
        {
            if (tag.Tree.Remove(windowId))
            {
                return WindowState.Tiled;
            }

            if (tag.Floating.Remove(windowId))
            {
                return WindowState.Floating;
            }

            tag.Minimized.Remove(windowId);
            return WindowState.Minimized;
        }

        /// <summary>
        /// Removes a window. Returns the index of the tag it was on, or null if it was unknown.
        /// </summary>
        public int? RemoveWindow(long windowId)
        {
            if (!_windowToTag.TryGetValue(windowId, out int index))
            {
                return null;
            }

            Tag tag = _tags[index];
            Unplace(tag, windowId);
            _windowToTag.Remove(windowId);

            if (tag.FocusedWindow == windowId) tag.FocusedWindow = null;
            if (tag.FullscreenWindow == windowId) tag.FullscreenWindow = null;
            if (tag.IsEmpty) tag.IsUrgent = false;

            return index;
        }

        /// <summary>
        /// Moves a window to another tag, next to that tag's last-focused window. Its state is kept.
        /// </summary>
        public bool MoveWindow(long windowId, int toTag)
        {
            if (!_windowToTag.TryGetValue(windowId, out int fromIndex) || fromIndex == toTag)
            {
                return false;
            }

            Tag from = _tags[fromIndex];
            WindowState state = Unplace(from, windowId);
            if (from.FocusedWindow == windowId) from.FocusedWindow = null;
            if (from.FullscreenWindow == windowId) from.FullscreenWindow = null;

            _windowToTag[windowId] = toTag;
            Place(_tags[toTag], windowId, state);
            return true;
        }

        public void SetState(long windowId, WindowState state)
        {
            Tag? tag = GetTag(windowId);
            if (tag == null || GetState(windowId) == state)
            {
                return;
            }

            Unplace(tag, windowId);
            Place(tag, windowId, state);
        }

        public void SetFocused(long windowId)
        {
            Tag? tag = GetTag(windowId);
            if (tag == null)
            {
                return;
            }

            tag.FocusedWindow = windowId;
            tag.IsUrgent = false;
            if (tag.Tree.Contains(windowId))
            {
                tag.Tree.FocusHint = windowId;
            }
        }

        #endregion

        #region Viewing

        /// <summary>
        /// Selects a tag and returns the windows to hide (old tag) and show (new tag). Minimized windows are left alone.
        /// </summary>
        public TagViewDelta View(int index)
        {
            if (index < 0 || index >= _tags.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (index == SelectedIndex)
            {
                return TagViewDelta.Empty;
            }

            Tag oldTag = SelectedTag;
            Tag newTag = _tags[index];

            PreviousIndex = SelectedIndex;
            SelectedIndex = index;
            newTag.IsUrgent = false;

            return new TagViewDelta(oldTag.VisibleWindows.ToList(), newTag.VisibleWindows.ToList());
        }

        public TagViewDelta ViewPrevious() => View(PreviousIndex);

        #endregion
    }
}
