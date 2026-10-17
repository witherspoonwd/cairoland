using System;
using System.Collections.Generic;
using System.Linq;

namespace CairoDesktop.Tiling.Core
{
    public abstract class BspNode
    {
        public BspSplit? Parent { get; internal set; }
    }

    public sealed class BspLeaf : BspNode
    {
        public long WindowId { get; internal set; }

        internal BspLeaf(long windowId)
        {
            WindowId = windowId;
        }

        public override string ToString() => WindowId.ToString();
    }

    public sealed class BspSplit : BspNode
    {
        public const double MinRatio = 0.1;
        public const double MaxRatio = 0.9;

        private double _ratio = 0.5;

        public Orientation Orientation { get; internal set; }

        public double Ratio
        {
            get => _ratio;
            internal set => _ratio = Math.Max(MinRatio, Math.Min(MaxRatio, value));
        }

        public BspNode First { get; internal set; }

        public BspNode Second { get; internal set; }

        internal BspSplit(Orientation orientation, BspNode first, BspNode second)
        {
            Orientation = orientation;
            First = first;
            Second = second;
            first.Parent = this;
            second.Parent = this;
        }

        internal void Replace(BspNode child, BspNode replacement)
        {
            if (First == child)
            {
                First = replacement;
            }
            else if (Second == child)
            {
                Second = replacement;
            }
            else
            {
                throw new InvalidOperationException("Node is not a child of this split.");
            }

            replacement.Parent = this;
        }

        public override string ToString() => $"{(Orientation == Orientation.Horizontal ? "H" : "V")}[{First} {Second}]";
    }

    /// <summary>
    /// Binary space partition of the windows on one tag.
    /// </summary>
    public class BspTree
    {
        private readonly Dictionary<long, BspLeaf> _leaves = new Dictionary<long, BspLeaf>();

        public BspNode? Root { get; private set; }

        /// <summary>
        /// The most recently focused window, used as the default insertion target.
        /// </summary>
        public long? FocusHint { get; set; }

        public int Count => _leaves.Count;

        public bool Contains(long windowId) => _leaves.ContainsKey(windowId);

        public BspLeaf? GetLeaf(long windowId) => _leaves.TryGetValue(windowId, out var leaf) ? leaf : null;

        #region Insert and remove

        /// <summary>
        /// Adds a window by splitting the target leaf (or the focused leaf, or the deepest right-most leaf).
        /// The new window takes the Second (right or bottom) half.
        /// </summary>
        public void Insert(long windowId, long? targetLeafId, RectI area)
        {
            if (_leaves.ContainsKey(windowId))
            {
                return;
            }

            BspLeaf newLeaf = new BspLeaf(windowId);
            _leaves[windowId] = newLeaf;

            if (Root == null)
            {
                Root = newLeaf;
                return;
            }

            BspLeaf target = ResolveTarget(targetLeafId);
            Dictionary<BspNode, RectI> rects = ComputeNodeRects(area, 0);
            RectI targetRect = rects.TryGetValue(target, out var r) ? r : area;
            Orientation orientation = targetRect.W >= targetRect.H ? Orientation.Horizontal : Orientation.Vertical;

            SplitLeaf(target, newLeaf, orientation, newFirst: false);
        }

        private BspLeaf ResolveTarget(long? targetLeafId)
        {
            if (targetLeafId.HasValue && _leaves.TryGetValue(targetLeafId.Value, out var target))
            {
                return target;
            }

            if (FocusHint.HasValue && _leaves.TryGetValue(FocusHint.Value, out target))
            {
                return target;
            }

            // dwindle-like fallback: follow Second children to the deepest right-most leaf
            BspNode node = Root!;
            while (node is BspSplit split)
            {
                node = split.Second;
            }

            return (BspLeaf)node;
        }

        private void SplitLeaf(BspLeaf target, BspLeaf newLeaf, Orientation orientation, bool newFirst)
        {
            BspSplit? parent = target.Parent;
            BspSplit split = newFirst
                ? new BspSplit(orientation, newLeaf, target)
                : new BspSplit(orientation, target, newLeaf);

            if (parent == null)
            {
                Root = split;
                split.Parent = null;
            }
            else
            {
                parent.Replace(target, split);
            }
        }

        /// <summary>
        /// Removes a window; its sibling takes the parent split's place.
        /// </summary>
        public bool Remove(long windowId)
        {
            if (!_leaves.TryGetValue(windowId, out var leaf))
            {
                return false;
            }

            _leaves.Remove(windowId);

            if (FocusHint == windowId)
            {
                FocusHint = null;
            }

            BspSplit? parent = leaf.Parent;
            if (parent == null)
            {
                Root = null;
                return true;
            }

            BspNode sibling = parent.First == leaf ? parent.Second : parent.First;
            BspSplit? grandParent = parent.Parent;

            if (grandParent == null)
            {
                Root = sibling;
                sibling.Parent = null;
            }
            else
            {
                grandParent.Replace(parent, sibling);
            }

            return true;
        }

