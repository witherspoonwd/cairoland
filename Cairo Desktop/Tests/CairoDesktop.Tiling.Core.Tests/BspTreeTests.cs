using CairoDesktop.Tiling.Core;

namespace CairoDesktop.Tiling.Core.Tests;

public class BspTreeTests
{
    private static readonly RectI Wide = new RectI(0, 0, 1000, 600);
    private static readonly RectI Tall = new RectI(0, 0, 600, 1000);

    private static BspTree Build(RectI area, params long[] ids)
    {
        var tree = new BspTree();
        foreach (long id in ids)
        {
            tree.Insert(id, null, area);
            tree.FocusHint = id;
        }
        return tree;
    }

    /// <summary>2x2 grid: 1 top-left, 2 top-right, 3 bottom-left, 4 bottom-right.</summary>
    private static BspTree Grid()
    {
        var tree = new BspTree();
        tree.Insert(1, null, Wide);       // [1]
        tree.Insert(2, 1, Wide);          // H[1 2]
        tree.Insert(3, 1, Wide);          // 1 is 500x600 -> V[1 3]
        tree.Insert(4, 2, Wide);          // 2 is 500x600 -> V[2 4]
        return tree;
    }

    [Fact]
    public void FirstWindowBecomesRoot()
    {
        var tree = Build(Wide, 1);
        Assert.IsType<BspLeaf>(tree.Root);
        Assert.Equal(new RectI(0, 0, 1000, 600), tree.Layout(Wide, 0)[1]);
    }

    [Fact]
    public void InsertSplitsTargetWithNewWindowSecond()
    {
        var tree = Build(Wide, 1, 2);
        var split = Assert.IsType<BspSplit>(tree.Root);
        Assert.Equal(1, ((BspLeaf)split.First).WindowId);
        Assert.Equal(2, ((BspLeaf)split.Second).WindowId);
        Assert.Equal(Orientation.Horizontal, split.Orientation);
    }

    [Fact]
    public void OrientationFollowsAspectRatio()
    {
        Assert.Equal(Orientation.Vertical, ((BspSplit)Build(Tall, 1, 2).Root!).Orientation);

        // wide area: 1|2, then 2 (500x600) is taller than wide so it splits vertically
        var tree = Build(Wide, 1, 2, 3);
        var root = (BspSplit)tree.Root!;
        Assert.Equal(Orientation.Horizontal, root.Orientation);
        var inner = Assert.IsType<BspSplit>(root.Second);
        Assert.Equal(Orientation.Vertical, inner.Orientation);
    }

    [Fact]
    public void InsertWithoutTargetOrFocusUsesDeepestRightmostLeaf()
    {
        var tree = new BspTree();
        tree.Insert(1, null, Wide);
        tree.Insert(2, null, Wide);
        tree.Insert(3, null, Wide);
        Assert.Equal("H[1 V[2 3]]", tree.ToString());
    }

    [Fact]
    public void InsertUsesFocusHint()
    {
        var tree = new BspTree();
        tree.Insert(1, null, Wide);
        tree.Insert(2, null, Wide);
        tree.FocusHint = 1;
        tree.Insert(3, null, Wide);
        Assert.Equal("H[V[1 3] 2]", tree.ToString());
    }

