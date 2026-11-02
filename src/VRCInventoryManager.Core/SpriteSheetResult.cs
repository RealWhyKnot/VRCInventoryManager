namespace VRCInventoryManager.Core;

internal sealed record SpriteSheetResult(
    byte[] PngBytes,
    int Frames,
    int FramesOverTime,
    int CanvasSize,
    int Grid,
    int CellSize);
