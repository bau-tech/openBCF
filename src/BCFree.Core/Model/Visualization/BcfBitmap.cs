namespace BCFree.Core.Model.Visualization;

public sealed record BcfBitmap(
    BcfBitmapType Type,
    string Reference,
    Point3D Location,
    Point3D Normal,
    Point3D Up,
    double Height);
