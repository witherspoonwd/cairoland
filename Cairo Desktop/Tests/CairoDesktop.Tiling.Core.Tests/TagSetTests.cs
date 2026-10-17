using CairoDesktop.Tiling.Core;

namespace CairoDesktop.Tiling.Core.Tests;

public class TagSetTests
{
    private static TagSet Create()
    {
        return new TagSet { Area = new RectI(0, 0, 1000, 600) };
    }

    [Fact]
    public void HasTenTagsLabelledLikeTheNumberRow()
    {
        var tags = Create();
        Assert.Equal(10, tags.Tags.Count);
        Assert.Equal("1", tags.Tags[0].Label);
        Assert.Equal("9", tags.Tags[8].Label);
        Assert.Equal("0", tags.Tags[9].Label);
        Assert.Equal(0, tags.SelectedIndex);
    }

    [Fact]
    public void AddAndRemoveWindows()
    {
        var tags = Create();
        tags.AddWindow(1, 0, floating: false);
        tags.AddWindow(2, 0, floating: true);
        tags.AddWindow(3, 4, WindowState.Minimized);

        Assert.True(tags.Tags[0].Tree.Contains(1));
        Assert.Contains(2L, tags.Tags[0].Floating);
        Assert.Contains(3L, tags.Tags[4].Minimized);
        Assert.True(tags.IsOccupied(0));
        Assert.True(tags.IsOccupied(4));
        Assert.False(tags.IsOccupied(1));
        Assert.Equal(4, tags.GetTagIndex(3));

        Assert.Equal(0, tags.RemoveWindow(1));
        Assert.Null(tags.RemoveWindow(1));
        Assert.False(tags.Contains(1));
    }

    [Fact]
    public void ViewReturnsDelta()
    {
        var tags = Create();
        tags.AddWindow(1, 0, false);
        tags.AddWindow(2, 0, true);
        tags.AddWindow(3, 0, WindowState.Minimized);
        tags.AddWindow(4, 2, false);

        var delta = tags.View(2);
        Assert.Equal(new long[] { 1, 2 }, delta.ToHide);
        Assert.Equal(new long[] { 4 }, delta.ToShow);
        Assert.Equal(2, tags.SelectedIndex);
        Assert.Equal(0, tags.PreviousIndex);

        Assert.Empty(tags.View(2).ToHide);
        Assert.Empty(tags.View(2).ToShow);

        var back = tags.ViewPrevious();
        Assert.Equal(new long[] { 4 }, back.ToHide);
        Assert.Equal(new long[] { 1, 2 }, back.ToShow);
        Assert.Equal(0, tags.SelectedIndex);
        Assert.Equal(2, tags.PreviousIndex);
    }

    [Fact]
    public void MoveWindowToAnotherTagKeepsStateAndInsertsNextToFocused()
    {
        var tags = Create();
        tags.AddWindow(1, 0, false);
        tags.AddWindow(2, 0, false);
        tags.AddWindow(10, 3, false);
        tags.AddWindow(11, 3, false);
        tags.SetFocused(10);
        tags.AddWindow(5, 0, true);

        Assert.True(tags.MoveWindow(2, 3));
        Assert.Equal(3, tags.GetTagIndex(2));
        Assert.False(tags.Tags[0].Tree.Contains(2));
        // inserted by splitting the focused window 10
        var leaf10 = tags.Tags[3].Tree.GetLeaf(10)!;
        var parent = leaf10.Parent!;
        Assert.Equal(2, ((BspLeaf)parent.Second).WindowId);

        Assert.True(tags.MoveWindow(5, 3));
        Assert.Contains(5L, tags.Tags[3].Floating);

        Assert.False(tags.MoveWindow(5, 3));
        Assert.False(tags.MoveWindow(99, 3));
    }

    [Fact]
    public void MovingTheFocusedWindowClearsFocusOnTheOldTag()
    {
        var tags = Create();
        tags.AddWindow(1, 0, false);
        tags.SetFocused(1);
        tags.MoveWindow(1, 1);
        Assert.Null(tags.Tags[0].FocusedWindow);
        Assert.False(tags.IsOccupied(0));
    }

    [Fact]
    public void SetStateMovesBetweenTreeFloatingAndMinimized()
    {
        var tags = Create();
        tags.AddWindow(1, 0, false);
        tags.AddWindow(2, 0, false);

        tags.SetState(1, WindowState.Floating);
        Assert.Equal(WindowState.Floating, tags.GetState(1));
        Assert.False(tags.Tags[0].Tree.Contains(1));

        tags.SetState(1, WindowState.Minimized);
        Assert.Equal(WindowState.Minimized, tags.GetState(1));

        tags.SetState(1, WindowState.Tiled);
        Assert.True(tags.Tags[0].Tree.Contains(1));
        Assert.Equal(2, tags.Tags[0].Tree.Count);
    }

    [Fact]
    public void ViewingAnUrgentTagClearsUrgency()
    {
        var tags = Create();
        tags.AddWindow(1, 5, false);
        tags.Tags[5].IsUrgent = true;
        tags.View(5);
        Assert.False(tags.Tags[5].IsUrgent);
    }
}
