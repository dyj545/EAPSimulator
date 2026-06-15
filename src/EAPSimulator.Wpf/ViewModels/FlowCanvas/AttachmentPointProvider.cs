using System.Windows;

namespace EAPSimulator.Wpf.ViewModels.FlowCanvas;

public enum AttachmentDirection
{
    Top, Bottom, Left, Right,
    TopLeft, TopRight, BottomLeft, BottomRight
}

public record AttachmentPoint(
    string Id, AttachmentDirection Direction,
    double RelativeX, double RelativeY,
    double OffsetX = 0, double OffsetY = 0)
{
    public Point GetAbsolutePosition(double nodeX, double nodeY, double nodeWidth, double nodeHeight)
    {
        return new Point(
            nodeX + nodeWidth * RelativeX + OffsetX,
            nodeY + nodeHeight * RelativeY + OffsetY);
    }
}

public static class AttachmentPointProvider
{
    private const double Out = 3;
    private const double P1 = 0.25;
    private const double P2 = 0.50;
    private const double P3 = 0.75;

    public static List<AttachmentPoint> GetDefaultPoints()
    {
        return
        [
            new("top-l",    AttachmentDirection.Top,       P1, 0,   0, -Out),
            new("top-c",    AttachmentDirection.Top,       P2, 0,   0, -Out),
            new("top-r",    AttachmentDirection.Top,       P3, 0,   0, -Out),
            new("bot-l",    AttachmentDirection.Bottom,    P1, 1.0, 0,  Out),
            new("bot-c",    AttachmentDirection.Bottom,    P2, 1.0, 0,  Out),
            new("bot-r",    AttachmentDirection.Bottom,    P3, 1.0, 0,  Out),
            new("left-t",   AttachmentDirection.Left,      0,  P1, -Out, 0),
            new("left-c",   AttachmentDirection.Left,      0,  P2, -Out, 0),
            new("left-b",   AttachmentDirection.Left,      0,  P3, -Out, 0),
            new("right-t",  AttachmentDirection.Right,     1.0, P1, Out, 0),
            new("right-c",  AttachmentDirection.Right,     1.0, P2, Out, 0),
            new("right-b",  AttachmentDirection.Right,     1.0, P3, Out, 0),
            new("corner-tl",AttachmentDirection.TopLeft,    0,   0, -Out, -Out),
            new("corner-tr",AttachmentDirection.TopRight,   1.0, 0,  Out, -Out),
            new("corner-bl",AttachmentDirection.BottomLeft, 0,   1.0,-Out, Out),
            new("corner-br",AttachmentDirection.BottomRight,1.0, 1.0, Out, Out),
        ];
    }
}
