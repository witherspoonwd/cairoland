using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace CairoDesktop.MenuBar.Launcher
{
    public class TextSegment
    {
        public string Text { get; }

        public bool IsMatch { get; }

        public TextSegment(string text, bool isMatch)
        {
            Text = text;
            IsMatch = isMatch;
        }

        /// <summary>
        /// Splits text into runs, marking the characters at <paramref name="positions"/> as matches.
        /// </summary>
        public static List<TextSegment> Split(string text, IReadOnlyList<int> positions)
        {
            List<TextSegment> segments = new List<TextSegment>();
            HashSet<int> matched = new HashSet<int>(positions);
            int start = 0;

            for (int i = 1; i <= text.Length; i++)
            {
                if (i == text.Length || matched.Contains(i) != matched.Contains(start))
                {
                    segments.Add(new TextSegment(text.Substring(start, i - start), matched.Contains(start)));
                    start = i;
                }
            }

            return segments;
        }
    }

    /// <summary>
    /// Attached property that fills a TextBlock with runs, bolding the matched ones.
    /// </summary>
    public static class HighlightedText
    {
        public static readonly DependencyProperty SegmentsProperty = DependencyProperty.RegisterAttached(
            "Segments", typeof(IEnumerable<TextSegment>), typeof(HighlightedText), new PropertyMetadata(null, OnSegmentsChanged));

        public static IEnumerable<TextSegment> GetSegments(DependencyObject obj) => (IEnumerable<TextSegment>)obj.GetValue(SegmentsProperty);

        public static void SetSegments(DependencyObject obj, IEnumerable<TextSegment> value) => obj.SetValue(SegmentsProperty, value);

        private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is TextBlock textBlock))
            {
                return;
            }

            textBlock.Inlines.Clear();
            if (!(e.NewValue is IEnumerable<TextSegment> segments))
            {
                return;
            }

            foreach (TextSegment segment in segments)
            {
                Run run = new Run(segment.Text);
                if (segment.IsMatch)
                {
                    run.FontWeight = FontWeights.Bold;
                }

                textBlock.Inlines.Add(run);
            }
        }
    }
}
