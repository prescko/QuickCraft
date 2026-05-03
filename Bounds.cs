namespace QuickCraft;

internal struct Bounds
{
    private readonly int xLen;
    private readonly int yLen;
    private readonly int outerLen;
    private int xPos;
    private int yPos;

    public Bounds(int xLen, int yLen, int outerLen)
    {
        this.xLen = xLen;
        this.yLen = yLen;
        this.outerLen = outerLen;
        xPos = 0;
        yPos = 0;
    }

    public void Align(int outer, int inner)
    {
        int x = outer % outerLen - inner % xLen;
        int y = outer / outerLen - inner / xLen;

        if (x >= 0 && y >= 0 && x + xLen < outerLen && y + yLen < outerLen)
        {
            xPos = x;
            yPos = y;
        }
    }

    public int ToInner(int outer)
    {
        return outer % outerLen - xPos + (outer / outerLen - yPos) * xLen;
    }

    public bool Contains(int index)
    {
        int x = index % outerLen;
        int y = index / outerLen;
        return x >= xPos && x < xPos + xLen && y >= yPos && y < yPos + yLen;
    }
}
