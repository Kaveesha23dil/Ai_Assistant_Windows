namespace WindowsAIAssistant.Core.Models.Computer;

public readonly record struct ComputerRect(int X, int Y, int Width, int Height)
{
    public static ComputerRect Empty => new(0, 0, 0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public int CenterX => X + (Width / 2);

    public int CenterY => Y + (Height / 2);

    public bool Contains(int x, int y) =>
        x >= X && x < X + Width && y >= Y && y < Y + Height;
}
