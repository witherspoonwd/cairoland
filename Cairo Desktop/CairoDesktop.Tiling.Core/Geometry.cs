using System;

namespace CairoDesktop.Tiling.Core
{
    public enum Direction
    {
        Left,
        Right,
        Up,
        Down
    }

    public enum Orientation
    {
        /// <summary>Children are side by side.</summary>
        Horizontal,

        /// <summary>Children are stacked.</summary>
        Vertical
    }

    public static class DirectionExtensions
    {
        public static Orientation Axis(this Direction direction)
        {
            return direction == Direction.Left || direction == Direction.Right ? Orientation.Horizontal : Orientation.Vertical;
        }

        /// <summary>True for Left and Up, the directions toward a split's First child.</summary>
        public static bool TowardFirst(this Direction direction)
        {
            return direction == Direction.Left || direction == Direction.Up;
        }
    }

    /// <summary>
    /// Integer rectangle in screen pixels.
    /// </summary>
    public readonly struct RectI : IEquatable<RectI>
    {
        public int X { get; }
        public int Y { get; }
        public int W { get; }
        public int H { get; }

        public int Right => X + W;
        public int Bottom => Y + H;

        public RectI(int x, int y, int w, int h)
        {
            X = x;
            Y = y;
            W = w;
            H = h;
        }

        public static RectI FromEdges(int left, int top, int right, int bottom)
        {
            return new RectI(left, top, right - left, bottom - top);
        }

        public (int X, int Y) Center => (X + W / 2, Y + H / 2);

        public bool IsEmpty => W <= 0 || H <= 0;

        public RectI Inset(int amount)
        {
            int w = Math.Max(0, W - amount * 2);
            int h = Math.Max(0, H - amount * 2);
            return new RectI(X + amount, Y + amount, w, h);
        }

        /// <summary>
        /// Splits into left and right parts with <paramref name="gap"/> pixels between them.
        /// </summary>
        public (RectI First, RectI Second) SplitH(double ratio, int gap)
        {
            int usable = Math.Max(0, W - gap);
            int first = (int)Math.Round(usable * ratio);
            int second = usable - first;
            return (new RectI(X, Y, first, H), new RectI(X + first + gap, Y, second, H));
        }

        /// <summary>
        /// Splits into top and bottom parts with <paramref name="gap"/> pixels between them.
        /// </summary>
        public (RectI First, RectI Second) SplitV(double ratio, int gap)
        {
            int usable = Math.Max(0, H - gap);
            int first = (int)Math.Round(usable * ratio);
            int second = usable - first;
            return (new RectI(X, Y, W, first), new RectI(X, Y + first + gap, W, second));
        }

        public (RectI First, RectI Second) Split(Orientation orientation, double ratio, int gap)
        {
            return orientation == Orientation.Horizontal ? SplitH(ratio, gap) : SplitV(ratio, gap);
        }

        public bool Contains(int x, int y)
        {
            return x >= X && x < Right && y >= Y && y < Bottom;
        }

        public bool Intersects(RectI other)
        {
            return X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
        }

        public bool Equals(RectI other)
        {
            return X == other.X && Y == other.Y && W == other.W && H == other.H;
        }

        public override bool Equals(object? obj)
        {
            return obj is RectI other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X;
                hash = hash * 397 ^ Y;
                hash = hash * 397 ^ W;
                hash = hash * 397 ^ H;
                return hash;
            }
        }

        public static bool operator ==(RectI a, RectI b) => a.Equals(b);

        public static bool operator !=(RectI a, RectI b) => !a.Equals(b);

        public override string ToString() => $"({X},{Y} {W}x{H})";
    }
}