        #endregion

        #region Layout

        /// <summary>
        /// Computes window rects: <paramref name="gap"/> around the outside and between every pair of windows.
        /// </summary>
        public Dictionary<long, RectI> Layout(RectI area, int gap)
        {
            Dictionary<long, RectI> result = new Dictionary<long, RectI>();
            foreach (KeyValuePair<BspNode, RectI> pair in ComputeNodeRects(area, gap))
            {
                if (pair.Key is BspLeaf leaf)
                {
                    result[leaf.WindowId] = pair.Value;
                }
            }

            return result;
        }

        private Dictionary<BspNode, RectI> ComputeNodeRects(RectI area, int gap)
        {
            Dictionary<BspNode, RectI> rects = new Dictionary<BspNode, RectI>();
            if (Root != null)
            {
                ComputeNodeRects(Root, area.Inset(gap), gap, rects);
            }

            return rects;
        }

        private static void ComputeNodeRects(BspNode node, RectI rect, int gap, Dictionary<BspNode, RectI> rects)
        {
            rects[node] = rect;

            if (node is BspSplit split)
            {
                (RectI first, RectI second) = rect.Split(split.Orientation, split.Ratio, gap);
                ComputeNodeRects(split.First, first, gap, rects);
                ComputeNodeRects(split.Second, second, gap, rects);
            }
        }

        #endregion

        #region Navigation

        /// <summary>
        /// Leaves in in-order traversal, for next/previous focus cycling.
        /// </summary>
        public IReadOnlyList<long> Leaves()
        {
            List<long> result = new List<long>(_leaves.Count);
            if (Root != null)
            {
                CollectLeaves(Root, result);
            }

            return result;
        }

        private static void CollectLeaves(BspNode node, List<long> result)
        {
            if (node is BspLeaf leaf)
            {
                result.Add(leaf.WindowId);
            }
            else if (node is BspSplit split)
            {
                CollectLeaves(split.First, result);
                CollectLeaves(split.Second, result);
            }
        }

        /// <summary>
        /// Finds the window that lies strictly in <paramref name="direction"/> and overlaps the source on the
        /// perpendicular axis. Closest edge wins, then largest overlap.
        /// </summary>
        public static long? FindNeighbor(long windowId, Direction direction, IReadOnlyDictionary<long, RectI> layout)
        {
            if (!layout.TryGetValue(windowId, out RectI source))
            {
                return null;
            }

            long? best = null;
            int bestDistance = int.MaxValue;
            int bestOverlap = int.MinValue;

            foreach (KeyValuePair<long, RectI> pair in layout)
            {
                if (pair.Key == windowId)
                {
                    continue;
                }

                RectI c = pair.Value;
                int distance;
                int overlap;

                switch (direction)
                {
                    case Direction.Left:
                        if (c.Right > source.X) continue;
                        distance = source.X - c.Right;
                        overlap = Math.Min(source.Bottom, c.Bottom) - Math.Max(source.Y, c.Y);
                        break;
                    case Direction.Right:
                        if (c.X < source.Right) continue;
                        distance = c.X - source.Right;
                        overlap = Math.Min(source.Bottom, c.Bottom) - Math.Max(source.Y, c.Y);
                        break;
                    case Direction.Up:
                        if (c.Bottom > source.Y) continue;
                        distance = source.Y - c.Bottom;
                        overlap = Math.Min(source.Right, c.Right) - Math.Max(source.X, c.X);
                        break;
                    default:
                        if (c.Y < source.Bottom) continue;
                        distance = c.Y - source.Bottom;
                        overlap = Math.Min(source.Right, c.Right) - Math.Max(source.X, c.X);
                        break;
                }

                if (overlap <= 0)
                {
                    continue;
                }

                if (distance < bestDistance || (distance == bestDistance && overlap > bestOverlap))
                {
                    best = pair.Key;
                    bestDistance = distance;
                    bestOverlap = overlap;
                }
            }

            return best;
        }

        public long? FindNeighbor(long windowId, Direction direction, RectI area, int gap)
        {
            return FindNeighbor(windowId, direction, Layout(area, gap));
        }

        #endregion

        #region Rearranging

        public bool Swap(long a, long b)
        {
            if (a == b || !_leaves.TryGetValue(a, out var leafA) || !_leaves.TryGetValue(b, out var leafB))
            {
                return false;
            }

            leafA.WindowId = b;
            leafB.WindowId = a;
            _leaves[a] = leafB;
            _leaves[b] = leafA;
            return true;
        }