    [Fact]
    public void RemovePromotesSibling()
    {
        var tree = Build(Wide, 1, 2, 3); // H[1 V[2 3]]
        Assert.True(tree.Remove(2));
        Assert.Equal("H[1 3]", tree.ToString());
        Assert.Same(tree.Root, ((BspSplit)tree.Root!).Second.Parent);

        Assert.True(tree.Remove(1));
        var leaf = Assert.IsType<BspLeaf>(tree.Root);
        Assert.Equal(3, leaf.WindowId);
        Assert.Null(leaf.Parent);

        Assert.True(tree.Remove(3));
        Assert.Null(tree.Root);
        Assert.Equal(0, tree.Count);
        Assert.False(tree.Remove(3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(13)]
    public void LayoutHasGapsNoOverlapAndFillsArea(int gap)
    {
        var tree = Build(Wide, 1, 2, 3, 4, 5);
        var layout = tree.Layout(Wide, gap);
        Assert.Equal(5, layout.Count);

        var rects = layout.Values.ToList();
        for (int i = 0; i < rects.Count; i++)
        {
            Assert.False(rects[i].IsEmpty);
            for (int j = i + 1; j < rects.Count; j++)
            {
                Assert.False(rects[i].Intersects(rects[j]), $"{rects[i]} overlaps {rects[j]}");
            }
        }

        // inside the outer gap
        RectI inner = Wide.Inset(gap);
        Assert.All(rects, r => Assert.True(r.X >= inner.X && r.Y >= inner.Y && r.Right <= inner.Right && r.Bottom <= inner.Bottom));

        // windows plus gaps cover the area: count uncovered pixels on a coarse grid
        if (gap == 0)
        {
            long area = rects.Sum(r => (long)r.W * r.H);
            Assert.Equal((long)Wide.W * Wide.H, area);
        }
        else
        {
            // each pair of horizontal/vertical neighbours is exactly `gap` apart
            foreach (var a in layout)
            {
                long? right = BspTree.FindNeighbor(a.Key, Direction.Right, layout);
                if (right.HasValue)
                {
                    Assert.Equal(gap, layout[right.Value].X - a.Value.Right);
                }
            }
        }
    }

    [Fact]
    public void LayoutRespectsOuterGap()
    {
        var layout = Build(Wide, 1, 2).Layout(Wide, 10);
        Assert.Equal(new RectI(10, 10, 485, 580), layout[1]);
        Assert.Equal(new RectI(505, 10, 485, 580), layout[2]);
    }

    [Fact]
    public void NeighborsInGrid()
    {
        var layout = Grid().Layout(Wide, 6);
        Assert.Equal(2L, BspTree.FindNeighbor(1, Direction.Right, layout));
        Assert.Equal(3L, BspTree.FindNeighbor(1, Direction.Down, layout));
        Assert.Null(BspTree.FindNeighbor(1, Direction.Left, layout));
        Assert.Null(BspTree.FindNeighbor(1, Direction.Up, layout));
        Assert.Equal(3L, BspTree.FindNeighbor(4, Direction.Left, layout));
        Assert.Equal(2L, BspTree.FindNeighbor(4, Direction.Up, layout));
        Assert.Equal(1L, BspTree.FindNeighbor(2, Direction.Left, layout));
    }

    [Fact]
    public void NeighborsInAsymmetricLayout()
    {
        // H[1 V[2 3]]: 1 is full height on the left, 2 and 3 stacked on the right
        var tree = Build(Wide, 1, 2, 3);
        var layout = tree.Layout(Wide, 0);
        Assert.Equal(1L, BspTree.FindNeighbor(2, Direction.Left, layout));
        Assert.Equal(1L, BspTree.FindNeighbor(3, Direction.Left, layout));
        // both 2 and 3 touch 1; equal distance and equal overlap so either is acceptable, but must be one of them
        Assert.Contains(BspTree.FindNeighbor(1, Direction.Right, layout)!.Value, new long[] { 2, 3 });

        // make 3 taller: largest overlap wins
        tree.Resize(2, Direction.Up, 0.2);
        layout = tree.Layout(Wide, 0);
        Assert.Equal(3L, BspTree.FindNeighbor(1, Direction.Right, layout));
    }

    [Fact]
    public void SwapExchangesWindows()
    {
        var tree = Build(Wide, 1, 2, 3);
        var before = tree.Layout(Wide, 0);
        Assert.True(tree.Swap(1, 3));
        var after = tree.Layout(Wide, 0);
        Assert.Equal(before[1], after[3]);
        Assert.Equal(before[3], after[1]);
        Assert.Equal("H[3 V[2 1]]", tree.ToString());
        Assert.False(tree.Swap(1, 99));
    }

    [Fact]
    public void MoveIntoNeighbor()
    {
        // H[1 V[2 3]]; move 1 right: nearest right neighbour is 2 (500x300, wide) -> horizontal split,
        // along the movement axis, so 1 goes on the side facing right (Second)
        var tree = Build(Wide, 1, 2, 3);
        Assert.True(tree.Move(1, Direction.Right, Wide, 0));
        Assert.Equal("V[H[2 1] 3]", tree.ToString());
        Assert.Equal(3, tree.Count);
    }

    [Fact]
    public void MoveFacingDirectionTakesFirstForLeftAndUp()
    {
        // V[1 2] in a tall area; move 2 up: once 2 is removed, 1 fills the tall area -> vertical split,
        // along the movement axis, so 2 goes on the side facing up (First)
        var tree = Build(Tall, 1, 2);
        Assert.True(tree.Move(2, Direction.Up, Tall, 0));
        Assert.Equal("V[2 1]", tree.ToString());

        // H[1 2] in a wide area then 3 below 2: move 3 left into 1 (500x600, tall) -> vertical, perpendicular
        var wide = Build(Wide, 1, 2);
        wide.Insert(3, 1, Wide); // H[V[1 3] 2]
        Assert.True(wide.Move(2, Direction.Left, Wide, 0));
        Assert.Equal(3, wide.Count);
        Assert.False(wide.Move(1, Direction.Up, Wide, 0));
    }

    [Fact]
    public void MoveWithoutNeighborFails()
    {
        var tree = Build(Wide, 1, 2);
        Assert.False(tree.Move(1, Direction.Left, Wide, 0));
        Assert.Equal("H[1 2]", tree.ToString());
    }

    [Fact]
    public void ResizeMovesTheMatchingEdge()
    {
        var tree = Build(Wide, 1, 2);
        var root = (BspSplit)tree.Root!;

        Assert.True(tree.Resize(1, Direction.Right, 0.05)); // grow 1 to the right
        Assert.Equal(0.55, root.Ratio, 3);
        Assert.True(tree.Resize(2, Direction.Left, 0.05));  // grow 2 to the left
        Assert.Equal(0.50, root.Ratio, 3);
        Assert.True(tree.Resize(2, Direction.Right, 0.05)); // fallback: move the only edge right, shrinking 2
        Assert.Equal(0.55, root.Ratio, 3);

        Assert.False(tree.Resize(1, Direction.Up, 0.05)); // no vertical split
    }

    [Fact]
    public void ResizePicksNearestAncestorOnTheRightSide()
    {
        // H[1 V[2 3]]: resizing 3 left must use the root split (3 is in its Second), not the inner vertical one
        var tree = Build(Wide, 1, 2, 3);
        var root = (BspSplit)tree.Root!;
        var inner = (BspSplit)root.Second;
        Assert.True(tree.Resize(3, Direction.Left, 0.1));
        Assert.Equal(0.4, root.Ratio, 3);
        Assert.Equal(0.5, inner.Ratio, 3);

        Assert.True(tree.Resize(3, Direction.Up, 0.1));
        Assert.Equal(0.4, inner.Ratio, 3);
    }

    [Fact]
    public void ResizeClampsRatio()
    {
        var tree = Build(Wide, 1, 2);
        var root = (BspSplit)tree.Root!;
        for (int i = 0; i < 20; i++) tree.Resize(1, Direction.Right, 0.05);
        Assert.Equal(BspSplit.MaxRatio, root.Ratio, 3);
        Assert.False(tree.Resize(1, Direction.Right, 0.05));
        for (int i = 0; i < 20; i++) tree.Resize(1, Direction.Left, 0.05);
        Assert.Equal(BspSplit.MinRatio, root.Ratio, 3);
    }

    [Fact]
    public void ToggleOrientationAndEqualize()
    {
        var tree = Build(Wide, 1, 2);
        tree.Resize(1, Direction.Right, 0.2);
        Assert.True(tree.ToggleOrientation(1));
        Assert.Equal(Orientation.Vertical, ((BspSplit)tree.Root!).Orientation);
        tree.Equalize();
        Assert.Equal(0.5, ((BspSplit)tree.Root!).Ratio, 3);
        Assert.False(Build(Wide, 1).ToggleOrientation(1));
    }

    [Fact]
    public void SetRatioFromRectFollowsMouseResize()
    {
        var tree = Build(Wide, 1, 2);
        var layout = tree.Layout(Wide, 0);
        var r1 = layout[1];
        // user dragged 1's right edge to x=700
        Assert.True(tree.SetRatioFromRect(1, RectI.FromEdges(r1.X, r1.Y, 700, r1.Bottom), Wide, 0));
        Assert.Equal(0.7, ((BspSplit)tree.Root!).Ratio, 3);
        Assert.Equal(700, tree.Layout(Wide, 0)[1].Right);

        // with gaps: drag 2's left edge to x=400
        var gapped = Build(Wide, 1, 2);
        var g2 = gapped.Layout(Wide, 10)[2];
        Assert.True(gapped.SetRatioFromRect(2, RectI.FromEdges(400, g2.Y, g2.Right, g2.Bottom), Wide, 10));
        Assert.Equal(400, gapped.Layout(Wide, 10)[2].X);
    }

    [Fact]
    public void LeavesAreInOrder()
    {
        Assert.Equal(new long[] { 1, 3, 2, 4 }, Grid().Leaves());
    }
}
