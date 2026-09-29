namespace WordBubble;

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

public static class Geometry
{
    public static PixelRect Beside(PixelRect anchor, PixelRect card, PixelRect area, int gap = 2)
    {
        var right = (long)anchor.X + anchor.Width + gap;
        var x = right + card.Width <= (long)area.X + area.Width ? right : (long)anchor.X - card.Width - gap;
        var y = (long)anchor.Y + anchor.Height - card.Height;
        return Clamp(card with { X = (int)Math.Clamp(x, int.MinValue, int.MaxValue), Y = (int)Math.Clamp(y, int.MinValue, int.MaxValue) }, area);
    }

    public static PixelRect Clamp(PixelRect value, PixelRect area)
    {
        var width = Math.Clamp(value.Width, 1, Math.Max(1, area.Width));
        var height = Math.Clamp(value.Height, 1, Math.Max(1, area.Height));
        // Long intermediates also tolerate corrupted or out-of-date persisted positions.
        var x = (int)Math.Clamp((long)value.X, area.X, (long)area.X + area.Width - width);
        var y = (int)Math.Clamp((long)value.Y, area.Y, (long)area.Y + area.Height - height);
        return new PixelRect(x, y, width, height);
    }

    public static PixelRect Snap(PixelRect value, PixelRect area, int threshold)
    {
        value = Clamp(value, area);
        var right = area.X + area.Width - value.Width;
        var bottom = area.Y + area.Height - value.Height;
        return value with
        {
            X = value.X - area.X <= threshold ? area.X : right - value.X <= threshold ? right : value.X,
            Y = value.Y - area.Y <= threshold ? area.Y : bottom - value.Y <= threshold ? bottom : value.Y
        };
    }
}