        /// <summary>
        /// Moves a window into its neighbour's space: the window is removed and re-inserted by splitting the
        /// neighbour. The new split's orientation follows the neighbour's aspect ratio. When that orientation lies
        /// along the movement axis, the window takes the side facing <paramref name="direction"/>; otherwise it
        /// takes the Second half.
        /// </summary>
        public bool Move(long windowId, Direction direction, RectI area, int gap)
        {
            long? neighbor = FindNeighbor(windowId, direction, area, gap);
            if (!neighbor.HasValue)
            {
                return false;
            }

            Remove(windowId);

            BspLeaf target = _leaves[neighbor.Value];
            Dictionary<BspNode, RectI> rects = ComputeNodeRects(area, gap);
            RectI targetRect = rects[target];
            Orientation orientation = targetRect.W >= targetRect.H ? Orientation.Horizontal : Orientation.Vertical;
            bool newFirst = orientation == direction.Axis() && direction.TowardFirst();

            BspLeaf newLeaf = new BspLeaf(windowId);
            _leaves[windowId] = newLeaf;
            SplitLeaf(target, newLeaf, orientation, newFirst);
            return true;
        }

        /// <summary>
        /// Moves the window's edge in <paramref name="direction"/> by <paramref name="delta"/> (a ratio, e.g. 0.05).
        /// Uses the nearest ancestor split on that axis whose boundary is that edge of the window, falling back to
        /// the nearest split on that axis.
        /// </summary>
        public bool Resize(long windowId, Direction direction, double delta)
        {
            if (!_leaves.TryGetValue(windowId, out var leaf))
            {
                return false;
            }

            Orientation axis = direction.Axis();
            // Right/Down edges belong to splits where the window is in First, Left/Up edges where it is in Second
            bool wantFirst = !direction.TowardFirst();

            BspSplit? fallback = null;
            BspNode child = leaf;
            for (BspSplit? split = leaf.Parent; split != null; child = split, split = split.Parent)
            {
                if (split.Orientation != axis)
                {
                    continue;
                }

                fallback ??= split;

                bool inFirst = split.First == child;
                if (inFirst == wantFirst)
                {
                    fallback = split;
                    break;
                }
            }

            if (fallback == null)
            {
                return false;
            }

            double old = fallback.Ratio;
            fallback.Ratio = old + (direction.TowardFirst() ? -delta : delta);
            return Math.Abs(fallback.Ratio - old) > double.Epsilon;
        }

        public bool ToggleOrientation(long windowId)
        {
            if (!_leaves.TryGetValue(windowId, out var leaf) || leaf.Parent == null)
            {
                return false;
            }

            leaf.Parent.Orientation = leaf.Parent.Orientation == Orientation.Horizontal ? Orientation.Vertical : Orientation.Horizontal;
            return true;
        }

        public void Equalize()
        {
            if (Root != null)
            {
                Equalize(Root);
            }
        }

        private static void Equalize(BspNode node)
        {
            if (node is BspSplit split)
            {
                split.Ratio = 0.5;
                Equalize(split.First);
                Equalize(split.Second);
            }
        }

        /// <summary>
        /// After the user resized a window with the mouse, moves the split boundaries next to each edge that changed.
        /// </summary>
        public bool SetRatioFromRect(long windowId, RectI newRect, RectI area, int gap)
        {
            if (!_leaves.TryGetValue(windowId, out var leaf))
            {
                return false;
            }

            Dictionary<BspNode, RectI> rects = ComputeNodeRects(area, gap);
            RectI old = rects[leaf];
            bool changed = false;

            if (newRect.X != old.X)
            {
                changed |= SetBoundary(leaf, Orientation.Horizontal, windowInFirst: false, boundary: newRect.X - gap, rects, gap);
            }

            if (newRect.Right != old.Right)
            {
                changed |= SetBoundary(leaf, Orientation.Horizontal, windowInFirst: true, boundary: newRect.Right, rects, gap);
            }

            if (newRect.Y != old.Y)
            {
                changed |= SetBoundary(leaf, Orientation.Vertical, windowInFirst: false, boundary: newRect.Y - gap, rects, gap);
            }

            if (newRect.Bottom != old.Bottom)
            {
                changed |= SetBoundary(leaf, Orientation.Vertical, windowInFirst: true, boundary: newRect.Bottom, rects, gap);
            }

            return changed;
        }

        /// <param name="boundary">The new end coordinate of the split's First child.</param>
        private static bool SetBoundary(BspLeaf leaf, Orientation axis, bool windowInFirst, int boundary, Dictionary<BspNode, RectI> rects, int gap)
        {
            BspNode child = leaf;
            for (BspSplit? split = leaf.Parent; split != null; child = split, split = split.Parent)
            {
                if (split.Orientation != axis || (split.First == child) != windowInFirst)
                {
                    continue;
                }

                RectI rect = rects[split];
                int start = axis == Orientation.Horizontal ? rect.X : rect.Y;
                int size = (axis == Orientation.Horizontal ? rect.W : rect.H) - gap;
                if (size <= 0)
                {
                    return false;
                }

                split.Ratio = (double)(boundary - start) / size;
                return true;
            }

            return false;
        }

        #endregion

        public override string ToString() => Root?.ToString() ?? "(empty)";
    }
}
